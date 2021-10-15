using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
#if !MAC
using System.Runtime.InteropServices;
#endif

namespace AntiGitLibrary
{
	internal class Backup
	{
#if MAC
		static int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags)
		{
			Support.ExecuteMacCommand("ln", "-s \"" + lpTargetFileName + "\" \"" + lpSymlinkFileName + "\"");
			return 0;
		}
		static bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes)
		{
			Support.ExecuteMacCommand("ln", " \"" + lpExistingFileName + "\" \"" + lpFileName + "\"");
			return true;
		}
#else
		[DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

		[DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
#endif

		public Backup(Context context)
		{
			Context = context;
		}
		private readonly Context Context;
		internal DateTime LastBackup;
		private Thread backupThread;
		internal bool StopBackup;
		internal int BackupRunning;

		public void Start(string SourceDir, string TargetDir, bool daily = true)
		{
			if (backupThread == null || !backupThread.IsAlive)
			{
				if (!string.IsNullOrEmpty(SourceDir) && !string.IsNullOrEmpty(TargetDir))
				{
					if (!new DirectoryInfo(SourceDir).Exists)
					{
						Context.WriteOutput(Resources.Dictionary.SourceNotFound);
						return;
					}
					if (!new DirectoryInfo(TargetDir).Exists)
					{
						Context.WriteOutput(Resources.Dictionary.TargetNotFound);
						return;
					}
					var readmeFile = Path.Combine(TargetDir, "readme.txt");
					if (!File.Exists(readmeFile))
						File.WriteAllText(readmeFile, Resources.Dictionary.Instruction1);
					backupThread = new Thread(() =>
					{
						LastBackup = DateTime.UtcNow;
						BackupRunning++;
						var today = DateTime.Now;
						var targetPath = daily ? Path.Combine(TargetDir, today.ToString("yyyy MM dd", CultureInfo.InvariantCulture)) : Path.Combine(TargetDir, "today", today.ToString("HH mm", CultureInfo.InvariantCulture));
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

						ExecuteBackup(SourceDir, targetPath, oldTargetPath, new List<FileOperation>());
						BackupRunning--;
					})
					{ Priority = ThreadPriority.Lowest };
					backupThread.Start();
				}
			}
		}

		private bool ExecuteBackup(string sourcePath, string targetPath, string oldTargetPath, List<FileOperation> spooler, string sourceRoot = null)
		{
			if (StopBackup)
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
				if (!Support.ToExclude(dir.Name) && (dir.Attributes & FileAttributes.Hidden) == 0)
				{
					var relativeDirName = sourcePath.Substring(sourceRoot.Length);
					var targetDirName = targetPath + relativeDirName;
					spooler.Add(dirOperation);
					var addToSpooler = new List<FileOperation>();
					foreach (var fileInfo in dir.GetFiles())
					{
						var file = fileInfo;
						file = Support.WaitFileUnlocked(file);
						if ((file.Attributes & FileAttributes.Hidden) == 0)
						{
							var originalFile = file.FullName;
							var relativeFileName = originalFile.Substring(sourceRoot.Length);
							var targetFile = targetPath + relativeFileName;
							if (oldTargetPath != null)
							{
								var oldFile = oldTargetPath + relativeFileName;
								if (FilesAreEqual(originalFile, oldFile))
								{
									addToSpooler.Add(new FileOperation(TypeOfOperation.LinkFile, targetFile, oldFile));
									continue;
								}
							}
							addToSpooler.Add(new FileOperation(TypeOfOperation.CopyFile, originalFile, targetFile));
							fileAreChanged = true;
						}
					}
					foreach (var sourceDir in Directory.GetDirectories(sourcePath))
					{
						fileAreChanged |= ExecuteBackup(sourceDir, targetPath, oldTargetPath, spooler, sourceRoot);
					}
					if (fileAreChanged || oldTargetPath == null)
					{
						spooler.AddRange(addToSpooler);
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
						if (fileAreChanged)
						{
							spooler.ForEach(operation => operation.Execute());
						}
					}
				}
			}
			catch (Exception e)
			{
				if (Support.IsDiskFull(e))
					Context.Alert(e.Message, true);
				else
					Context.WriteOutput(e.Message);
			}
			return fileAreChanged;
		}

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
							Directory.CreateDirectory(Param1);
						}
						break;
					case TypeOfOperation.LinkDirectory:
						if (!Directory.Exists(Param1))
						{
							// we use the relative position otherwise it gives an error if we rename the directory
							var targetRelativeDir = Support.GetRelativePath(Param1, Param2);
							CreateSymbolicLink(Param1, targetRelativeDir, 1); // The parameter 1 = directory
																																//CreateSymbolicLink(Param1, Param2, 1); // The parameter 1 = directory
							if (checkedIsAdmin)
							{
								checkedIsAdmin = true;
								var dir = new DirectoryInfo(Param1);
								if (!dir.Exists)
								{
									Context.RequestAdministrationMode(Resources.Dictionary.Warning3);
								}
							}
						}
						break;
					case TypeOfOperation.LinkFile:
						var targetRelativeFile = Support.GetRelativePath(Param1, Param2);
						CreateHardLink(Param1, targetRelativeFile, IntPtr.Zero); // These files from cannot be copied easily, you need to use the terminal command
																																		 //CreateHardLink(Param1, Param2, IntPtr.Zero); // These files from cannot be copied easily, you need to use the terminal command
																																		 //CreateSymbolicLink(Param1, Param2, 0);// The parameter 0 = file 
						break;
					case TypeOfOperation.CopyFile:
						Support.WaitFileUnlocked(Param1);
						Support.WaitFileUnlocked(Param2);
						File.Copy(Param1, Param2, true);
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
			fileInfo1 = Support.WaitFileUnlocked(fileInfo1);
			var fileInfo2 = new FileInfo(nameFile2);
			fileInfo2 = Support.WaitFileUnlocked(fileInfo2);
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
			const int bufferSize = 1024 * sizeof(long);
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
