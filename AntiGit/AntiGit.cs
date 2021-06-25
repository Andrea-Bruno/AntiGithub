using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
#if !MAC
using System.Runtime.InteropServices;
#endif
using System.Threading;

namespace AntiGit
{
	public class AntiGit
	{
		public string Info = "The source directory is the one with the files to keep, in the target directory the daily backups will be saved, the git directory is a remote directory accessible to all those who work on the same source files, for example, the git directory can correspond to a disk of network or to the address of a pen drive connected to the router, in this directory AntiGit will create a synchronized version of the source in real time.";
#if MAC
		static int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags)
		{
			ExecuteMacCommand("ln -s \"" + lpTargetFileName + "\" \"" + lpSymlinkFileName + "\"");
			return 0;
		}
		static bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes)
		{
			ExecuteMacCommand("ln \"" + lpExistingFileName + "\" \"" + lpFileName + "\"");
			return true;
		}
		public static void ExecuteMacCommand(string command, bool hidden = true)
		{
			Process proc = new Process();
			proc.StartInfo.FileName = @"/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal";
			proc.StartInfo.Arguments = "-c \" " + command + " \"";
			proc.StartInfo.UseShellExecute = false;
			proc.StartInfo.RedirectStandardOutput = true;
			proc.StartInfo.CreateNoWindow = hidden;
			proc.Start();

			while (!proc.StandardOutput.EndOfStream)
			{
				WriteOutput(proc.StandardOutput.ReadLine());
			}
		}
