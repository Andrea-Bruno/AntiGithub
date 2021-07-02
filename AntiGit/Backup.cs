using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
#if !MAC
using System.Runtime.InteropServices;
#else
using System.Diagnostics;
#endif

namespace AntiGit
{
	internal class Backup
	{
#if MAC
		static int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags)
		{
			ExecuteMacCommand("ln","-s \"" + lpTargetFileName + "\" \"" + lpSymlinkFileName + "\"");
			return 0;
		}
		static bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes)
		{
			ExecuteMacCommand("ln"," \"" + lpExistingFileName + "\" \"" + lpFileName + "\"");
			return true;
		}
		public static void ExecuteMacCommand(string command, string arguments, bool hidden = true)
		{
			var proc = new Process {StartInfo = {FileName = command, Arguments = arguments}};
			proc.Start();
			proc.WaitForExit();
		}
#else
		[DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

		[DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
#endif

		public Backup(AntiGit context)
		{
			Context = context;
		}
		private readonly AntiGit Context;
		internal DateTime LastBackup;
		private Thread backupThread;
		internal bool BackupRunning;

		public void Start(string SourceDir, string TargetDir, bool daily = true)
		{
			if (backupThread == null || !backupThread.IsAlive)
			{
				if (!string.IsNullOrEmpty(SourceDir) && !string.IsNullOrEmpty(TargetDir))
				{
					if (!new DirectoryInfo(SourceDir).Exists)
					{
						AntiGit.WriteOutput("Source not found!");
						return;
					}
					if (!new DirectoryInfo(TargetDir).Exists)
					{
						AntiGit.WriteOutput("Target not found!");
						return;
					}
					var readmeFile = Path.Combine(TargetDir, "readme.txt");
					if (!File.Exists(readmeFile))
						File.WriteAllText(readmeFile, "To restore the backup you need to copy the desired version where you prefer, via the command line (copy and paste does not work)");
					backupThread = new Thread(() =>
					{
						LastBackup = DateTime.UtcNow;
						BackupRunning = true;
						var today = DateTime.Now;
						var targetPath = daily ? Path.Combine(TargetDir, today.ToString("dd MM yyyy", CultureInfo.InvariantCulture)) : Path.Combine(TargetDir, "today", today.ToString("HH mm", CultureInfo.InvariantCulture));
						DirectoryInfo target;
						if (daily)
						{
							target = new DirectoryInfo(TargetDir);
						}
						else
						{
							var yesterday = new DirectoryInfo(Path.Combine(TargetDir, "yesterday"));
							if (yesterday.Exists && yesterday.CreationTime.DayOfYear != DateTime.Now.AddDays(-1).DayOfYear)
							{
								yesterday.Delete(true);
							}
							var todayInfo = new DirectoryInfo(Path.Combine(TargetDir, "today"));
							if (todayInfo.Exists && todayInfo.CreationTime.DayOfYear != DateTime.Now.DayOfYear)
							{
								yesterday = new DirectoryInfo(Path.Combine(TargetDir, "yesterday"));
								if (yesterday.Exists)
									yesterday.Delete(true);
								todayInfo.MoveTo(yesterday.FullName);

							}
							todayInfo = new DirectoryInfo(Path.Combine(TargetDir, "today"));
							if (!todayInfo.Exists)
							{
								todayInfo.Create();
							}
							target = todayInfo;
						}

						DirectoryInfo oltDir = null;
						foreach (var dir in target.GetDirectories())
						{
							if (dir.FullName != targetPath && dir.Name.Split().Length == (daily ? 3 : 2))
							{
								if (oltDir == null || dir.CreationTimeUtc >= oltDir.CreationTimeUtc)
									oltDir = dir;
							}
						}

						var oldTargetPath = oltDir?.FullName;

						ExecuteBackup(!daily, SourceDir, targetPath, oldTargetPath);
						BackupRunning = false;
					})
					{ Priority = ThreadPriority.Lowest };
					backupThread.Start();
				}
			}
		}

		private bool ExecuteBackup(bool skipIfThereAreNoChanges, string sourcePath, string targetPath, string oldTargetPath, string sourceRoot = null)
		{
			if (!BackupRunning)
				return false;
			var rootRecursive = false;
			if (sourceRoot == null)
			{
				rootRecursive = true;
				sourceRoot = sourcePath;
			}
			var dirOperation = new FileOperation();
			var fileAreChanged = false;
			try
			{

				var dir = new DirectoryInfo(sourcePath);
				var nameDir = dir.Name.ToLower();
				if (!AntiGit.ExcludeDir.Contains(nameDir) && (dir.Attributes & FileAttributes.Hidden) == 0)
				{
					var relativeDirName = sourcePath.Substring(sourceRoot.Length);
					var targetDirName = targetPath + relativeDirName;
					Spooler.Add(dirOperation);
					foreach (var fileInfo in dir.GetFiles())
					{
						if ((fileInfo.Attributes & FileAttributes.Hidden) == 0)
						{
							var originalFile = fileInfo.FullName;
							var relativeFileName = originalFile.Substring(sourceRoot.Length);
							var targetFile = targetPath + relativeFileName;
							if (oldTargetPath != null)
							{
								var oldFile = oldTargetPath + relativeFileName;
								if (FilesAreEqual(originalFile, oldFile))
								{
									Spooler.Add(new FileOperation(TypeOfOperation.LinkFile, targetFile, oldFile));
									continue;
								}
							}
							Spooler.Add(new FileOperation(TypeOfOperation.CopyFile, originalFile, targetFile));
							fileAreChanged = true;
						}
					}
					foreach (var sourceDir in Directory.GetDirectories(sourcePath))
					{
						fileAreChanged |= ExecuteBackup(skipIfThereAreNoChanges, sourceDir, targetPath, oldTargetPath, sourceRoot);
					}
					if (fileAreChanged || oldTargetPath == null)
					{
						dirOperation.Operation = TypeOfOperation.CreateDirectory;
						dirOperation.Param1 = targetDirName;
					}
					else
					{
						var oldTargetDirName = oldTargetPath + relativeDirName;
						dirOperation.Operation = TypeOfOperation.LinkDirectory;
						dirOperation.Param1 = targetDirName;
						dirOperation.Param2 = oldTargetDirName;
					}
					if (rootRecursive)
					{
						if (!skipIfThereAreNoChanges || fileAreChanged)
						{
							Spooler.ForEach(operation => operation.Execute());
						}
					}
				}
			}
			catch (Exception ex)
			{
				AntiGit.WriteOutput(ex.Message);
			}
			return fileAreChanged;
		}

		private readonly List<FileOperation> Spooler = new List<FileOperation>();
		class FileOperation
		{
			public FileOperation()
			{
			}
			public FileOperation(TypeOfOperation operation, string param1, string param2 = null)
			{
				Operation = operation;
				Param1 = param1;
				Param2 = param2;
			}
			public TypeOfOperation Operation = TypeOfOperation.Nothing;
			public string Param1;
			public string Param2;
			public void Execute()
			{
				switch (Operation)
				{
					case TypeOfOperation.CreateDirectory:
						if (!Directory.Exists(Param1))
						{
							//AntiGit.WriteOutput("create directory  " + Param1);
							Directory.CreateDirectory(Param1);
						}
						break;
					case TypeOfOperation.LinkDirectory:
						if (!Directory.Exists(Param1))
						{
							//AntiGit.WriteOutput("link directory  " + Param2 + " => " + Param1);
							CreateSymbolicLink(Param1, Param2, 1); // The parameter 1 = directory
							if (checkedIsAdmin == false)
							{
								checkedIsAdmin = true;
								var dir = new DirectoryInfo(Param1);
								if (!dir.Exists)
								{
									AntiGit.RequestAdministrationMode();
								}
							}
						}
						break;
					case TypeOfOperation.LinkFile:
						//AntiGit.WriteOutput("link " + Param2 + " => " + Param1);
						// These files from cannot be copied easily, you need to use the terminal command
						CreateHardLink(Param1, Param2, IntPtr.Zero);
						//CreateSymbolicLink(Param1, Param2, 0);// The parameter 0 = file
						break;
					case TypeOfOperation.CopyFile:
						File.Copy(Param1, Param2, true);
						//AntiGit.WriteOutput("copy " + Param1 + " => " + Param2);
						break;
				}
			}
			private static bool checkedIsAdmin;
		}

		enum TypeOfOperation
		{
			Nothing,
			CreateDirectory,
			LinkDirectory,
			LinkFile,
			CopyFile,
		}

		public static bool FilesAreEqual(string nameFile1, string nameFile2)
		{
			var fileInfo1 = new FileInfo(nameFile1);
			var fileInfo2 = new FileInfo(nameFile2);

			if (fileInfo1.Exists != fileInfo2.Exists)
				return false;

			if (fileInfo1.Length != fileInfo2.Length)
			{
				return false;
			}

			if (fileInfo1.CreationTimeUtc == fileInfo2.CreationTimeUtc)
			{
				return true;
			}
			using (var file1 = fileInfo1.OpenRead())
			{
				using (var file2 = fileInfo2.OpenRead())
				{
					return StreamsContentsAreEqual(file1, file2);
				}
			}
		}

		private static bool StreamsContentsAreEqual(Stream stream1, Stream stream2)
		{
			const int bufferSize = 1024 * sizeof(Int64);
			var buffer1 = new byte[bufferSize];
			var buffer2 = new byte[bufferSize];

			while (true)
			{
				var count1 = stream1.Read(buffer1, 0, bufferSize);
				var count2 = stream2.Read(buffer2, 0, bufferSize);

				if (count1 != count2)
				{
					return false;
				}

				if (count1 == 0)
				{
					return true;
				}

				if (!buffer1.SequenceEqual(buffer2))
				{
					return false;
				}

			}
		}

	}
}
