using System;
using System.Runtime.InteropServices;

namespace BackupLibrary
{
    public class OSSupport
    {
        // OSX & Linux
        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int symlink(string target, string linkpath);

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int link(string oldpath, string newpath);

        // Windows
        public const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;
        public const int SYMBOLIC_LINK_FLAG_FILE = 0x0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);


    }
}











