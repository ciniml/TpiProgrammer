using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Codeplex.Reactive;
using Codeplex.Reactive.Extensions;
using TpiProgrammer.Model;

namespace TpiProgrammer.ViewModel
{
    public class MainWindowViewModel : Livet.ViewModel
    {
        public ObservableCollection<TpiCommunication> Programmers
        {
            get { return TpiCommunication.Devices; }
        }
        public ReactiveProperty<TpiCommunication> SelectedProgrammer { get; private set; }

        public ReactiveProperty<TpiCommunication> CurrentProgrammer { get; private set; }

        public ReactiveProperty<DeviceSignature> TargetDeviceSignature { get; private set; }

        public ReactiveCommand OpenSelectedProgrammerCommand { get; private set; }
        public ReactiveCommand ConnectToDeviceCommand { get; private set; }

        public ReactiveProperty<string> FileToProgram { get; private set; }
        public ReactiveCommand ProgramToDeviceCommand { get; private set; }

        public MainWindowViewModel()
        {
            TpiCommunication.UpdateDevices();

            this.SelectedProgrammer = new ReactiveProperty<TpiCommunication>();

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
                .Select(programmer => programmer.ObserveProperty(self => self.IsConnected))
                .Switch();
            this.FileToProgram = new ReactiveProperty<string>();

            this.ProgramToDeviceCommand = Observable
                .CombineLatest(isConnectedObservable, this.FileToProgram,
                    (isConnected, fileToProgram) => isConnected && !String.IsNullOrWhiteSpace(fileToProgram))
                .ToReactiveCommand();
            this.ProgramToDeviceCommand.Subscribe(_ =>
            {
                var programmer = new Programmer(this.CurrentProgrammer.Value);
                // TODO: ProgramAsync
            })
            .AddTo(this.CompositeDisposable);
        }
    }
}
