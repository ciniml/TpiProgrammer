using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using Codeplex.Reactive;
using FTD2XX_NET;
using TpiProgrammer.Annotations;

namespace TpiProgrammer.Model
{
    [Serializable]
    public class FtdiException : Exception
    {
        public FTDI.FT_STATUS Status { get; private set; }

        public FtdiException(FTDI.FT_STATUS status)
        {
            this.Status = status;
        }

        protected FtdiException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
    public static class FtdiStatusExtensions
    {
        public static void Check(this FTDI.FT_STATUS status)
        {
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                throw new FtdiException(status);
            }
        }
    }

    public struct DeviceSignature
    {
        public byte ManufacturerId { get; set; }
        public byte DeviceId1 { get; set; }
        public byte DeviceId2 { get; set; }

        public DeviceSignature(byte[] signatureBytes) : this()
        {
            ManufacturerId = signatureBytes[0];
            DeviceId1 = signatureBytes[1];
            DeviceId2 = signatureBytes[2];
        }
    }
    public class Programmer : INotifyPropertyChanged, IDisposable
    {
        public static ObservableCollection<Programmer> Devices { get; private set; }

        private static Dictionary<uint, Programmer> devices = new Dictionary<uint, Programmer>();
        private bool _isConnected;

        public static void UpdateDevices()
        {
            lock (devices)
            {
                var ftdi = new FTDI();
                uint numberOfDevices = 0;
                ftdi.GetNumberOfDevices(ref numberOfDevices);
                var newDeviceList = new FTDI.FT_DEVICE_INFO_NODE[numberOfDevices];
                ftdi.GetDeviceList(newDeviceList);

                var currentDeviceKeys = devices.Keys;
                var newDeviceMap = newDeviceList.ToDictionary(x => x.LocId);
                var unchangedDeviceKeys = currentDeviceKeys.Intersect(newDeviceMap.Keys).ToArray();
                var addedDeviceKeys = newDeviceMap.Keys.Except(unchangedDeviceKeys);
                var removedDeviceKeys = currentDeviceKeys.Except(unchangedDeviceKeys);

                foreach (var removedDeviceKey in removedDeviceKeys)
                {
                    var device = devices[removedDeviceKey];
                    devices.Remove(removedDeviceKey);
                    Devices.Remove(device);
                }
                foreach (var addedDeviceKey in addedDeviceKeys)
                {
                    var deviceInfo = newDeviceMap[addedDeviceKey];
                    var device = new Programmer(deviceInfo);
                    Devices.Add(device);
                    devices.Add(device.LocationId, device);
                }
            }
        }

        static Programmer() 
        {
            Devices = new ObservableCollection<Programmer>();
            UpdateDevices();
        }

        private readonly FTDI.FT_DEVICE_INFO_NODE deviceInfoNode;
        private FTDI ftdi;
        private DeviceSignature _deviceSignature;

        private uint LocationId
        {
            get { return this.deviceInfoNode.LocId; }
        }

        public string Description
        {
            get { return this.deviceInfoNode.Description; }
        }

        public bool IsOpened
        {
            get { return this.ftdi != null; }
        }

