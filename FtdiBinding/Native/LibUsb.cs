using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FtdiBinding.Native
{
    public static class LibUsb
    {
        private const string LibUsbFileName = @"libusb-1.0.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct libusb_device_descriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfiggurations;
        }

        [Flags]
        public enum libusb_hotplug_event
        {
            LIBUSB_HOTPLUG_EVENT_DEVICE_ARRIVED = 0x01,
            LIBUSB_HOTPLUG_EVENT_DEVICE_LEFT = 0x02,
        }

        [Flags]
        public enum libusb_hotplug_flag
        {
            LIBUSB_HOTPLUG_ENUMERATE = 0x01,
        }

        public const int LIBUSB_HOTPLUG_MATCH_ANY = -1;

        private static readonly Preloader preloader = new Preloader(LibUsbFileName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Ansi)]
        public delegate int libusb_hotplug_callback_fn(IntPtr ctx, IntPtr device, [MarshalAs(UnmanagedType.I4)]libusb_hotplug_event evt, IntPtr user_data);

        [DllImport(LibUsbFileName, CharSet = CharSet.Ansi)]
        public static extern int libusb_hotplug_register_callback(
            IntPtr context, libusb_hotplug_event events, libusb_hotplug_flag flags,
            int vendor_id, int product_id, int dev_class,
            libusb_hotplug_callback_fn cb_fn, IntPtr user_data,
            out IntPtr handle);

        [DllImport(LibUsbFileName, CharSet = CharSet.Ansi)]
        public static extern void libusb_hotplug_deregister_callback(IntPtr context, IntPtr handle);

        [DllImport(LibUsbFileName, CharSet = CharSet.Ansi)]
        public static extern IntPtr libusb_get_device(IntPtr dev_handle);

        [DllImport(LibUsbFileName, CharSet = CharSet.Ansi)]
        public static extern int libusb_get_device_descriptor(IntPtr context, out libusb_device_descriptor desc);
    }
}