#else
		[DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		static extern int CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

		[DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
		static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
#endif


		public AntiGit()
		{
			_sourceDir = GetValue("source"); // ?? Directory.GetCurrentDirectory();
			_targetDir = GetValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");

			if (string.IsNullOrEmpty(_targetDir))
				_targetDir = GetDefaultBackupPosition();
			_gitDir = GetValue("git");
			dailyTimer = new System.Threading.Timer((x) => { Startbackup(); }, null, new TimeSpan(1, 0, 0, 0), new TimeSpan(1, 0, 0, 0));

			Startbackup();
			SyncGit();
		}

		static public void WriteOutput(string text)
		{
			Debug.WriteLine(text);
			//Console.WriteLine(text);
		}
		public void SyncGit()
		{
			fullSyncGit(_sourceDir, _gitDir);
		}

		private static string GetDefaultBackupPosition()
		{
			try
			{
				string result = null;
				foreach (var drive in DriveInfo.GetDrives())
				{
					if (drive.IsReady)
					{
						result = Path.Combine(drive.Name, "backup");
					}
				}
				return result;
			}
			catch (Exception)
			{


			}

			return null;
		}

		static string[] ExcludeDir = { "bin", "obj", ".vs", "packages" };

		private string _sourceDir;
		public string SourceDir
		{
			get => _sourceDir;
			set { _sourceDir = value; SetValue("source", value); }
		}
		private string _targetDir;

		public string TargetDir
		{
			get => _targetDir;
			set { _targetDir = value; SetValue("target", value); }

		}
		private string _gitDir;

		public string GitDir
		{
			get => _gitDir;
			set { _gitDir = value; SetValue("git", value); }
		}


		private System.Threading.Timer dailyTimer;

		public string GetValue(string name)
		{
			var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".txt");
			return File.Exists(file) ? File.ReadAllText(file) : null;
		}

		public void SetValue(string name, string value)
		{
			var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".txt");
			File.WriteAllText(file, value);
		}

		private Thread backupThread;
		public bool BackupRunning = false;
		public void Startbackup()
		{
			if (backupThread == null || !backupThread.IsAlive)
			{
				if (!string.IsNullOrEmpty(SourceDir) && !string.IsNullOrEmpty(TargetDir))
				{
					backupThread = new Thread(() =>
						{
							BackupRunning = true;
							var today = DateTime.Now;
							var targetPath = Path.Combine(TargetDir, today.ToString("dd MM yyyy", CultureInfo.InvariantCulture));
							var target = new DirectoryInfo(TargetDir);
							DirectoryInfo oltDir = null;
							foreach (var dir in target.GetDirectories())
							{
								if (dir.FullName != targetPath && dir.Name.Split().Length == 3)
								{
									if (oltDir == null || dir.CreationTimeUtc >= oltDir.CreationTimeUtc)
										oltDir = dir;
								}
							}

							var oldTargetPath = oltDir?.FullName;

							//var oldTargetPath = Path.Combine(TargetDir, today.AddDays(-1).ToString("dd MM yyyy", CultureInfo.InvariantCulture));
							//if (!Directory.Exists(oldTargetPath))
							//{
							//	oldTargetPath = null;
							//}
							ExecuteBackup(SourceDir, targetPath, oldTargetPath);
							BackupRunning = false;
						})
					{ Priority = ThreadPriority.Lowest };
					backupThread.Start();
				}
			}
		}

		private bool ExecuteBackup(string sourcePath, string targetPath, string oldTargetPath, string sourceRoot = null)
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
				if (!ExcludeDir.Contains(nameDir) && (dir.Attributes & FileAttributes.Hidden) == 0)
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
						fileAreChanged |= ExecuteBackup(sourceDir, targetPath, oldTargetPath, sourceRoot);
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
						Spooler.ForEach(operation => { operation.execute(); });
					}
				}
			}
			catch (System.Exception ex)
			{
				WriteOutput(ex.Message);
			}
			return fileAreChanged;
		}

		private List<FileOperation> Spooler = new List<FileOperation>();
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
			public void execute()
			{
				switch (Operation)
				{
					case TypeOfOperation.CreateDirectory:
						if (!Directory.Exists(Param1))
						{
							WriteOutput("create directory  " + Param1);
							Directory.CreateDirectory(Param1);
						}
						break;
					case TypeOfOperation.LinkDirectory:
						if (!Directory.Exists(Param1))
						{
							WriteOutput("link directory  " + Param2 + " => " + Param1);
							CreateSymbolicLink(Param1, Param2, 1);
							if (checkedIsAdmin == false)
							{
								checkedIsAdmin = true;
								var dir = new DirectoryInfo(Param1);
								if (!dir.Exists)
								{
									Console.WriteLine("Error: The application must be run in administrator mode");
									Debugger.Break();
								}
							}
						}
						break;
					case TypeOfOperation.LinkFile:
						WriteOutput("link " + Param2 + " => " + Param1);
						CreateHardLink(Param1, Param2, IntPtr.Zero);
						break;
					case TypeOfOperation.CopyFile:
						File.Copy(Param1, Param2, true);
						WriteOutput("copy " + Param1 + " => " + Param2);
						break;
				}
			}
			static private bool checkedIsAdmin;
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
			bool result;

			if (fileInfo1.Length != fileInfo2.Length)
			{
				return false;
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


		private Thread gitTask;

		void fullSyncGit(string sourcePath, string targetPath)
		{
			var gitTask = new Thread(() =>
			{
				_stopSync = false;
				while (!_stopSync)
				{
					if (!string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(targetPath))
					{
						SyncGit(Scan.LocalDrive, sourcePath, targetPath);
						SyncGit(Scan.RemoteDrive, targetPath, sourcePath);
					}
					else
					{
						Thread.Sleep(10000);
					}
				}
				_stopSync = true;
			})
			{ Priority = ThreadPriority.Lowest };
			gitTask.Start();
		}
		private bool _stopSync = true;

		public void StopSyncGit()
		{
			_stopSync = true;
		}

		public bool SyncGitRunning => !_stopSync;

		void SyncGit(Scan scan, string sourcePath, string targetPath, string sourceRoot = null, DateTime? updateLimit = null)
		{
			if (_stopSync)
				return;
			if (sourceRoot == null)
			{
				sourceRoot = sourcePath;
			}
#if RELEASE
			try
			{
#endif
			var relativeDirName = sourcePath.Substring(sourceRoot.Length);
			var targetDirName = targetPath + relativeDirName;

			var dir = new DirectoryInfo(sourcePath);
			var nameDir = dir.Name.ToLower();
			if (!ExcludeDir.Contains(nameDir) && (dir.Attributes & FileAttributes.Hidden) == 0)
			{
				var dirTarget = new DirectoryInfo(targetDirName);
				FileInfo[] targetFiles = null;
				if (!dirTarget.Exists)
				{
					WriteOutput("create directory  " + targetDirName);
					Directory.CreateDirectory(targetDirName);
				}
				else
				{
					targetFiles = dirTarget.GetFiles();
				}

				DirectoryInfo localDir = scan == Scan.LocalDrive ? dir : dirTarget;
				if (updateLimit == null)
					updateLimit = UpdateDateLimit(localDir);// Copy to remote only the files of the latest version working at compile time

				foreach (var file in dir.GetFiles())
				{
					if (_stopSync) break;

					//if (file.FullName == @"C:\Users\USER\OneDrive\Sorgenti\BitBoxLab\CryptoMessenger\TelegraphWhiteLabel\TelegraphWhiteLabel\App.xaml.cs")
					//{
					//	Console.WriteLine(file.FullName);
					//}

					FileInfo localFile;
					var targetFile = Path.Combine(targetDirName, file.Name);
					var target = targetFiles?.ToList().Find(x => x.Name == targetFile) ?? new FileInfo(targetFile);
					localFile = scan == Scan.LocalDrive ? file : target;


					FileInfo from = null;
					FileInfo to = null;
					var copy = CopyType.None;
					if (!target.Exists || RoundDate(file.LastWriteTimeUtc) > RoundDate(target.LastWriteTimeUtc))
					{
						copy = scan == Scan.LocalDrive ? CopyType.CopyToRemote : CopyType.CopyFromRemote;
						from = file;
						to = target;
					}
					else if (RoundDate(file.LastWriteTimeUtc) < RoundDate(target.LastWriteTimeUtc))
					{
						copy = scan == Scan.LocalDrive ? CopyType.CopyFromRemote : CopyType.CopyToRemote;
						from = target;
						to = file;
					}

					if (copy == CopyType.None) continue;


					if (copy == CopyType.CopyToRemote && updateLimit != null && updateLimit != DateTime.MinValue && from.LastWriteTimeUtc > updateLimit) // Copy to remote only the files of the latest version working at compile time
					{
						if (IsTextFiles(from))
							MyPendingFiles.Add(from);
					}
					else
					{
						if (IsTextFiles(from) && copy == CopyType.CopyFromRemote && MyPendingFiles.Contain(to))
						{
							WriteOutput("Merge " + @from.FullName + " => " + to.FullName);
							Merge(from, to);
						}
						else
						{
							WriteOutput("copy " + @from.FullName + " => " + to.FullName);
							File.Copy(@from.FullName, to.FullName, true);
						}
						if (copy == CopyType.CopyFromRemote && updateLimit != null)
							MyPendingFiles.Remove(to);
					}
				}
				foreach (var sourceDir in Directory.GetDirectories(sourcePath))
				{
					SyncGit(scan, sourceDir, targetPath, sourceRoot, updateLimit);
				}
			}

#if RELEASE
			}
			catch (System.Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
#endif
		}

		private static int LinePosition(List<line> listFrom, int line, List<line> listTo)
		{
			var insertTo = -1;
			var positions = new List<int>();
			int n = 0;
			var hash = listFrom[line].hash;
			for (int i = 0; i < listTo.Count; i++)
			{
				if (listTo[i].hash == hash)
				{
					positions.Add(i);
				}
			}
			while (positions.Count != 0)
			{
				if (positions.Count == 1)
					insertTo = positions[0];
				n++;
				if (line - n < 0 || positions.Count == 1)
					break;
				hash = listFrom[line - n].hash;
				foreach (var item in positions.ToArray())
				{
					var pos = item - n;
					if (pos < 0 || listTo[pos].hash != hash)
						positions.Remove(item);
				}
			}
			return insertTo;
		}

		private static void Merge(FileInfo from, FileInfo to)
		{
			var fromLines = File.ReadAllLines(from.FullName);
			var listFrom = new List<line>();
			fromLines.ToList().ForEach(x => listFrom.Add(new line() { text = x, hash = GetHashCode(x) }));

			var toLines = File.ReadAllLines(to.FullName);
			var listTo = new List<line>();
			toLines.ToList().ForEach(x => listTo.Add(new line() { text = x, hash = GetHashCode(x) }));

			for (var index = 0; index < listFrom.Count; index++)
			{
				var x = listFrom[index];
				int lineOfX = LinePosition(listFrom, index, listTo);
				if (lineOfX == -1)
				{
					var insert = LinePosition(listFrom, index - 1, listTo);
					if (insert != -1)
					{
						listTo.Insert(insert + 1, x);
					}
				}
			}

			var lines = new List<string>();
			listTo.ForEach(x => lines.Add(x.text));
			File.WriteAllLines(to.FullName, lines);
		}

		class line
		{
			public string text;
			public ulong hash;
		}

		public static ulong GetHashCode(string input)
		{
			input = input.Replace("\t", "");
			input = input.Replace(" ", "");
			var hashedValue = 3074457345618258791ul;
			foreach (var t in input)
			{
				hashedValue += t;
				hashedValue *= 3074457345618258799ul;
			}
			return hashedValue;
		}


		private static bool IsTextFiles(FileInfo file)
		{
			var extension = file.Extension;
			var textExtensions = new string[] { ".cs", ".txt", ".json", ".xml", ".csproj", ".vb", ".xaml", ".sln", ".cs", ".resx" };
			return textExtensions.Contains(extension);
		}

		private readonly PendingFiles MyPendingFiles = new PendingFiles();

		private class PendingFiles : List<string>
		{
			public void Add(FileInfo file)
			{
				Remove(file);
				this.Add(file.FullName.ToLower());
			}
			public void Remove(FileInfo file)
			{
				var filename = file.FullName.ToLower();
				var pending = this.Find(x => x == filename);
				if (pending != null)
				{
					this.Remove(pending);
				}
			}
			public bool Contain(FileInfo file)
			{
				return base.Contains(file.FullName.ToLower());
			}
		}


		private enum CopyType
		{
			None,
			CopyToRemote,
			CopyFromRemote,
		}

		private enum Scan
		{
			LocalDrive,
			RemoteDrive,
		}

		private static DateTime RoundDate(DateTime dt)
		{
			return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
		}

		/// <summary>
		/// Update only files with data lower than that of the last final compilation (files not yet compiled locally will not be sent to the remote repository to avoid having versions that do not work because with compilation errors)
		/// </summary>
		/// <param name="dir"></param>
		/// <returns>Date of the last working compilation of the project</returns>
		private DateTime? UpdateDateLimit(DirectoryInfo dir)
		{
			DirectoryInfo bin;
			if (Directory.Exists(Path.Combine(dir.FullName, "bin")))
			{
				bin = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
				var dlls = bin.GetFiles("*.dll", SearchOption.AllDirectories);
				if (dlls.Length > 0)
				{
					var result = DateTime.MinValue;
					foreach (var fileInfo in dlls)
					{
						if (fileInfo.LastAccessTimeUtc > result)
							result = fileInfo.LastAccessTimeUtc;
					}
					return result;
				}
				return DateTime.MinValue;
			}
			return null;
		}


	}
}
