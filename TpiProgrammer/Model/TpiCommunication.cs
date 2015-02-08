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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media.TextFormatting;
using Codeplex.Reactive;
using TpiProgrammer.Annotations;
using FtdiBinding;

namespace TpiProgrammer.Model
{

    /// <summary>
    /// Device Signature of AVR
    /// </summary>
    public struct DeviceSignature
    {
        /// <summary>
        /// Manufacturer ID
        /// </summary>
        public byte ManufacturerId { get; set; }
        /// <summary>
        /// first byte of device ID 
        /// </summary>
        public byte DeviceId1 { get; set; }
        /// <summary>
        /// second byte of device ID
        /// </summary>
        public byte DeviceId2 { get; set; }

        /// <summary>
        /// Construct DeviceSignature from signature bytes.
        /// </summary>
        /// <param name="signatureBytes"></param>
        public DeviceSignature(byte[] signatureBytes) : this()
        {
            ManufacturerId = signatureBytes[0];
            DeviceId1 = signatureBytes[1];
            DeviceId2 = signatureBytes[2];
        }
    }

    /// <summary>
    /// TPI communication through FTDI devices with MPSSE.
    /// </summary>
    public class TpiCommunication : INotifyPropertyChanged, IDisposable
    {
        private static readonly ObservableCollection<TpiCommunication>  observableDevices = new ObservableCollection<TpiCommunication>();

        public static ReadOnlyObservableCollection<TpiCommunication> Devices { get; private set; }
        
        private static Dictionary<FtdiDevice, TpiCommunication> tpiDevices = new Dictionary<FtdiDevice, TpiCommunication>();
        private static readonly Ftdi enumerationContext;

        private bool isConnected;

        
        public static void UpdateDevices()
        {
            lock (tpiDevices)
            {
                var currentDevices = enumerationContext.GetDevices();
                var previousDevices = tpiDevices.Keys;
                var unchangedDevices = previousDevices.Intersect(currentDevices).ToArray();
                var addedDevices = currentDevices.Except(unchangedDevices).ToList();
                var removedDevices = previousDevices.Except(unchangedDevices).ToList();

                foreach (var removedDevice in removedDevices)
                {
                    var device = tpiDevices[removedDevice];
                    tpiDevices.Remove(removedDevice);
                    observableDevices.Remove(device);
                }
                foreach (var addedDevice in addedDevices)
                {
                    var device = new TpiCommunication(addedDevice);
                    observableDevices.Add(device);
                    tpiDevices.Add(addedDevice, device);
                }
            }
        }

        static TpiCommunication() 
        {
            Devices = new ReadOnlyObservableCollection<TpiCommunication>(observableDevices);
            enumerationContext = new Ftdi();
            UpdateDevices();
        }

        private readonly FtdiDevice device;
        private Ftdi ftdi;
        private DeviceSignature deviceSignature;

        public string Description => this.device.Description;

        public bool IsOpened => this.ftdi != null;

        private TpiCommunication(FtdiDevice device)
        {
            this.device = device;
            this.IsConnected = true;
        }

        public bool IsConnected
        {
            get { return this.isConnected; }
            private set
            {
                if (value.Equals(this.isConnected)) return;
                this.isConnected = value;
                this.OnPropertyChanged();
            }
        }

