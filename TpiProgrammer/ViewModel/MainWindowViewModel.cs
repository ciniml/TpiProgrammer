using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Codeplex.Reactive;
using Codeplex.Reactive.Extensions;
using Codeplex.Reactive.Notifiers;
using Livet.Messaging.IO;
using TpiProgrammer.Model;
using TpiProgrammer.Model.Devices;
using TpiProgrammer.View;

namespace TpiProgrammer.ViewModel
{
    public class MainWindowViewModel : Livet.ViewModel
    {
        public static readonly string ErrorOccuredMessageKey = nameof(ErrorOccuredMessageKey);

        public ReadOnlyObservableCollection<TpiCommunication> ProgrammingDevices => TpiCommunication.Devices;
        public ReactiveProperty<TpiCommunication> SelectedProgrammingDevice { get; private set; }

        public ReactiveProperty<Programmer> CurrentProgrammer { get; private set; }

        public ReactiveProperty<DeviceInformation> TargetDevice { get; private set; }

        public ReactiveCommand OpenSelectedProgrammerCommand { get; private set; }
        public ReactiveCommand ToggleConnectionCommand { get; private set; }
        
        public ReactiveCommand<OpeningFileSelectionMessage> OpenImageCommand { get; private set; }
        public ReactiveProperty<string> FileToProgram { get; private set; }
        public ReactiveCommand ProgramToDeviceCommand { get; private set; }

        public ReactiveProperty<bool> IsConnected { get; private set; }
        public ReactiveProperty<bool> CanExecuteOperation { get; private set; }
        public ReactiveProperty<bool> CanCloseWindow { get; private set; }

        public ReactiveProperty<bool> DisconnectOnSuccess { get; private set; }

        public ReactiveProperty<float> ProgrammingProgress { get; private set; }
        public ReactiveProperty<ProgrammerOperation> ProgrammerOperation { get; private set; }

        public int VersionMajor => Assembly.GetEntryAssembly().GetName().Version.Major;
        public int VersionMinor => Assembly.GetEntryAssembly().GetName().Version.Minor;
        public int VersionRevision=> Assembly.GetEntryAssembly().GetName().Version.Revision;

        public ReactiveCommand<string> OpenUriCommand { get; private set; }

        private void RaiseError(Exception e)
        {
            this.Messenger.Raise(new MessageBoxMessage(ErrorOccuredMessageKey, MessageBoxButton.OK, e.Message));
        }

