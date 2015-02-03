using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace FtdiBinding.Test
{
    public class NativeMethods 
    {
        [Fact]
        public void GetVersion()
        {
            var version = LibFtdi.ftdi_get_library_version();
            Assert.Equal(1, version.Major);
            Assert.Equal(2, version.Minor);
            var versionString = Marshal.PtrToStringAnsi(version.VersionString);
            Assert.Equal("1.2", versionString);
        }

        [Fact]
        public void NewContext()
        {
            using (var context = LibFtdi.ftdi_new())
            {
                Assert.True(!context.IsInvalid);
                Assert.True(!context.IsClosed);
            }
        }

        [Fact]
        public void DeviceList()
        {
            using (var context = LibFtdi.ftdi_new())
            {
                var deviceList = new LibFtdi.FtdiDeviceList();
                LibFtdi.ftdi_init(context);
                try
                {
                    IntPtr devlist;
                    var count = LibFtdi.ftdi_usb_find_all(context, out devlist, 0, 0);
                    deviceList = new LibFtdi.FtdiDeviceList(devlist);
                    Assert.True(count >= 0);
                    Assert.False(deviceList.IsInvalid);
                    Assert.False(deviceList.IsClosed);
                }
                finally
                {
                    deviceList.Close();
                    LibFtdi.ftdi_deinit(context);
                }
            }
        }
    }
}
