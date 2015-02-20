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
    /// <summary>
    /// An exception which is thrown when some erros are occured in libftdi.
    /// </summary>
    [Serializable]
    public class FtdiException : Exception
    {
        /// <summary>
        /// Gets libftdi error code
        /// </summary>
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

    /// <summary>
    /// Represents a USB device which can use with libftdi.
    /// </summary>
    public class FtdiDevice
    {
        private LibUsb.libusb_device_descriptor descriptor;
        /// <summary>
        /// Gets this device's manufacturer string.
        /// </summary>
        public string Manufacturer { get; private set; }
        /// <summary>
        /// Gets this device's description string.
        /// </summary>
        public string Description { get; private set; }
        /// <summary>
        /// Gets this device's serial string.
        /// </summary>
        public string Serial { get; private set; }

        /// <summary>
        /// Gets idVendor field of the device descriptor.
        /// </summary>
        public int VendorId => (int) this.descriptor.idVendor;
        /// <summary>
        /// Gets idProduct field of the device descriptor.
        /// </summary>
        public int ProductId => (int)this.descriptor.idProduct;
        /// <summary>
        /// Gets bcdDevice field of the device descriptor.
        /// </summary>
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
                (this.Manufacturer?.GetHashCode() ?? 0) ^
                (this.Description?.GetHashCode() ?? 0) ^
                (this.Serial?.GetHashCode() ?? 0) ^
                this.VendorId.GetHashCode() ^
                this.ProductId.GetHashCode() ^
                this.BcdDevice.GetHashCode();
        }

        public static bool operator==(FtdiDevice lhs, FtdiDevice rhs)
        {
            if (object.ReferenceEquals(lhs, rhs)) return true;
            if ((object)lhs == null) return false;
            if ((object)rhs == null) return false;
            return lhs.Equals(rhs);
        }
        public static bool operator!=(FtdiDevice lhs, FtdiDevice rhs)
        {
            return !(lhs == rhs);
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

    /// <summary>
    /// Manages FTDI devices.
    /// </summary>
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

        /// <summary>
        /// Register to receive hotplug notifications of the current device.
        /// </summary>
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
                LibUsb.libusb_hotplug_event.LIBUSB_HOTPLUG_EVENT_DEVICE_LEFT,   // Receive detach event only.
                LibUsb.libusb_hotplug_flag.LIBUSB_HOTPLUG_ENUMERATE,
                LibUsb.LIBUSB_HOTPLUG_MATCH_ANY, LibUsb.LIBUSB_HOTPLUG_MATCH_ANY, LibUsb.LIBUSB_HOTPLUG_MATCH_ANY,
                this.hotplugCallback, IntPtr.Zero, out this.hotPlugNotificationHandle);
        }

        /// <summary>
        /// Unregister to receive hotplug notification.
        /// </summary>
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

        /// <summary>
        /// Enumerates currently attached devices and returns a list of them.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<FtdiDevice> GetDevices()
        {
            const int stringBufferSize = 1024;
            var devices = new List<FtdiDevice>();

            IntPtr deviceListHandle;
            CheckResult(LibFtdi.ftdi_usb_find_all(this.context, out deviceListHandle, 0, 0));
            // Handle of the device list libftdi returns is a pointer to head node of device list.
            // We need to save this handle to release the device list after enumeration completes.
            var deviceListPtr = deviceListHandle;
            try
            {
                while (deviceListPtr != IntPtr.Zero)
                {
                    // Gets the current node.
                    var deviceList = (LibFtdi.FtdiDeviceList) Marshal.PtrToStructure(deviceListPtr, typeof (LibFtdi.FtdiDeviceList));

                    var descriptor = new LibUsb.libusb_device_descriptor();
                    LibUsb.libusb_get_device_descriptor(deviceList.Device, out descriptor);

                    // Reads the string descriptors if exist.
                    var manufacturerBuffer = new StringBuilder(stringBufferSize);
                    var productBuffer = new StringBuilder(stringBufferSize);
                    var serialBuffer = new StringBuilder(stringBufferSize);
                    var result = (LibFtdi.FtdiError)LibFtdi.ftdi_usb_get_strings(this.context, deviceList.Device, 
                        manufacturerBuffer, stringBufferSize, 
                        productBuffer, stringBufferSize, 
                        serialBuffer, stringBufferSize);
                    if (result != LibFtdi.FtdiError.UnableToOpenDevice &&
                        result != LibFtdi.FtdiError.LibUsbGetDeviceDescriptorFailed)
                    {
                        var manufacturer = result == LibFtdi.FtdiError.GetProductManufacturerFailed ? null : manufacturerBuffer.ToString();
                        var product = result == LibFtdi.FtdiError.GetProductDescriptionFailed ? null : productBuffer.ToString();
                        var serial = result == LibFtdi.FtdiError.GetSerialNumberFailed ? null : serialBuffer.ToString();
                        // Add a device to the list.
                        devices.Add(new FtdiDevice(manufacturer, product, serial, descriptor));
                    }
                    deviceListPtr = deviceList.Next;
                }
                return devices;
            }
            finally
            {
                LibFtdi.ftdi_list_free2(deviceListHandle);
            }
        }

        /// <summary>
        /// Open the specified device.
        /// </summary>
        /// <param name="device"></param>
        public void Open(FtdiDevice device)
        {
            this.Open(device.VendorId, device.ProductId, device.Description, device.Serial, 0);
        }

        /// <summary>
        /// Open a device identified by values of its device desciptor fields.
        /// </summary>
        /// <param name="vendor">idVendor field</param>
        /// <param name="product">idProduct field</param>
        /// <param name="description">Product description string descriptor</param>
        /// <param name="serial">Serial number string descriptor</param>
        /// <param name="index">
        /// An index to select a device when devices which has the same USB descriptor are attached.
        /// </param>
        public void Open(int vendor, int product, string description, string serial, int index)
        {
            if( this.isOpened ) throw new InvalidOperationException("Device is already opened.");
            CheckResult(LibFtdi.ftdi_set_interface(this.context, FtdiInterface.A));
            CheckResult(LibFtdi.ftdi_usb_open_desc_index(this.context, vendor, product, description, serial, (uint)index));
            this.RegisterHotPlugNotification();
            this.isOpened = true;
        }

        /// <summary>
        /// Close the device currently opened.
        /// </summary>
        public void Close()
        {
            if( !this.isOpened ) throw new InvalidOperationException("Device is not opened.");
            this.UnregisterHotPlugNotification();
            CheckResult(LibFtdi.ftdi_usb_close(this.context));
            this.isOpened = false;
        }

        public Ftdi()
        {
            this.context = LibFtdi.ftdi_new();
            if (this.context.IsInvalid)
            {
                throw new FtdiException(0);
            }
            this.isInitialized = true;
        }
        
        /// <summary>
        /// Purge contents of the receive buffer.
        /// </summary>
        public void PurgeReceiveBuffer()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_rx_buffer(this.context));
        }
        /// <summary>
        /// Purge contents of the transmit buffer.
        /// </summary>
        public void PurgeTransmitBuffer()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_tx_buffer(this.context));
        }
        /// <summary>
        /// Purge both the receive buffer and the transmit buffer.
        /// </summary>
        public void PurgeBothBuffers()
        {
            CheckResult(LibFtdi.ftdi_usb_purge_buffers(this.context));
        }

        /// <summary>
        /// Write data to the device.
        /// </summary>
        /// <param name="data">An byte array which contains data to write.</param>
        /// <param name="size">Number of bytes to write</param>
        /// <returns>Number of bytes actually written to the device.</returns>
        public int Write(byte[] data, int size)
        {
            return CheckResult(LibFtdi.ftdi_write_data(this.context, data, size));
        }
        /// <summary>
        /// Write data to the device.
        /// </summary>
        /// <param name="data">An byte array which contains data to write.</param>
        /// <param name="offset"></param>
        /// <param name="size">Number of bytes to write</param>
        /// <returns>Number of bytes actually written to the device.</returns>
        public int Write(byte[] data, int offset, int size)
        {
            // Pins the array and get its address.
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var pointer = handle.AddrOfPinnedObject();
                var pointerWithOffset = IntPtr.Add(pointer, offset);
                return CheckResult(LibFtdi.ftdi_write_data(this.context, pointerWithOffset, size));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Write data to the device asynchronously.
        /// </summary>
        /// <param name="data">An byte array which contains data to write.</param>
        /// <param name="size">Number of bytes to write</param>
        /// <returns>Number of bytes actually written to the device.</returns>
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
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var pointer = handle.AddrOfPinnedObject();
                var pointerWithOffset = IntPtr.Add(pointer, offset);
                return CheckResult(LibFtdi.ftdi_read_data(this.context, pointerWithOffset, size));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Read data from the device asynchronously.
        /// </summary>
        /// <param name="data">An byte array to store data read from the device</param>
        /// <param name="size">Number of bytes to read</param>
        /// <returns>Number of bytes actually read to the device.</returns>
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
        /// <summary>
        /// Waits until the asynchronous transfer completes.
        /// </summary>
        /// <param name="transfer">Handle of the asynchronous transfer to wait.</param>
        /// <returns></returns>
        private Task<int> WaitTransfer(IntPtr transfer)
        {
            return Task.Run(() => LibFtdi.ftdi_transfer_data_done(transfer));
        }

        /// <summary>
        /// Gets or sets the chunk size when reading data from the device.
        /// </summary>
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
        /// <summary>
        /// Gets or sets the chunk size when writing data to the device.
        /// </summary>
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

        /// <summary>
        /// Sets bit mode of the device.
        /// </summary>
        /// <param name="bitMask">I/O direction of the port.</param>
        /// <param name="mode">bit mode. See <see cref="FtdiMpsseMode"/></param>
        public void SetBitMode(byte bitMask, FtdiMpsseMode mode)
        {
            CheckResult(LibFtdi.ftdi_set_bitmode(this.context, bitMask, mode));
        }

        /// <summary>
        /// Disables bit bang mode.
        /// </summary>
        public void DisableBitBang()
        {
            CheckResult(LibFtdi.ftdi_disable_bitbang(this.context));
        }

        /// <summary>
        /// Reads current pin values.
        /// </summary>
        /// <returns></returns>
        public byte ReadPins()
        {
            byte pins;
            CheckResult(LibFtdi.ftdi_read_pins(this.context, out pins));
            return pins;
        }

        /// <summary>
        /// Gets or sets latency timer.
        /// </summary>
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

        /// <summary>
        /// Dispose this object.
        /// </summary>
        /// <param name="disposing"></param>
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

    /// <summary>
    /// FTDI chip type
    /// </summary>
    public enum FtdiChipType
    {
        TypeAM,
        TypeBM,
        /// <summary>
        /// FT2232C
        /// </summary>
        Type2232C,
        /// <summary>
        /// FT232R
        /// </summary>
        TypeR,
        /// <summary>
        /// FT2232H
        /// </summary>
        Type2232H,
        /// <summary>
        /// FT4232H
        /// </summary>
        Type4232H,
        /// <summary>
        /// FT232H
        /// </summary>
        Type232H,
        /// <summary>
        /// FT230X
        /// </summary>
        Type230X,
    }

    public enum FtdiModuleDetachMode
    {
        AutoDetachSioModule,
        DontDetachSioModule,
    }

    /// <summary>
    /// MPSSE mode
    /// </summary>
    public enum FtdiMpsseMode : byte
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

    /// <summary>
    /// Device interface
    /// </summary>
    public enum FtdiInterface
    {
        /// <summary>
        /// Any interface available.
        /// </summary>
        Any,
        /// <summary>
        /// Interface A
        /// </summary>
        A,
        /// <summary>
        /// Interface B
        /// </summary>
        B,
        /// <summary>
        /// Interface C
        /// </summary>
        C,
        /// <summary>
        /// Interface D
        /// </summary>
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