        public MainWindowViewModel()
        {
            TpiCommunication.UpdateDevices();
 
            var operationCounter = new CountNotifier();
            var programmingProgress = new ScheduledNotifier<ProgrammingProgressInfo>();

            this.CanCloseWindow = operationCounter.Select(x => x == CountChangedStatus.Empty).ToReactiveProperty();

            this.DisconnectOnSuccess = new ReactiveProperty<bool>(true);

            this.SelectedProgrammingDevice = new ReactiveProperty<TpiCommunication>();

            this.OpenSelectedProgrammerCommand =
                Observable.CombineLatest(operationCounter, this.SelectedProgrammingDevice,
                    (counter, device) => counter == CountChangedStatus.Empty && device != null)
                    .ToReactiveCommand();
            this.OpenSelectedProgrammerCommand.Select(_ =>
            {
                var communication = this.SelectedProgrammingDevice.Value;
                communication.Open();
                this.CurrentProgrammer.Value?.Dispose();
                this.CurrentProgrammer.Value = new Programmer(communication, ((App)Application.Current).DeviceInformations.Value);
                return Unit.Default;
            })
            .OnErrorRetry((Exception e) => { this.RaiseError(e); })
            .Subscribe().AddTo(this.CompositeDisposable);

            this.CurrentProgrammer = new ReactiveProperty<Programmer>();
            this.CurrentProgrammer.AddTo(this.CompositeDisposable);

            this.TargetDevice = this.CurrentProgrammer
                .Where(programmer => programmer != null)
                .Select(programmer => programmer.ObserveProperty(self => self.ConnectedDevice))
                .Switch()
                .ToReactiveProperty();
            var programmerIsOpened = this.CurrentProgrammer.Select(programmer => programmer != null).Publish();
            this.CanExecuteOperation = Observable.CombineLatest(programmerIsOpened, operationCounter,
                (isOpened, counter) => isOpened && counter == CountChangedStatus.Empty)
                .ToReactiveProperty();
            this.ToggleConnectionCommand = this.CanExecuteOperation.ToReactiveCommand();
            this.ToggleConnectionCommand
                .SelectMany(_ =>
                {
                    operationCounter.Increment();
                    if (this.CurrentProgrammer.Value.ConnectedDevice == null)
                    {
                        return Observable.FromAsync(__ => this.CurrentProgrammer.Value.ConnectAsync())
                            .Select(__ => Unit.Default)
                            .Finally(() => operationCounter.Decrement());
                    }
                    else
                    {
                        return Observable.FromAsync(cancellationToken => this.CurrentProgrammer.Value.DisconnectAsync(cancellationToken))
                            .Finally(() => operationCounter.Decrement());
                    }
                })
                .OnErrorRetry((Exception e) => { this.RaiseError(e); })
                .Subscribe()
                .AddTo(this.CompositeDisposable);
            programmerIsOpened.Connect();

            this.ProgrammerOperation = this.CurrentProgrammer
                .Select(
                    programmer =>
                        programmer == null
                            ? Observable.Return(Model.ProgrammerOperation.None)
                            : programmer.ObserveProperty(self => self.Operation))
                .Switch().ToReactiveProperty();

            this.IsConnected = this.CurrentProgrammer
                .Select(programmer => programmer == null ? Observable.Return(false) : programmer.ObserveProperty(self => self.ConnectedDevice).Select(device => device != null))
                .Switch()
                .ToReactiveProperty();
            this.FileToProgram = new ReactiveProperty<string>();
            this.OpenImageCommand = this.ProgrammerOperation
                .Select(status => status == Model.ProgrammerOperation.None)
                .ToReactiveCommand<OpeningFileSelectionMessage>();

            this.OpenImageCommand
                .Subscribe(message =>
                {
                    this.FileToProgram.Value = message.Response?.FirstOrDefault() ?? "";
                })
                .AddTo(this.CompositeDisposable);

            this.ProgrammingProgress = programmingProgress.Select(progress => progress.CompletionRatio).ToReactiveProperty();

            this.ProgramToDeviceCommand = Observable
                .CombineLatest(this.CanExecuteOperation, this.IsConnected, this.FileToProgram, this.ProgrammerOperation,
                    (canExecuteOperation, isConnected, fileToProgram, operation) => canExecuteOperation && isConnected && !String.IsNullOrWhiteSpace(fileToProgram) && operation == Model.ProgrammerOperation.None)
                .ToReactiveCommand();
            this.ProgramToDeviceCommand
                .SelectMany(_ =>
                {
                    operationCounter.Increment();
                    var fileToProgram = this.FileToProgram.Value;
                    var programmer = this.CurrentProgrammer.Value;
                    var disconnectOnSuccess = this.DisconnectOnSuccess.Value;
                    return Observable.FromAsync(cancellationToken => programmer.ProgramAsync(fileToProgram, disconnectOnSuccess, programmingProgress, cancellationToken))
                        .Finally(() => operationCounter.Decrement());
                })
                .OnErrorRetry((Exception e) => { this.RaiseError(e); })
                .Subscribe()
                .AddTo(this.CompositeDisposable);

            this.OpenUriCommand = new ReactiveCommand<string>();
            this.OpenUriCommand
                .Select(uri => {
                    Process.Start(uri);
                    return Unit.Default;
                })
                .OnErrorRetry((Exception e) => this.RaiseError(e))
                .Subscribe().AddTo(this.CompositeDisposable);

            operationCounter.Increment();
            operationCounter.Decrement();
        }
    }
}