        private Programmer(FTDI.FT_DEVICE_INFO_NODE deviceInfoNode)
        {
            this.deviceInfoNode = deviceInfoNode;
            this.IsConnected = true;
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            private set
            {
                if (value.Equals(_isConnected)) return;
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public DeviceSignature DeviceSignature
        {
            get { return _deviceSignature; }
            private set
            {
                if (value.Equals(_deviceSignature)) return;
                _deviceSignature = value;
                OnPropertyChanged();
            }
        }

        private class MpsseCommand
        {
            private readonly List<byte> buffer;

            public MpsseCommand()
            {
                this.buffer = new List<byte>();
            }

            public MpsseCommand(int reservedLength)
            {
                this.buffer = new List<byte>(reservedLength);
            }

            public byte[] ToBytes()
            {
                return this.buffer.ToArray();
            }

            public int ExpectedResponseLength { get; private set; }

            private static byte GetDataShiftingCommand(bool writeTms, bool readTdo, bool writeTdi, bool lsbFirst,
                bool readOnNegativeEdge, bool bitMode, bool writeOnNegativeEdge)
            {
                return (byte)(
                    (writeTms            ? 0x40 : 0x00) | 
                    (readTdo             ? 0x20 : 0x00) |
                    (writeTdi            ? 0x10 : 0x00) |
                    (lsbFirst            ? 0x08 : 0x00) |
                    (readOnNegativeEdge  ? 0x04 : 0x00) |
                    (bitMode             ? 0x02 : 0x00) |
                    (writeOnNegativeEdge ? 0x01 : 0x00));
            }

            private static void CheckBufferArguments(byte[] buffer, int index, int length)
            {
                if (index < 0 || buffer.Length <= index) throw new ArgumentOutOfRangeException("index");
                if(length <= 0 || (buffer.Length - index) < length) throw new ArgumentOutOfRangeException("length");
            }
            private MpsseCommand WriteDataBytes(bool writeOnly, bool lsbFirst, bool readOnNegativeEdge, bool writeOnNegativeEdge, byte[] data, int index, int length)
            {
                if( data == null) throw new ArgumentNullException("data");
                if (length <= 0 || 65536 < length) throw new ArgumentOutOfRangeException("length");
                CheckBufferArguments(data, index, length);

                this.buffer.Add(GetDataShiftingCommand(false, !writeOnly, true, lsbFirst, readOnNegativeEdge, false, writeOnNegativeEdge));
                this.buffer.Add((byte)((length - 1) & 0xff));
                this.buffer.Add((byte)((length - 1) >> 8));
                this.buffer.AddRange(data.Skip(index).Take(length));
                this.ExpectedResponseLength += (writeOnly ? 0 : length);
                return this;
            }

            private MpsseCommand WriteTmsBits(bool writeOnly, bool readOnNegativeEdge, bool writeOnNegativeEdge, bool tdoLevel, byte data, int length)
            {
                if (length <= 0 || 6 < length) throw new ArgumentOutOfRangeException("length");
                
                this.buffer.Add(GetDataShiftingCommand(true, !writeOnly, false, true, readOnNegativeEdge, false, writeOnNegativeEdge));
                this.buffer.Add((byte)(length - 1));
                this.buffer.Add((byte)((data & 0x7f) | (tdoLevel ? 0x80 : 0x00)));
                this.ExpectedResponseLength += (writeOnly ? 0 : 1);
                return this;
            }


            public MpsseCommand WriteDataBytes(bool lsbFirst, bool writeOnNegativeEdge,
                byte[] data, int index, int length)
            {
                return this.WriteDataBytes(true, lsbFirst, false, writeOnNegativeEdge, data, index, length);
            }

            public MpsseCommand ExchangeDataBytes(bool lsbFirst, bool readOnNegativeEdge, bool writeOnNegativeEdge,
                byte[] data, int index, int length)
            {
                return this.WriteDataBytes(false, lsbFirst, readOnNegativeEdge, writeOnNegativeEdge, data, index, length);
            }

            public MpsseCommand WriteTmsBits(bool writeOnNegativeEdge, bool tdoLevel, byte data, int length)
            {
                return this.WriteTmsBits(true, false, writeOnNegativeEdge, tdoLevel, data, length);
            }

            public MpsseCommand ExchangeTmsBits(bool readOnNegativeEdge, bool writeOnNegativeEdge, bool tdoLevel, byte data, int length)
            {
                return this.WriteTmsBits(false, readOnNegativeEdge, writeOnNegativeEdge, tdoLevel, data, length);
            }

            public MpsseCommand ReadDataBytes(bool lsbFirst, bool readOnNegativeEdge, int length)
            {
                if (length <= 0 || 65536 < length) throw new ArgumentOutOfRangeException("length");

                this.buffer.Add(GetDataShiftingCommand(false, true, false, lsbFirst, readOnNegativeEdge, false, false));
                this.buffer.Add((byte)((length - 1) & 0xff));
                this.buffer.Add((byte)((length - 1) >> 8));
                this.ExpectedResponseLength += length;
                return this;
            }

            private MpsseCommand WriteDataBits(bool writeOnly, bool lsbFirst, bool readOnNegativeEdge, bool writeOnNegativeEdge, byte data, int length)
            {
                if (length <= 0 || 8 < length) throw new ArgumentOutOfRangeException("length");

                this.buffer.Add(GetDataShiftingCommand(false, !writeOnly, true, lsbFirst, readOnNegativeEdge, true, writeOnNegativeEdge));
                this.buffer.Add((byte) (length - 1));
                this.buffer.Add(data);
                this.ExpectedResponseLength += writeOnly ? 0 : 1;
                return this;
            }

            public MpsseCommand WriteDataBits(bool lsbFirst, bool writeOnNegativeEdge, byte data, int length)
            {
                return this.WriteDataBits(true, lsbFirst, false, writeOnNegativeEdge, data, length);
            }

            public MpsseCommand ExchangeDataBits(bool lsbFirst, bool readOnNegativeEdge, bool writeOnNegativeEdge,
                byte data, int length)
            {
                return this.WriteDataBits(false, lsbFirst, readOnNegativeEdge, writeOnNegativeEdge, data, length);
            }

            public MpsseCommand ReadDataBits(bool lsbFirst, bool readOnNegativeEdge, int length)
            {
                if (length <= 0 || 8 < length) throw new ArgumentOutOfRangeException("length");

                this.buffer.Add(GetDataShiftingCommand(false, true, false, lsbFirst, readOnNegativeEdge, true, false));
                this.buffer.Add((byte)(length - 1));
                this.ExpectedResponseLength += 1;
                return this;
            }
            public MpsseCommand SetGpio(byte data, byte direction, bool isHighByte)
            {
                this.buffer.Add((byte)(isHighByte ? 0x82 : 0x80));
                this.buffer.Add(data);
                this.buffer.Add(direction);
                return this;
            }

            public MpsseCommand ReadGpio(bool isHighByte)
            {
                this.buffer.Add((byte)(isHighByte ? 0x83 : 0x81));
                return this;
            }

            public MpsseCommand SetClockDivisor(ushort divisor)
            {
                this.buffer.Add(0x86);
                this.buffer.Add((byte)(divisor & 0xff));
                this.buffer.Add((byte)(divisor >> 8));
                return this;
            }
        }

        private Task<byte[]> ExecuteCommand(MpsseCommand command)
        {
            return Task.Run(() =>
            {
                var commandBytes = command.ToBytes();
                var bytesWritten = 0u;
                this.ftdi.Write(commandBytes, commandBytes.Length, ref bytesWritten).Check();
                var response = new byte[command.ExpectedResponseLength];
                if (response.Length > 0)
                {
                    var bytesRead = 0u;
                    this.ftdi.Read(response, (uint) response.Length, ref bytesRead).Check();
                }
                return response;
            });
        }

        private static int CalculateEvenParity(int value, int parity)
        {
            parity = parity ^ value;
            parity = (parity ^ (parity >> 1));
            parity = (parity ^ (parity >> 2));
            parity = (parity ^ (parity >> 4));
            return parity & 1;
        }

        private async Task WriteBreak()
        {
            var command = new MpsseCommand();
            var data = new byte[] { 0x00, 0x80 };
            command.WriteDataBytes(true, true, data, 0, data.Length);
            await this.ExecuteCommand(command);
        }
        private async Task WriteFrame(byte data)
        {
            var command = new MpsseCommand();
            var parity = CalculateEvenParity(data, 0);
            
            var firstByte = (byte) ((data << 2) | 1);       // IDLE + START + DATA[0...5]
            var secondByte = (byte) ((data >> 6) | ((parity & 1) << 2) | 0x18);   // DATA6 + DATA7 + PARITY + SP1 + SP2
            command
                .WriteDataBits(true, true, firstByte, 8)
                .WriteDataBits(true, true, secondByte, 5);

            await this.ExecuteCommand(command);
        }

        private async Task<byte> ReadFrame()
        {
            // Maximum guard time (128 bit) + 1 Frame (12bit) = 140 bits must be shifted out
            // to ensure got a frame.
            var command = new MpsseCommand();
            var data = new byte[19];
            for(int i = 0; i < data.Length; i++)
            {
                data[i] = 0xff;
            }
            command.ExchangeDataBytes(true, true, true, data, 0, data.Length);

            var response = await this.ExecuteCommand(command);
            // Search head of a frame.
            int headIndex = response.Length;
            for(int i = 0; i < response.Length; i++)
            {
                if (response[i] != 0xff)
                {
                    headIndex = i;
                    break;
                }
            }
            if( headIndex >= response.Length - 1)
            {
                // No frames were found in valid range.
                throw new Exception("No frames were found.");
            }
            headIndex = headIndex < 1 ? 0 : headIndex - 1;

            uint frame = BitConverter.ToUInt32(response, headIndex);
            // Find a start bit.
            for (int i = 0; i < 32 - 12 && (frame & 1) != 0; i++, frame >>= 1) ;    // Search a start bit from LSB to (32 - 12) bit. 
            // Check frame integrity.
            if( (frame & 0x400u) == 0 || (frame & 0x800u) == 0)
            {
                // Bad stop bits.
                throw new Exception("Frame error");
            }
            var responseData = (int)((frame >> 1) & 0xffu);
            var parity = (int)((frame >> 9) & 1u);

            var responseParity = CalculateEvenParity(responseData, parity);
            if (responseParity != 0)
            {
                // Parity error
                throw new Exception("Parity error");
            }

            return (byte)responseData;
        }

        public void Open()
        {
            // Create FTDI device object and open the device.
            this.ftdi = new FTDI();
            this.ftdi.OpenByLocation(this.deviceInfoNode.LocId).Check();
            // Configure device mode
            this.ftdi.SetBitMode(0, FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE).Check();
            this.ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX).Check();
        }

        private async Task<byte> LoadDataIndirect(bool postIncrement)
        {
            await this.WriteFrame((byte) (postIncrement ? 0x24 : 0x20));
            return await this.ReadFrame();
        }

        private async Task StorePointerRegister(ushort pointer)
        {
            await this.WriteFrame(0x68);
            await this.WriteFrame((byte)(pointer & 0xff));
            await this.WriteFrame(0x69);
            await this.WriteFrame((byte)(pointer >> 8));
        }

        private async Task SerialOutToIoSpace(int address, byte value)
        {
            if (address < 0 || 0x3f < address) throw new ArgumentOutOfRangeException("address");
            var command = 0x90 | ((address << 1) & 0x60) | (address & 0x0f);
            await this.WriteFrame((byte)command);
            await this.WriteFrame(value);
        }

        private async Task SerialKeySignaling()
        {
            var key = new byte[] { 0xff, 0x88, 0xd8, 0xcd, 0x45, 0xab, 0x89, 0x12 };
            await this.WriteFrame(0xe0);    // SKEY
            foreach(var value in key)
            {
                await this.WriteFrame(value);
            }
        }
        private const int NvmCsr = 0x32;
        private const int NvmCmd = 0x33;

        public async Task<DeviceSignature> ConnectToDeviceAsync()
        {
            // Set clock rate to 1[MHz]
            {
                var command = new MpsseCommand();
                command.SetClockDivisor(4); // 12 / ((1 + 4) * 2) = 1.2[MHz]
                await this.ExecuteCommand(command);
            }
            // First, assert #RESET pin to reset the device
            {
                // Deassert #RESET and wait 100[ms] 
                var command = new MpsseCommand();
                command.SetGpio(0x10, 0x1b, false);
                await this.ExecuteCommand(command);
                await Task.Delay(100);
            }
            {
                // Assert #RESET and wait 100[ms]
                var command = new MpsseCommand();
                command.SetGpio(0x00, 0x1b, false);
                await this.ExecuteCommand(command);
                await Task.Delay(100);
            }
            // Next, clock 16 cycles with keeping data to high. 
            {
                var command = new MpsseCommand();
                command.WriteDataBytes(true, true, new byte[] {0xff, 0xff}, 0, 2);
                await this.ExecuteCommand(command);
            }
            // Send a BREAK character to ensure the interface is not in error state.
            await this.WriteBreak();

            // Now, the device should accept commands from the programmer.
            // We must identify what the interface is by reading TPIIR control register (0x0f) with SLDCS instruction.
            {
                await this.WriteFrame(0x8f);    // SLDCS TPIIR
                var interfaceId = await this.ReadFrame();
                if (interfaceId != 0x80) // target interface is not TPI
                {
                    throw new Exception("Invalid interface ID");
                }
            }
            // Set the guard time as short as possible.
            {
                await this.WriteFrame(0xc0 | 0x02); // SSTCS TPIPCR, 7
                await this.WriteFrame(7);           // /
            }
            // Send KEY to enable NVM programming.
            await this.SerialKeySignaling();

            // Then we must identify what the device is by reading device signature bytes which is located in 0x3FC0..0x3FC2
            {
                await this.SerialOutToIoSpace(NvmCmd, 0x00);   
                await this.StorePointerRegister(0x3fc0);

                var signature = new DeviceSignature();
                signature.ManufacturerId = await this.LoadDataIndirect(true);
                signature.DeviceId1 = await this.LoadDataIndirect(true);
                signature.DeviceId2 = await this.LoadDataIndirect(true);

                this.DeviceSignature = signature;
                this.IsConnected = true;
                return signature;
            }
        }

        public void Close()
        {
            this.ftdi.Close();
            this.ftdi = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (this.ftdi != null)
            {
                this.Close();
            }
        }
    }
}
