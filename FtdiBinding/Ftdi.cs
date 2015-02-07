using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FtdiBinding.Native;

namespace FtdiBinding
{
    [Serializable]
    public class FtdiException : Exception
    {
        public int ErrorCode { get; private set; }
        //
        // For guidelines regarding the creation of new exception types, see
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
        // and
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
        //

        public FtdiException(int errorCode)
        {
            this.ErrorCode = errorCode;
        }

        protected FtdiException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    public class FtdiDevice
    {
        private LibUsb.libusb_device_descriptor descriptor;
        public string Manufacturer { get; private set; }
        public string Description { get; private set; }
        public string Serial { get; private set; }

        public int VendorId => (int) this.descriptor.idVendor;
        public int ProductId => (int)this.descriptor.idProduct;
        public int BcdDevice => (int) this.descriptor.bcdDevice;

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            var device = obj as FtdiDevice;
            return device != null &&
                   this.Manufacturer == device.Manufacturer &&
                   this.Description == device.Description &&
                   this.Serial == device.Serial &&
                   this.VendorId == device.VendorId &&
                   this.ProductId == device.ProductId &&
                   this.BcdDevice == device.BcdDevice;
        }

        public override int GetHashCode()
        {
            return
                this.Manufacturer.GetHashCode() ^
                this.Description.GetHashCode() ^
                this.Serial.GetHashCode() ^
                this.VendorId.GetHashCode() ^
                this.ProductId.GetHashCode() ^
                this.BcdDevice.GetHashCode();
        }

        public static bool operator==(FtdiDevice lhs, FtdiDevice rhs)
        {
            if (Object.ReferenceEquals(lhs, rhs)) return true;
            if (lhs == null) return false;
            if (rhs == null) return false;
            return lhs.Equals(rhs);
        }
        public static bool operator!=(FtdiDevice lhs, FtdiDevice rhs)
        {
            return !(lhs != rhs);
        }

        internal FtdiDevice(string manufacturer, string description, string serial,
            LibUsb.libusb_device_descriptor descriptor)
        {
            this.descriptor = descriptor;
            this.Manufacturer = manufacturer;
            this.Description = description;
            this.Serial = serial;
        }
    }

    public class Ftdi : IDisposable
    {
        private LibFtdi.FtdiContext context;
        private bool isInitialized = false;
        private bool isOpened = false;

        
        private static int CheckResult(int result)
        {
            if (result < 0)
            {
                throw new FtdiException(result);
            }
            return result;
        }

        private LibUsb.libusb_hotplug_callback_fn hotplugCallback;
        private IntPtr hotPlugNotificationHandle;
        private IntPtr usbDeviceContext;

        public event EventHandler DeviceDetached;

        private void RegisterHotPlugNotification()
        {
            this.hotplugCallback = (ctx, device, evt, data) =>
            {
                if (device != this.usbDeviceContext)
                {
                    return 0;
                }
                this.DeviceDetached?.Invoke(this, new EventArgs());
                return 1;
            };
            var usbContext = this.context.UsbContext;
            this.usbDeviceContext = LibUsb.libusb_get_device(this.context.UsbDeviceHandle);
            var result = LibUsb.libusb_hotplug_register_callback(
                usbContext,
                LibUsb.libusb_hotplug_event.LIBUSB_HOTPLUG_EVENT_DEVICE_LEFT,
                LibUsb.libusb_hotplug_flag.LIBUSB_HOTPLUG_ENUMERATE,
                LibUsb.LIBUSB_HOTPLUG_MATCH_ANY, LibUsb.LIBUSB_HOTPLUG_MATCH_ANY, LibUsb.LIBUSB_HOTPLUG_MATCH_ANY,
                this.hotplugCallback, IntPtr.Zero, out this.hotPlugNotificationHandle);
        }

        private void UnregisterHotPlugNotification()
        {
            if (this.hotPlugNotificationHandle != IntPtr.Zero)
            {
                LibUsb.libusb_hotplug_deregister_callback(this.context.UsbContext, this.hotPlugNotificationHandle);
                this.hotPlugNotificationHandle = IntPtr.Zero;
                this.hotplugCallback = null;
                this.usbDeviceContext = IntPtr.Zero;
            }
        }

