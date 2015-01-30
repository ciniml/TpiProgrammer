using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TpiProgrammer.Model.Devices
{
    public partial class DeviceInformations
    {
        public static DeviceInformations Load(Stream stream)
        {
            var serializer = new XmlSerializer(typeof (DeviceInformations));
            return (DeviceInformations)serializer.Deserialize(stream);
        }

        public static DeviceInformations Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Load(stream);
            }
        }

        private Lazy<Dictionary<string, ConfigurationSection>> configurationSectionDictionary;
        private Lazy<Dictionary<Model.DeviceSignature, DeviceInformation>> deviceInformationDictionary;

        public DeviceInformations()
        {
            this.configurationSectionDictionary = new Lazy<Dictionary<string, ConfigurationSection>>(
                () => this.ConfigurationSection?.ToDictionary(x => x.Name) ?? new Dictionary<string, ConfigurationSection>(), LazyThreadSafetyMode.PublicationOnly);
            this.deviceInformationDictionary = new Lazy<Dictionary<DeviceSignature, DeviceInformation>>(
                () => this.DeviceInformation?.ToDictionary(x => new DeviceSignature(x.Signature)) ?? new Dictionary<DeviceSignature, DeviceInformation>(), LazyThreadSafetyMode.PublicationOnly);
        }

        public bool TryGetDeviceInformation(DeviceSignature key, out DeviceInformation value)
        {
            return this.deviceInformationDictionary.Value.TryGetValue(key, out value);
        }

        public DeviceInformation GetDeviceInformation(DeviceSignature key)
        {
            DeviceInformation value;
            if (!this.TryGetDeviceInformation(key, out value))
            {
                throw new KeyNotFoundException();
            }
            return value;
        }
        public bool TryGetConfigurationSection(string key, out ConfigurationSection value)
        {
            return this.configurationSectionDictionary.Value.TryGetValue(key, out value);
        }

        public ConfigurationSection GetConfigurationSection(string key)
        {
            ConfigurationSection value;
            if (!this.TryGetConfigurationSection(key, out value))
            {
                throw new KeyNotFoundException();
            }
            return value;
        }
    }

    public partial class FlashSection
    {
        public UInt32 AddressAsNumber => UInt32.Parse(this.Address.Substring(2), NumberStyles.HexNumber);
        public UInt32 SizeAsNumber => UInt32.Parse(this.Size);

    }

    public partial class ConfigurationSectionRef
    {
    }

}
