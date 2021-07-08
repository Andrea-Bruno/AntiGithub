using System;
using System.Diagnostics;

namespace AntiGit
{
	static class Support
	{
#if MAC
		public static void ExecuteMacCommand(string command, string arguments)
		{
			var proc = new Process { StartInfo = { FileName = command, Arguments = arguments } };
			proc.Start();
			proc.WaitForExit();
		}
#endif
		public static bool IsDiskFull(Exception ex)
		{
			const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
			const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
			return ex.HResult == HR_ERROR_HANDLE_DISK_FULL
			       || ex.HResult == HR_ERROR_DISK_FULL;
		}

		private static DateTime RoundDate(DateTime dt)
		{
			// FAT / VFAT has a maximum resolution of 2s
			// NTFS has a maximum resolution of 100 ns
#if MAC
			var add = 0;
#else
			var add = dt.Millisecond < 500 ? 0 : 1; // 0 - 499 round to lowers, 500 - 999 to upper
#endif
			return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second + add);
		}





	}




}
