using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Codeplex.Reactive;
using TpiProgrammer.Model.Devices;

namespace TpiProgrammer
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private const string DeviceInformationsFileName = "DeviceInformations.xml";

        public ReactiveProperty<Model.Devices.DeviceInformations> DeviceInformations { get; private set; }

        public void ReloadDeviceInformations()
        {
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var path = Path.Combine(assemblyDirectory, DeviceInformationsFileName);
            this.DeviceInformations.Value = Model.Devices.DeviceInformations.Load(path);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DeviceInformations = new ReactiveProperty<DeviceInformations>();
            this.ReloadDeviceInformations();

            base.OnStartup(e);
        }

    }
}
