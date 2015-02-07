using System;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;

namespace FtdiBinding.Native
{

    public static class LibFtdi
    {
        private const string LibFtdiFileName = @"libftdi1";
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct FtdiVersionInfo
        {
            public int Major;
            public int Minor;
            public int Micro;
            public IntPtr VersionString;
            public IntPtr SnapshotString;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct FtdiDeviceList
        {
            public IntPtr Next;
            public IntPtr Device;
        }

        //[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        //public struct FtdiContext
        //{
        //    public IntPtr UsbContext;
        //    public IntPtr UsbDeviceHandle;
        //    public int UsbReadTimeout;
        //    public int UsbWriteTimeout;
        //    [MarshalAs(UnmanagedType.I4)]
        //    public FtdiChipType Type;
        //    public int BaudRate;
        //    public byte BitBangEnabled;
        //    public IntPtr ReadBuffer;
        //    public uint ReadBufferOffset;
        //    public uint ReadBufferRemaining;
        //    public uint ReadBufferChunkSize;
        //    public uint WriteBufferChunkSize;
        //    public uint MaxPacketSize;
        //    public int Interface;
        //    public int Index;
        //    public int InEndPoint;
        //    public int OutEndPoint;
        //    [MarshalAs(UnmanagedType.U1)]
        //    public FtdiMpsseMode BitBangMode;
        //    public IntPtr Eeprom;
        //    public IntPtr ErrorString;
        //    [MarshalAs(UnmanagedType.I4)]
        //    public FtdiModuleDetachMode ModuleDetachMode;
        //}

        private static readonly Preloader preloader = new Preloader(LibFtdiFileName);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_init(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ftdi_deinit(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern FtdiContext ftdi_new();
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ftdi_free(IntPtr ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_interface(FtdiContext ftdi, FtdiInterface @interface);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern FtdiVersionInfo ftdi_get_library_version();

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ftdi_list_free2(IntPtr devlist);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_find_all(FtdiContext ftdi, out IntPtr devlist, int vendor, int product);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_get_strings(FtdiContext ftdi, IntPtr dev, StringBuilder manufacturer,
            int mnf_len, StringBuilder description, int desc_len, StringBuilder serial, int serial_len);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_open_dev(FtdiContext ftdi, IntPtr device);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_open(FtdiContext ftdi, IntPtr device, int vendor, int product);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_open_desc(FtdiContext ftdi, IntPtr device, int vendor, int product, string description, string serial);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_open_desc_index(FtdiContext ftdi, int vendor, int product, string description, string serial, uint index);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_open_string(FtdiContext ftdi, string description);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_reset(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_purge_rx_buffer(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_purge_tx_buffer(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_purge_buffers(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_usb_close(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_baudrate(FtdiContext ftdi, int baudrate);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_line_property(FtdiContext ftdi, FtdiBits bits, FtdiStopBits stopbits, FtdiParity parity);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_line_property2(FtdiContext ftdi, FtdiBits bits, FtdiStopBits stopbits, FtdiParity parity, FtdiBreak @break);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_data(FtdiContext ftdi, IntPtr buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_data(FtdiContext ftdi, byte[] buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ftdi_write_data_submit(FtdiContext ftdi, IntPtr buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ftdi_write_data_submit(FtdiContext ftdi, byte[] buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_data_set_chunksize(FtdiContext ftdi, uint chunksize);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_data_get_chunksize(FtdiContext ftdi, out uint chunksize);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_data(FtdiContext ftdi, IntPtr buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_data(FtdiContext ftdi, byte[] buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ftdi_read_data_submit(FtdiContext ftdi, IntPtr buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ftdi_read_data_submit(FtdiContext ftdi, byte[] buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_data_set_chunksize(FtdiContext ftdi, uint chunksize);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_data_get_chunksize(FtdiContext ftdi, out uint chunksize);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_transfer_data_done(IntPtr tc);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_bitmode(FtdiContext ftdi, byte bitmask, [MarshalAs(UnmanagedType.U1)]FtdiMpsseMode mode);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_disable_bitbang(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_pins(FtdiContext ftdi, out byte pins);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_latency_timer(FtdiContext ftdi, byte latency);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_get_latency_timer(FtdiContext ftdi, out byte latency);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_poll_modem_status(FtdiContext ftdi, out ushort status);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_setflowctrl(FtdiContext ftdi, int flowctrl);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_setdtr(FtdiContext ftdi, int state);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_setrts(FtdiContext ftdi, int state);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_setdts_rts(FtdiContext ftdi, int dtr, int rts);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_event_char(FtdiContext ftdi, byte eventch, byte enable);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_error_char(FtdiContext ftdi, byte errorch, byte enable);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_eeprom_initdefaults(FtdiContext ftdi, string manufacturer, string product, string serial);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_eeprom_set_strings(FtdiContext ftdi, string manufacturer, string product, string serial);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_eeprom_build(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_eeprom_decode(FtdiContext ftdi, int verbose);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_get_eeprom_value(FtdiContext ftdi, FtdiEepromValue value_name, out int value);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_eeprom_value(FtdiContext ftdi, FtdiEepromValue value_name, int value);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_get_eeprom_buf(FtdiContext ftdi, byte[] buf, int size);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_set_eeprom_buf(FtdiContext ftdi, byte[] buf, int size);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_eeprom_location(FtdiContext ftdi, int eeprom_addr, out ushort eeprom_val);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_eeprom(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_read_chipid(FtdiContext ftdi, out uint chipid);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_eeprom_location(FtdiContext ftdi, int eeprom_addr, ushort eeprom_val);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_write_eeprom(FtdiContext ftdi);

        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ftdi_erase_eeprom(FtdiContext ftdi);
        [DllImport(LibFtdiFileName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ftdi_get_error_string(FtdiContext ftdi);

        [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
        [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
        public class FtdiContext : SafeHandle
        {
            public FtdiContext() : base(IntPtr.Zero, true)
            {
            }

            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    ftdi_free(this.handle);
                }
                return true;
            }

            public override bool IsInvalid => this.handle == IntPtr.Zero;

            
            public IntPtr UsbContext
            {
                get
                {
                    if(this.IsInvalid) throw new InvalidOperationException();
                    return Marshal.ReadIntPtr(this.handle, 0);
                }
            }
            public IntPtr UsbDeviceHandle
            {
                get
                {
                    if (this.IsInvalid) throw new InvalidOperationException();
                    return Marshal.ReadIntPtr(this.handle, IntPtr.Size);
                }
            }
        }
        
    }
}