        public IReadOnlyList<FtdiDevice> GetDevices()
        {
            const int stringBufferSize = 1024;
            var devices = new List<FtdiDevice>();

            IntPtr deviceListHandle;
            CheckResult(LibFtdi.ftdi_usb_find_all(this.context, out deviceListHandle, 0, 0));
            var deviceListPtr = deviceListHandle;
            try
            {
                while (deviceListPtr != IntPtr.Zero)
                {
                    var deviceList = (LibFtdi.FtdiDeviceList) Marshal.PtrToStructure(deviceListPtr, typeof (LibFtdi.FtdiDeviceList));

                    var descriptor = new LibUsb.libusb_device_descriptor();
                    LibUsb.libusb_get_device_descriptor(deviceList.Device, out descriptor);
                    var manufacturerString = new StringBuilder(stringBufferSize);
                    var productString = new StringBuilder(stringBufferSize);
                    var serialString = new StringBuilder(stringBufferSize);
                    LibFtdi.ftdi_usb_get_strings(this.context, deviceList.Device, 
                        manufacturerString, stringBufferSize, 
                        productString, stringBufferSize, 
                        serialString, stringBufferSize);

                    devices.Add(new FtdiDevice(manufacturerString.ToString(), productString.ToString(), serialString.ToString(), descriptor));
                    deviceListPtr = deviceList.Next;

                    Marshal.DestroyStructure(deviceListPtr, typeof (LibFtdi.FtdiDeviceList));
                }
                return devices;
            }
            finally
            {
                LibFtdi.ftdi_list_free2(deviceListHandle);
            }
        }
        public void Open(FtdiDevice device)
        {
            this.Open(device.VendorId, device.ProductId, device.Description, device.Serial, 0);
        }
        public void Open(int vendor, int product, string description, string serial, int index)
        {
            if( this.isOpened ) throw new InvalidOperationException("Device is already opened.");
            CheckResult(LibFtdi.ftdi_usb_open_desc_index(this.context, vendor, product, description, serial, (uint)index));
            this.RegisterHotPlugNotification();
            this.isOpened = true;
        }
        public Ftdi()
        {
            this.context = LibFtdi.ftdi_new();
            CheckResult(LibFtdi.ftdi_init(this.context));
            this.isInitialized = true;
        }
        
