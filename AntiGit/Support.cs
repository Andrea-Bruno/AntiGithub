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
	}
}
