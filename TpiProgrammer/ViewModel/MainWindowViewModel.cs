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
        public ObservableCollection<Programmer> Programmers
        {
            get { return Programmer.Devices; }
        }
        public ReactiveProperty<Programmer> SelectedProgrammer { get; private set; }

        public ReactiveProperty<Programmer> CurrentProgrammer { get; private set; }

        public ReactiveCommand OpenSelectedProgrammerCommand { get; private set; }
        public ReactiveCommand ConnectToDeviceCommand { get; private set; }

        public MainWindowViewModel()
        {
            Programmer.UpdateDevices();

            this.SelectedProgrammer = new ReactiveProperty<Programmer>();

            this.OpenSelectedProgrammerCommand = this.SelectedProgrammer.Select(programmer => programmer != null).ToReactiveCommand();
            this.OpenSelectedProgrammerCommand.Subscribe(_ =>
            {
                var programmer = this.SelectedProgrammer.Value;
                programmer.Open();
                this.CurrentProgrammer.Value = programmer;
            }).AddTo(this.CompositeDisposable);

            this.CurrentProgrammer = new ReactiveProperty<Programmer>();
            var programmerIsOpened = this.CurrentProgrammer.Select(programmer => programmer != null && programmer.IsOpened).Publish();
            this.ConnectToDeviceCommand = programmerIsOpened.ToReactiveCommand();
            this.ConnectToDeviceCommand.Subscribe(async _ =>
            {
                await this.CurrentProgrammer.Value.ConnectToDeviceAsync();
            }).AddTo(this.CompositeDisposable);
            programmerIsOpened.Connect();
        }
    }
}