        public DeviceSignature DeviceSignature
        {
            get { return this.deviceSignature; }
            private set
            {
                if (value.Equals(this.deviceSignature)) return;
                this.deviceSignature = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// MPSSE Command builder
        /// </summary>
        private class MpsseCommand
        {
            /// <summary>
            /// A buffer to store command bytes.
            /// </summary>
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

            /// <summary>
            /// Expected length of responses for commands this instance represents.
            /// </summary>
            public int ExpectedResponseLength { get; private set; }

            /// <summary>
            /// Get a data shifting MPSSE command number.
            /// </summary>
            /// <param name="writeTms">Writes data to TMS signal or not</param>
            /// <param name="readTdo">Reads data from TDO signal or not</param>
            /// <param name="writeTdi">Writes data to TDI signal or not</param>
            /// <param name="lsbFirst">Read/Write data from LSB or MSB</param>
            /// <param name="readOnNegativeEdge">Read data on negative edge or positive edge.</param>
            /// <param name="bitMode">bitwise transfer mode or bytewise transfer mode</param>
            /// <param name="writeOnNegativeEdge">Write data on negative edge or positive edge.</param>
            /// <returns></returns>
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

            /// <summary>
            /// Check buffer related arguments and throw an exception if at least one of arguments are illegal.
            /// </summary>
            /// <param name="buffer"></param>
            /// <param name="index"></param>
            /// <param name="length"></param>
            private static void CheckBufferArguments(byte[] buffer, int index, int length)
            {
                if (index < 0 || buffer.Length <= index) throw new ArgumentOutOfRangeException("index");
                if(length <= 0 || (buffer.Length - index) < length) throw new ArgumentOutOfRangeException("length");
            }

            /// <summary>
            /// Write or exchange data bytes from/to the TDI/TDO signals.
            /// </summary>
            /// <param name="writeOnly">Just writes data to TDI signal and does not read from TDO signal.</param>
            /// <param name="lsbFirst"></param>
            /// <param name="readOnNegativeEdge"></param>
            /// <param name="writeOnNegativeEdge"></param>
            /// <param name="data"></param>
            /// <param name="index"></param>
            /// <param name="length"></param>
            /// <returns></returns>
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

#if false
private class TpiCommandSequence
        {
            private const int MaximumGuardTime = 127;

            private struct CommandData
            {
                public bool IsRead;
                public byte ValueToWrite;
            }

            private readonly List<CommandData> sequence = new List<CommandData>();
            private readonly int guardTime;

            public TpiCommandSequence(int guardTime)
            {
                if( guardTime < 0 || TpiCommandSequence.MaximumGuardTime < guardTime ) throw new ArgumentOutOfRangeException("guardTime");
                this.guardTime = guardTime;
            }

            public TpiCommandSequence() : this(TpiCommandSequence.MaximumGuardTime)
            {
            }
        }
#endif

        private void Read(byte[] buffer, int offset, int length)
        {
            while (length > 0)
            {
                var bytesRead = this.ftdi.Read(buffer, offset, length);
                if (bytesRead == 0)
                {
                    // No data are currently available. Wait.
                    Thread.Sleep(0);
                }
                offset += bytesRead;
                length -= bytesRead;
            }
        }

        private void Write(byte[] buffer, int offset, int length)
        {
            while (length > 0)
            {
                var bytesWritten = this.ftdi.Write(buffer, offset, length);
                if (bytesWritten == 0)
                {
                    // No data are currently available. Wait.
                    Thread.Sleep(0);
                }
                offset += bytesWritten;
                length -= bytesWritten;
            }
        }

        private Task<byte[]> ExecuteCommandAsync(MpsseCommand command)
        {
            return Task.Run(() =>
            {
                var commandBytes = command.ToBytes();
                this.Write(commandBytes, 0, commandBytes.Length);
                var response = new byte[command.ExpectedResponseLength];
                if (response.Length > 0)
                {
                    this.Read(response, 0, response.Length);
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

        private async Task WriteBreakAsync()
        {
            var command = new MpsseCommand();
            var data = new byte[] { 0x00, 0x80 };
            command.WriteDataBytes(true, true, data, 0, data.Length);
            await this.ExecuteCommandAsync(command);
        }
        private async Task WriteFrameAsync(byte data)
        {
            var command = new MpsseCommand();
            var parity = CalculateEvenParity(data, 0);
            
            var firstByte = (byte) ((data << 2) | 1);       // IDLE + START + DATA[0...5]
            var secondByte = (byte) ((data >> 6) | ((parity & 1) << 2) | 0x18);   // DATA6 + DATA7 + PARITY + SP1 + SP2
            command
                .WriteDataBits(true, true, firstByte, 8)
                .WriteDataBits(true, true, secondByte, 5);

            await this.ExecuteCommandAsync(command);
        }

        private async Task<byte> ReadFrameAsync()
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

            var response = await this.ExecuteCommandAsync(command);
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
            this.ftdi = new Ftdi();
            this.ftdi.Open(this.device);
            // Configure device mode
            this.ftdi.SetBitMode(0x1b, FtdiMpsseMode.Mpsse);
            this.ftdi.PurgeBothBuffers();
        }

        public async Task<byte> LoadDataIndirectAsync(bool postIncrement)
        {
            await this.WriteFrameAsync((byte) (postIncrement ? 0x24 : 0x20));
            return await this.ReadFrameAsync();
        }
        public async Task StoreDataIndirectAsync(byte value, bool postIncrement)
        {
            await this.WriteFrameAsync((byte)(postIncrement ? 0x64 : 0x60));
            await this.WriteFrameAsync(value);
        }

        public async Task StorePointerRegisterAsync(ushort pointer)
        {
            await this.WriteFrameAsync(0x68);
            await this.WriteFrameAsync((byte)(pointer & 0xff));
            await this.WriteFrameAsync(0x69);
            await this.WriteFrameAsync((byte)(pointer >> 8));
        }

        private async Task<byte> InFromIoSpaceAsync(int address)
        {
            if (address < 0 || 0x3f < address) throw new ArgumentOutOfRangeException("address");
            var command = 0x10 | ((address << 1) & 0x60) | (address & 0x0f);
            await this.WriteFrameAsync((byte)command);
            return await this.ReadFrameAsync();
        }

        private async Task OutToIoSpaceAsync(int address, byte value)
        {
            if (address < 0 || 0x3f < address) throw new ArgumentOutOfRangeException("address");
            var command = 0x90 | ((address << 1) & 0x60) | (address & 0x0f);
            await this.WriteFrameAsync((byte)command);
            await this.WriteFrameAsync(value);
        }

        private async Task<byte> LoadControlAndStatusRegisterAsync(int address)
        {
            if (address < 0 || 0x0f < address) throw new ArgumentOutOfRangeException("address");
            await this.WriteFrameAsync((byte)(0x80 | address));
            return await this.ReadFrameAsync();
        }
        private async Task StoreControlRegisterAsync(int address, byte value)
        {
            if (address < 0 || 0x0f < address) throw new ArgumentOutOfRangeException("address");
            await this.WriteFrameAsync((byte)(0xc0 | address));
            await this.WriteFrameAsync(value);
        }

        private async Task KeySignalingAsync()
        {
            var key = new byte[] { 0xff, 0x88, 0xd8, 0xcd, 0x45, 0xab, 0x89, 0x12 };
            await this.WriteFrameAsync(0xe0);    // SKEY
            foreach(var value in key)
            {
                await this.WriteFrameAsync(value);
            }
        }

        private const int NvmCsr = 0x32;
        private const int NvmCmd = 0x33;

        private enum NvmCommand : byte
        {
            NoOperation = 0x00,
            ChipErase = 0x10,
            SectionErase = 0x14,
            WordWrite = 0x1d,
        }

        private Task StoreNvmCommandAsync(NvmCommand command)
        {
            return this.OutToIoSpaceAsync(TpiCommunication.NvmCmd, (byte)command);
        }

        private const int CodeSectionStartAddress = 0x4000;
        private const int CodeSectionSize = 0x400;

        private async Task WaitWhileNvmIsBusyAsync(CancellationToken? cancellationToken = null)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            while(true)
            {
                cancellationToken.Value.ThrowIfCancellationRequested();
                var nvmStatus = await this.InFromIoSpaceAsync(NvmCsr);
                if ((nvmStatus & 0x80) == 0)
                {
                    return;
                }
            }
        }
        public async Task ChipEraseAsync(CancellationToken? cancellationToken = null)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
            await this.StoreNvmCommandAsync(NvmCommand.ChipErase);
            await this.StorePointerRegisterAsync(TpiCommunication.CodeSectionStartAddress | 1);
            await this.StoreDataIndirectAsync(0, false);
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
        }
        public async Task SectionEraseAsync(CancellationToken? cancellationToken = null)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
            await this.StoreNvmCommandAsync(NvmCommand.SectionErase);
            await this.StorePointerRegisterAsync(TpiCommunication.CodeSectionStartAddress | 1);
            await this.StoreDataIndirectAsync(0, false);
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
        }
        public async Task WordWriteAsync(ushort address, byte lowByte, byte highByte,  CancellationToken? cancellationToken = null)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
            await this.StoreNvmCommandAsync(NvmCommand.WordWrite);
            await this.StorePointerRegisterAsync(address);
            await this.StoreDataIndirectAsync(lowByte, true);
            await this.StoreDataIndirectAsync(highByte, true);
            await this.WaitWhileNvmIsBusyAsync(cancellationToken);
        }

        public async Task ReadDataAsync(ushort address, byte[] buffer, int offset, ushort count, CancellationToken? cancellationToken = null)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            
            await this.StorePointerRegisterAsync(address);
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = await this.LoadDataIndirectAsync(true);
            }
        }