        public void PurgeReceiveBuffer()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_rx_buffer(this.context));
        }

        public void PurgeTransmitBuffer()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_tx_buffer(this.context));
        }

        public void PurgeBothBuffers()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_buffers(this.context));
        }

        public int Write(byte[] data, int size)
        {
            return CheckResult(LibFtdi.ftdi_write_data(this.context, data, size));
        }
        public int Write(byte[] data, int offset, int size)
        {
            var handle = GCHandle.Alloc(data);
            try
            {
                var pointer = handle.AddrOfPinnedObject();
                var pointerWithOffset = new IntPtr(pointer.ToInt64() + offset);
                return CheckResult(LibFtdi.ftdi_write_data(this.context, pointerWithOffset, size));
            }
            finally
            {
                handle.Free();
            }
        }

        public async Task<int> WriteAsync(byte[] data, int size)
        {
            var transfer = LibFtdi.ftdi_write_data_submit(this.context, data, size);
            if (transfer == IntPtr.Zero)
            {
                throw new FtdiException(0);
            }
            var result = await this.WaitTransfer(transfer);
            return CheckResult(result);
        }

        public int Read(byte[] data, int size)
        {
            return CheckResult(LibFtdi.ftdi_read_data(this.context, data, size));
        }
        public int Read(byte[] data, int offset, int size)
        {
            var handle = GCHandle.Alloc(data);
            try
            {
                var pointer = handle.AddrOfPinnedObject();
                var pointerWithOffset = new IntPtr(pointer.ToInt64() + offset);
                return CheckResult(LibFtdi.ftdi_read_data(this.context, pointerWithOffset, size));
            }
            finally
            {
                handle.Free();
            }
        }
        public async Task<int> ReadAsync(byte[] data, int size)
        {
            var transfer = LibFtdi.ftdi_read_data_submit(this.context, data, size);
            if (transfer == IntPtr.Zero)
            {
                throw new FtdiException(0);
            }
            var result = await this.WaitTransfer(transfer);
            return CheckResult(result);
        }
        private Task<int> WaitTransfer(IntPtr transfer)
        {
            return Task.Run(() => LibFtdi.ftdi_transfer_data_done(transfer));
        }

        public uint ReadChunkSize
        {
            get
            {
                uint value;
                CheckResult(LibFtdi.ftdi_read_data_get_chunksize(this.context, out value));
                return value;
            }
            set { LibFtdi.ftdi_read_data_set_chunksize(this.context, value); }
        }
        public uint WriteChunkSize
        {
            get
            {
                uint value;
                CheckResult(LibFtdi.ftdi_write_data_get_chunksize(this.context, out value));
                return value;
            }
            set { LibFtdi.ftdi_write_data_set_chunksize(this.context, value); }
        }

        public void SetBitMode(byte bitMask, FtdiMpsseMode mode)
        {
            CheckResult(LibFtdi.ftdi_set_bitmode(this.context, bitMask, mode));
        }

        public void DisableBitBang()
        {
            CheckResult(LibFtdi.ftdi_disable_bitbang(this.context));
        }

        public byte ReadPins()
        {
            byte pins;
            CheckResult(LibFtdi.ftdi_read_pins(this.context, out pins));
            return pins;
        }

        public byte LatencyTimer
        {
            get
            {
                byte value;
                CheckResult(LibFtdi.ftdi_get_latency_timer(this.context, out value));
                return value;
            }
            set { LibFtdi.ftdi_set_latency_timer(this.context, value); }
        }

        private void Dispose(bool disposing)
        {
            if (this.context != null)
            {
                this.UnregisterHotPlugNotification();
                //
                if (this.isOpened)
                {
                    LibFtdi.ftdi_usb_close(this.context);
                    this.isOpened = false;
                }
                if (this.isInitialized)
                {
                    LibFtdi.ftdi_deinit(this.context);
                    this.isInitialized = false;
                }
                this.context.Dispose();
                this.context = null;
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Ftdi()
        {
            this.Dispose(false);
        }
    }
    public enum FtdiChipType
    {
        TypeAM,
        TypeBM,
        Type2232C,
        TypeR,
        Type2232H,
        Type4232H,
        Type232H,
        Type230X,
    }

    public enum FtdiModuleDetachMode
    {
        AutoDetachSioModule,
        DontDetachSioModule,
    }

    public enum FtdiMpsseMode
    {
        Reset = 0x00,
        BitBang = 0x01,
        Mpsse = 0x02,
        SyncBB = 0x04,
        Mcu = 0x08,
        Opto = 0x10,
        CBus = 0x20,
        SyncFF = 0x40,
        Ft1284 = 0x80,
    }

    public enum FtdiInterface
    {
        Any,
        A,
        B,
        C,
        D,
    }

    public enum FtdiBits
    {
        Bits7,
        Bits8,
    }

    public enum FtdiStopBits
    {
        StopBit1,
        StopBit15,
        StopBit2,
    }

    public enum FtdiParity
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }

    public enum FtdiBreak
    {
        BreakOff,
        BreakOn,
    }

    public enum FtdiEepromValue
    {
        VendorId = 0,
        ProductId = 1,
        SelfPowered = 2,
        RemoteWakeup = 3,
        IsNotPnp = 4,
        SuspendDbus7 = 5,
        InIsIsochronous = 6,
        OutIsIsochronous = 7,
        SuspendPullDowns = 8,
        UseSerial = 9,
        UsbVersion = 10,
        UseUsbVersion = 11,
        MaxPower = 12,
        ChannelAType = 13,
        ChannelBType = 14,
        ChannelADriver = 15,
        ChannelBDriver = 16,
        CBusFunction0 = 17,
        CBusFunction1 = 18,
        CBusFunction2 = 19,
        CBusFunction3 = 20,
        CBusFunction4 = 21,
        CBusFunction5 = 22,
        CBusFunction6 = 23,
        CBusFunction7 = 24,
        CBusFunction8 = 25,
        CBusFunction9 = 26,
        HighCurrent = 27,
        HighCurrentA = 28,
        HighCurrentB = 29,
        Invert = 30,
        Group0Drive = 31,
        Group0Schmitt = 32,
        Group0Slew = 33,
        Group1Drive = 34,
        Group1Schmitt = 35,
        Group1Slew = 36,
        Group2Drive = 37,
        Group2Schmitt = 38,
        Group2Slew = 39,
        Group3Drive = 40,
        Group3Schmitt = 41,
        Group3Slew = 42,
        ChipSize = 43,
        ChipType = 44,
        PowerSave = 45,
        ClockPolarity = 46,
        DataOrder = 47,
        FlowControl = 48,
        ChannelCDriver = 49,
        ChannelDDriver = 50,
        ChannelARs485 = 51,
        ChannelBRs485 = 52,
        ChannelCRs485 = 53,
        ChannelDRs485 = 54,
        ReleaseNumber = 55,
    }


}
