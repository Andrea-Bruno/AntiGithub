using System;
using System.Diagnostics;
using System.IO;

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

		/// <summary>
		/// Creates a relative path from one file or folder to another.
		/// </summary>
		/// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
		/// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
		/// <returns>The relative path from the start directory to the end path or <c>toPath</c> if the paths are not related.</returns>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="UriFormatException"></exception>
		/// <exception cref="InvalidOperationException"></exception>
		public static string GetRelativePath(string fromPath, string toPath)
		{
			if (string.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
			if (string.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");
			var fromUri = new Uri(fromPath);
			var toUri = new Uri(toPath);
			if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.
			var relativeUri = fromUri.MakeRelativeUri(toUri);
			var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
			if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
			{
				relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			}
			return relativePath;
		}
		public static bool IsFileLocked(FileInfo file)
		{
			if (file.Exists && file.Length == 0)
			{
				try
				{
					using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
					{
						stream.Close();
					}
				}
				catch (IOException)
				{
					//the file is unavailable because it is:
					//still being written to
					//or being processed by another thread
					return true;
				}
			}
			//file is not locked
			return false;
		}

		public static FileInfo WaitFileUnlocked(FileInfo file)
		{
			int attempt = 0;
			bool isLocked;
			do
			{
				isLocked = IsFileLocked(file);
				if (isLocked)
				{
					attempt++;
					if (attempt == 320) // 5 min - Note: It may be that a copy is in progress, or other normal operations use the file, before giving a warning we wait for the normal operations to be finished
					{
						Context.Alert(Resources.Dictionary.FileLocked + " " + file.FullName);
					}
					System.Threading.Thread.Sleep(1000);
					file = new FileInfo(file.FullName);
				}
			} while (isLocked);
			return file;
		}
		public static void WaitFileUnlocked(string fileName)
		{
			_ = WaitFileUnlocked(new FileInfo(fileName));
		}
	}
}
