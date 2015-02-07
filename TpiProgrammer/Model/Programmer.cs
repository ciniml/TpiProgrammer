using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ImageLoader;
using TpiProgrammer.Annotations;
using TpiProgrammer.Model.Devices;

namespace TpiProgrammer.Model
{
    public class ProgrammingProgressInfo
    {
        public float CompletionRatio { get; private set; }
        public ProgrammerOperation Operation { get; private set; }

        public ProgrammingProgressInfo(float completionRatio, ProgrammerOperation operation)
        {
            this.CompletionRatio = completionRatio;
            this.Operation = operation;
        }
    }

    public enum ProgrammerOperation
    {
        None,
        Connecting,
        Erasing,
        CheckingBlank,
        Programming,
        Verifying,
    }

    public class Programmer : INotifyPropertyChanged, IDisposable
    {
        private class NullProgrammingProgress : IProgress<ProgrammingProgressInfo>
        {
            public void Report(ProgrammingProgressInfo value)
            {
            }
        }

        private TpiCommunication comm;
        private DeviceInformations deviceInformations;
        private DeviceInformation connectedDevice;
        private ProgrammerOperation operation;

        public string Description => this.comm.Description;

        public DeviceInformation ConnectedDevice
        {
            get { return this.connectedDevice; }
            set
            {
                if (Equals(value, this.connectedDevice)) return;
                this.connectedDevice = value;
                this.OnPropertyChanged();
            }
        }

        public ProgrammerOperation Operation
        {
            get { return this.operation; }
            private set
            {
                if (value == this.operation) return;
                this.operation = value;
                this.OnPropertyChanged();
            }
        }

        private class OperationScope : IDisposable
        {
            private readonly ProgrammerOperation originalOperation;
            private readonly Programmer outer;
            public OperationScope(Programmer outer)
            {
                this.outer = outer;
                this.originalOperation = this.outer.Operation;
            }

            public void Dispose()
            {
                this.outer.Operation = this.originalOperation;
            }
        }

        private OperationScope EnterOperationScope(ProgrammerOperation operation)
        {
            var scope = new OperationScope(this);
            this.Operation = operation;
            return scope;
        }

        public Programmer(TpiCommunication comm, DeviceInformations deviceInformations)
        {
            this.comm = comm;
            this.deviceInformations = deviceInformations;
        }

        public async Task<DeviceInformation> ConnectAsync()
        {
            var signature = await this.comm.ConnectToDeviceAsync();
            var deviceInformation = default(DeviceInformation);
            if (this.deviceInformations.TryGetDeviceInformation(signature, out deviceInformation))
            {
                this.ConnectedDevice = deviceInformation;
                return deviceInformation;
            }
            else
            {
                // Unknown device.
                throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Check connection to the device and throw exception if no devices are connected currently.
        /// </summary>
        private void CheckConnection()
        {
            if (!this.comm.IsConnected || this.ConnectedDevice == null)
            {
                throw new InvalidOperationException("Must be connected to a device.");
            }
        }

        public async Task EraseChipAsync(IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            this.CheckConnection();

            using (this.EnterOperationScope(ProgrammerOperation.Erasing))
            {
                progress.Report(new ProgrammingProgressInfo(0, ProgrammerOperation.Erasing));
                await this.comm.ChipEraseAsync(cancellationToken);
                progress.Report(new ProgrammingProgressInfo(1, ProgrammerOperation.Erasing));
            }
        }

        public async Task<bool> VerifyImageAsync(SparseImage<byte> image, IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;

            // Check prerequistics
            this.CheckConnection();

            if (image.Count == 0)
            {
                return true;
            }

            using (this.EnterOperationScope(ProgrammerOperation.Verifying))
            {
                var sectionStartAddress = this.ConnectedDevice.FlashSection.AddressAsNumber;

                var previousAddress = image.First().Key;
                var count = 0;
                var numberOfBytes = (float) image.Count;
                foreach (var item in image)
                {
                    cancellationToken.Value.ThrowIfCancellationRequested();
                    if (count == 0 || previousAddress + 1 != item.Key)
                    {
                        await this.comm.StorePointerRegisterAsync((ushort) (item.Key + sectionStartAddress));
                    }

                    var actual = await this.comm.LoadDataIndirectAsync(true);
                    if (actual != item.Value)
                    {
                        return false;
                    }

                    progress.Report(new ProgrammingProgressInfo(count/numberOfBytes, ProgrammerOperation.Verifying));
                    count++;
                }
            }
            return true;
        }

        public async Task<bool> CheckBlank(IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;

            // Check prerequistics
            this.CheckConnection();

            var sectionStartAddress = this.ConnectedDevice.FlashSection.AddressAsNumber;
            var sectionSize = this.connectedDevice.FlashSection.SizeAsNumber;

            using (this.EnterOperationScope(ProgrammerOperation.CheckingBlank))
            {
                // Set start address to the pointer.
                await this.comm.StorePointerRegisterAsync((ushort) (sectionStartAddress));
                for (var index = 0; index < sectionSize; index++)
                {
                    cancellationToken.Value.ThrowIfCancellationRequested();

                    var value = await this.comm.LoadDataIndirectAsync(true);
                    // Blank bytes in a program memory should be 0xff.
                    if (value != 0xff)
                    {
                        // Not blank.
                        return false;
                    }
                    progress.Report(new ProgrammingProgressInfo(index/(float) sectionSize,
                        ProgrammerOperation.CheckingBlank));
                }
            }
            return true;
        }

        
        public async Task ProgramImageAsync(SparseImage<byte> image, IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;

            // Check prerequistics
            this.CheckConnection();

            using (this.EnterOperationScope(ProgrammerOperation.Programming))
            {
                var targetDevice = this.ConnectedDevice;
                var flashSectionStartAddress = targetDevice.FlashSection.AddressAsNumber;

                int count = 0;
                float numberOfBytes = image.Count;
                foreach (var item in image)
                {
                    if ((item.Key & 1) != 0)
                    {
                        continue;
                    }
                    if (item.Key > 0xffffu)
                    {
                        break;
                    }

                    var lowByte = item.Value;
                    var highByte = image[item.Key + 1];

                    await
                        this.comm.WordWriteAsync((ushort) (item.Key + flashSectionStartAddress), lowByte, highByte,
                            cancellationToken);

                    // Report progress.
                    progress.Report(new ProgrammingProgressInfo(count/numberOfBytes, ProgrammerOperation.Programming));
                    count += 2;
                }
            }
        }

        public async Task ProgramAsync(string path, bool disconnectOnSuccess, IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;
            
            SparseImage<byte> image;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                image = await SimpleImageLoader.LoadHexAsync(stream, cancellationToken.Value);
            }

            using (this.EnterOperationScope(ProgrammerOperation.Programming))
            {
                await this.EraseChipAsync(progress, cancellationToken);
                //await this.CheckBlank(progress, cancellationToken);
                await this.ProgramImageAsync(image, progress, cancellationToken);
                await this.VerifyImageAsync(image, progress, cancellationToken);
            }
            if (disconnectOnSuccess)
            {
                await this.DisconnectAsync(cancellationToken);
            }
            
        }

        public async Task DisconnectAsync(CancellationToken? cancellationToken)
        {
            try
            {
                await this.comm.DisconnectAsync(cancellationToken);
            }
            finally
            {
                this.ConnectedDevice = null;
            }
        }

        public void Dispose()
        {
            if (this.comm != null)
            {
                this.comm.Dispose();
                this.comm = null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}