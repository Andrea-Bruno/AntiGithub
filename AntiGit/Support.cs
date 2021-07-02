using System;
using System.Collections.Generic;
using System.Text;

namespace AntiGit
{
	static class Support
	{
		public static bool IsDiskFull(Exception ex)
		{
			const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
			const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
			return ex.HResult == HR_ERROR_HANDLE_DISK_FULL
			       || ex.HResult == HR_ERROR_DISK_FULL;
		}
	}
}
