using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Codeplex.Reactive;
using Codeplex.Reactive.Extensions;
using Livet.Messaging.IO;
using TpiProgrammer.Model;

namespace TpiProgrammer.ViewModel
{
    public class MainWindowViewModel : Livet.ViewModel
    {
        public enum MainViewStatus
        {
            Idle,
            Programming,
        }

        public ReactiveProperty<MainViewStatus> Status { get; private set; }

        public ReadOnlyObservableCollection<TpiCommunication> Programmers => TpiCommunication.Devices;
        public ReactiveProperty<TpiCommunication> SelectedProgrammer { get; private set; }

        public ReactiveProperty<TpiCommunication> CurrentProgrammer { get; private set; }

        public ReactiveProperty<DeviceSignature> TargetDeviceSignature { get; private set; }

        public ReactiveCommand OpenSelectedProgrammerCommand { get; private set; }
        public ReactiveCommand ConnectToDeviceCommand { get; private set; }

        public ReactiveCommand<OpeningFileSelectionMessage> OpenImageCommand { get; private set; }
        public ReactiveProperty<string> FileToProgram { get; private set; }
        public ReactiveCommand ProgramToDeviceCommand { get; private set; }

        public ReactiveProperty<float> ProgrammingProgress { get; private set; }

        public MainWindowViewModel()
        {
            TpiCommunication.UpdateDevices();

            this.SelectedProgrammer = new ReactiveProperty<TpiCommunication>();

            this.Status = new ReactiveProperty<MainViewStatus>(MainViewStatus.Idle);

            this.OpenSelectedProgrammerCommand = this.SelectedProgrammer.Select(programmer => programmer != null).ToReactiveCommand();
            this.OpenSelectedProgrammerCommand.Subscribe(_ =>
            {
                var communication = this.SelectedProgrammer.Value;
                communication.Open();
                this.CurrentProgrammer.Value = communication;
            }).AddTo(this.CompositeDisposable);

            this.CurrentProgrammer = new ReactiveProperty<TpiCommunication>();
            this.TargetDeviceSignature = this.CurrentProgrammer
                .Where(programmer => programmer != null)
                .Select(programmer => programmer.ObserveProperty(self => self.DeviceSignature))
                .Switch()
                .ToReactiveProperty();
            var programmerIsOpened = this.CurrentProgrammer.Select(programmer => programmer != null && programmer.IsOpened).Publish();
            this.ConnectToDeviceCommand = programmerIsOpened.ToReactiveCommand();
            this.ConnectToDeviceCommand.Subscribe(async _ =>
            {
                await this.CurrentProgrammer.Value.ConnectToDeviceAsync();
            }).AddTo(this.CompositeDisposable);
            programmerIsOpened.Connect();

            var isConnectedObservable = this.CurrentProgrammer
                .Select(programmer => programmer == null ? Observable.Return(false) : programmer.ObserveProperty(self => self.IsConnected))
                .Switch();
            this.FileToProgram = new ReactiveProperty<string>();
            this.OpenImageCommand = this.Status.Select(status => status == MainViewStatus.Idle).ToReactiveCommand<OpeningFileSelectionMessage>();
            this.OpenImageCommand
                .Subscribe(message =>
                {
                    this.FileToProgram.Value = message.Response?.FirstOrDefault() ?? "";
                })
                .AddTo(this.CompositeDisposable);

            var programmingProgress = new ObservableProgress<ProgrammingProgressInfo>();
            this.ProgrammingProgress = programmingProgress.Select(progress => progress.CompletionRatio).ToReactiveProperty();

            this.ProgramToDeviceCommand = Observable
                .CombineLatest(isConnectedObservable, this.FileToProgram, this.Status,
                    (isConnected, fileToProgram, status) => isConnected && !String.IsNullOrWhiteSpace(fileToProgram) && status == MainViewStatus.Idle)
                .ToReactiveCommand();
            this.ProgramToDeviceCommand
                .Select(_ => this.FileToProgram.Value)
                .Do(_ => this.Status.Value = MainViewStatus.Programming)
                .Select(fileToProgram => Observable.FromAsync( async __ =>
                    {
                        try
                        {
                            var programmer = new Programmer(this.CurrentProgrammer.Value);
                            await programmer.ProgramImageAsync(fileToProgram, programmingProgress, null);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                        finally
                        {
                            this.Status.Value = MainViewStatus.Idle;
                        }
                    }))
                .Switch()
                .Subscribe()
                .AddTo(this.CompositeDisposable);
        }
    }
}