        public async Task<DeviceSignature> ConnectToDeviceAsync()
        {
            // Set clock rate to 1[MHz]
            {
                var command = new MpsseCommand();
                command.SetClockDivisor(4); // 12 / ((1 + 4) * 2) = 1.2[MHz]
                await this.ExecuteCommandAsync(command);
            }
            // First, assert #RESET pin to reset the device
            {
                // Deassert #RESET and wait 100[ms] 
                var command = new MpsseCommand();
                command.SetGpio(0x10, 0x1b, false);
                await this.ExecuteCommandAsync(command);
                await Task.Delay(100);
            }
            {
                // Assert #RESET and wait 100[ms]
                var command = new MpsseCommand();
                command.SetGpio(0x00, 0x1b, false);
                await this.ExecuteCommandAsync(command);
                await Task.Delay(100);
            }
            // Next, clock 16 cycles with keeping data to high. 
            {
                var command = new MpsseCommand();
                command.WriteDataBytes(true, true, new byte[] {0xff, 0xff}, 0, 2);
                await this.ExecuteCommandAsync(command);
            }
            // Send a BREAK character to ensure the interface is not in error state.
            await this.WriteBreakAsync();

            // Now, the device should accept commands from the programmer.
            // We must identify what the interface is by reading TPIIR control register (0x0f) with SLDCS instruction.
            {
                var interfaceId = await this.LoadControlAndStatusRegisterAsync(0x0f);
                if (interfaceId != 0x80) // target interface is not TPI
                {
                    throw new Exception("Invalid interface ID");
                }
            }
            // Set the guard time as short as possible.
            {
                await this.StoreControlRegisterAsync(0x02, 7);   // TPIPCR = 7;
            }
            // Send KEY to enable NVM programming.
            await this.KeySignalingAsync();

            // Then we must identify what the device is by reading device signature bytes which is located in 0x3FC0..0x3FC2
            {
                await this.OutToIoSpaceAsync(NvmCmd, 0x00);   
                await this.StorePointerRegisterAsync(0x3fc0);

                var signature = new DeviceSignature();
                signature.ManufacturerId = await this.LoadDataIndirectAsync(true);
                signature.DeviceId1 = await this.LoadDataIndirectAsync(true);
                signature.DeviceId2 = await this.LoadDataIndirectAsync(true);

                this.DeviceSignature = signature;
                this.IsConnected = true;
                return signature;
            }
        }

        private const int TpiSr = 0x00;
        private const byte NvmEn = 0x02;

        public async Task DisconnectAsync(CancellationToken? cancellationToken)
        {
            cancellationToken = cancellationToken ?? CancellationToken.None;
            try
            {
                // Reset NVMEN bit in TPISR
                {
                    await this.StoreControlRegisterAsync(TpiSr, 0x00);
                    var result = await this.LoadControlAndStatusRegisterAsync(TpiSr);
                    while ((result & NvmEn) != 0)
                    {
                        cancellationToken.Value.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }
                // Release all signals.
                {
                    var command = new MpsseCommand();
                    command.SetGpio(0x00, 0x00, false);
                    command.SetGpio(0x00, 0x00, true);
                    await this.ExecuteCommandAsync(command);
                }
            }
            finally
            {
                this.IsConnected = false;
            }
            //
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
            var handler = PropertyChanged;
            handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
