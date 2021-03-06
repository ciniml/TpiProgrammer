﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FtdiBinding.Native
{
    /// <summary>
    /// Loads a native library and its dependencies before actually using them.
    /// </summary>
    class Preloader
    {
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008u;

        [DllImport("kernel32",SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        public static void Preload(string libraryFileName)
        {
            var path = (Environment.Is64BitProcess ? @"Native\x64\" : @"Native\x86\") + libraryFileName;
            if (LoadLibraryEx(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) == IntPtr.Zero)
            {
                throw new Exception(String.Format("Failed to load library {0}", libraryFileName));
            }
        }
    }
}
