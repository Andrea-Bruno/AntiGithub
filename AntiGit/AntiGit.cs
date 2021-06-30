//===============================================
// by Bruno Andrea
//===============================================

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
#if !MAC
using System.Runtime.InteropServices;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AntiGit
{
	public class AntiGit
	{
		public string Info = "The SOURCE directory is the one with the files to keep (your projects and your solutions must be here), in the TARGET directory the daily backups will be saved, the GIT directory is a remote directory accessible to all those who work on the same source files, for example, the git directory can correspond to a disk of network or to the address of a pen drive connected to the router, in this directory AntiGit will create a synchronized version of the source in real time.";
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


		public AntiGit(Action<string> alert = null)
		{

			_alert = alert;
			_sourceDir = getValue("source"); // ?? Directory.GetCurrentDirectory();
			_targetDir = getValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");

			if (string.IsNullOrEmpty(_targetDir))
				_targetDir = GetDefaultBackupPosition();
			_gitDir = getValue("git");

			LocalFiles = LoadMemoryFile(_sourceDir);
			RemoteFiles = LoadMemoryFile(_gitDir);

			setCurrentDateTime();
			backupTimer = new Timer((x) =>
			{
				if (LastBackup.Day != DateTime.UtcNow.Day)
				{
					StartBackup();
				}
			}, null, TimeSpan.Zero, new TimeSpan(1, 0, 0, 0));

			_backupOfTheChange = new Timer((x) =>
			{
				StartBackup(false);
			}, null, -1, -1);


			SyncGit();
		}

		public struct SystemTime
		{
			public short Year;
			public short Month;
			public short DayOfWeek;
			public short Day;
			public short Hour;
			public short Minute;
			public short Second;
			public short Milliseconds;
		};

		private void setCurrentDateTime()
		{
			if (GetAverageDateTimeFromWeb(out var currentDateTime))
			{
				if (Math.Abs((DateTime.UtcNow - currentDateTime).TotalSeconds) >= 3)
				{
					WriteOutput("The current computer time is incorrect: It is " + currentDateTime.ToLocalTime().ToString("HH mm ss") + " the computer clock is " + DateTime.Now.ToString("HH mm ss"));
					if (SetDateTime(currentDateTime))
					{
						WriteOutput("I fixed the system clock!");
					}
					else
					{
						WriteOutput("Please adjust the system clock!");
						RequestAdministrationMode();
					}
				}
			}
			else
			{
				WriteOutput("The current time could not be checked online");
			}
		}

#if MAC
    // write here apple macOS code to change System Date Time
		private bool SetDateTime(DateTime currentDateTime)
		{
			return	true;
		}

#else
		private bool SetDateTime(DateTime currentDateTime)
		{
			SystemTime st = new SystemTime
			{
				Year = (short)currentDateTime.Year,
				Month = (short)currentDateTime.Month,
				Day = (short)currentDateTime.Day,
				Hour = (short)currentDateTime.Hour,
				Minute = (short)currentDateTime.Minute,
				Second = (short)currentDateTime.Second,
				Milliseconds = (short)currentDateTime.Millisecond
			};
			return SetSystemTime(ref st);
		}
		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool SetSystemTime(ref SystemTime st);
#endif

		private static bool GetAverageDateTimeFromWeb(out DateTime dateTime)
		{
			var webs = new[] {
								"http://www.wikipedia.org/",
								"https://www.facebook.com/",
								"https://www.linuxfoundation.org/",
								"https://www.google.com/",
								"https://www.microsoft.com/",
						};
			var times = new List<DateTime>();
			for (var i = 1; i <= 1; i++)
				foreach (var web in webs)
				{
					var time = GetDateTimeFromWeb(web);
					if (time != null)
						times.Add((DateTime)time);
				}
			if (times.Count == 0)
			{
				dateTime = DateTime.UtcNow;
				return false;
			}
			times.Sort();

			var middle = times.Count / 2;
			dateTime = times[middle];
			return true;
		}
		private static DateTime? GetDateTimeFromWeb(string fromWebsite)
		{
			using (var client = new System.Net.Http.HttpClient())
			{
				client.Timeout = new TimeSpan(0, 0, 2);
				try
				{
					var result = client.GetAsync(fromWebsite, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).Result;
					if (result.Headers?.Date != null)
						return result.Headers?.Date.Value.UtcDateTime.AddMilliseconds(366); // for stats the time of website have a error of 366 ms; 					
				}
				catch
				{
					// ignored
				}
				return null;
			}
		}

		private readonly Timer _backupOfTheChange;

		public static void WriteOutput(string text)
		{
			Debug.WriteLine(text);
			//Console.WriteLine(text);
		}
		public void SyncGit()
		{
			if (!string.IsNullOrEmpty(_gitDir))
			{
				if (!Directory.Exists(_gitDir))
					WriteOutput("Git " + directoryNotFound);
				else if (!Directory.Exists(_sourceDir))
					WriteOutput("Source " + directoryNotFound);
				else
					FullSyncGit(_sourceDir, _gitDir);
			}
		}
		private const string directoryNotFound = "directory not found";

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

		public static readonly string[] ExcludeDir = { "bin", "obj", ".vs", "packages" };

		private string _sourceDir;
		public string SourceDir
		{
			get => _sourceDir;
			set
			{
				if (_sourceDir != value)
				{
					LocalFiles = null;
					_sourceDir = value;
					setValue("source", value);
					if (CheckSourceAndGit())
					{
						SyncGit();
					}
					//if (CheckSourceAndGit())
					//	setValue("source", value);
					//else
					//	_sourceDir = null;

				}
			}
		}
		private string _targetDir;

		public string TargetDir
		{
			get => _targetDir;
			set
			{
				if (_targetDir != value)
				{
					_targetDir = value;
					setValue("target", value);
					if (!string.IsNullOrEmpty(_targetDir) && !Directory.Exists(_targetDir))
						WriteOutput("Target " + directoryNotFound);
				}
			}

		}
		private string _gitDir;

		public string GitDir
		{
			get => _gitDir;
			set
			{
				if (_gitDir != value)
				{
					RemoteFiles = null;
					_gitDir = value;
					setValue("git", value);
					if (CheckSourceAndGit())
					{
						SyncGit();
					}
					//if (CheckSourceAndGit())
					//	setValue("git", value);
					//else
					//	_gitDir = null;
				}
			}
		}

		private bool CheckSourceAndGit()
		{
			if (!string.IsNullOrEmpty(_sourceDir) && !string.IsNullOrEmpty(_gitDir))
			{
				try
				{
					var source = new DirectoryInfo(_sourceDir);
					var git = new DirectoryInfo(_gitDir);
					var sourceCount = source.GetFileSystemInfos().Length;
					var gitCount = git.GetFileSystemInfos().Length;
					if (sourceCount != 0 && gitCount != 0)
					{
						//if (!IsSourceAndGitCompatible(source, git)) // This line has been removed to prevent a merge between different versions: If you use AntiGit it is good practice that the first programmer synchronizes their version on git, and everyone else creates their own version locally by cloning the Git one. This software does it automatically!
						//{
						Alert("Error: During the first setup Git and Source cannot contain files at the same time: If you want to synchronize this computer with the shared Git, then Git must contain the files and the Source directory must be empty. If you want to create a shared Git, then Git must be empty and Source must contain the files you want to share.");
						return false;
						//}
					}
				}
				catch (Exception e)
				{
					Alert(e.Message);
					return false;
				}
			}
			return true;
		}

		private static void Alert(string message)
		{
			Console.WriteLine(message);
			if (_alert != null)
				Task.Run(() => _alert?.Invoke(message));
		}
		private static Action<string> _alert;

		private Timer backupTimer;


		private readonly DirectoryInfo _appDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\AntiGit");

		private string getValue(string name)
		{
			var file = Path.Combine(_appDir.FullName, name + ".txt");
			return File.Exists(file) ? File.ReadAllText(file) : null;
		}

		private void setValue(string name, string value)
		{
			if (!_appDir.Exists)
				_appDir.Create();
			var file = Path.Combine(_appDir.FullName, name + ".txt");
			File.WriteAllText(file, value);
		}

		private DateTime LastBackup;
		private Thread backupThread;
		public bool BackupRunning = false;
		public void StartBackup(bool daily = true)
		{
			if (backupThread == null || !backupThread.IsAlive)
			{
				if (!string.IsNullOrEmpty(SourceDir) && !string.IsNullOrEmpty(TargetDir))
				{
					if (!new DirectoryInfo(SourceDir).Exists)
					{
						WriteOutput("Source not found!");
						return;
					}
					if (!new DirectoryInfo(TargetDir).Exists)
					{
						WriteOutput("Target not found!");
						return;
					}

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
			catch (System.Exception ex)
			{
				WriteOutput(ex.Message);
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
							WriteOutput("create directory  " + Param1);
							Directory.CreateDirectory(Param1);
						}
						break;
					case TypeOfOperation.LinkDirectory:
						if (!Directory.Exists(Param1))
						{
							WriteOutput("link directory  " + Param2 + " => " + Param1);
							CreateSymbolicLink(Param1, Param2, 1); // The parameter 1 = directory
							if (checkedIsAdmin == false)
							{
								checkedIsAdmin = true;
								var dir = new DirectoryInfo(Param1);
								if (!dir.Exists)
								{
									RequestAdministrationMode();
								}
							}
						}
						break;
					case TypeOfOperation.LinkFile:
						WriteOutput("link " + Param2 + " => " + Param1);
						//CreateHardLink(Param1, Param2, IntPtr.Zero); // These files from windows cannot be copied easily from one device to another, you need to use the Xcopy command
						CreateSymbolicLink(Param1, Param2, 0);// The parameter 0 = file
						break;
					case TypeOfOperation.CopyFile:
						File.Copy(Param1, Param2, true);
						WriteOutput("copy " + Param1 + " => " + Param2);
						break;
				}
			}
			private static bool checkedIsAdmin;
		}

		private static bool _requestAdmin;
		static void RequestAdministrationMode()
		{
			if (_requestAdmin) return;
			Alert("Error: The application must be run in administrator mode");
			_requestAdmin = true;
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

		private Thread gitTask;
		private int fullSyncCycle;


		private void FullSyncGit(string sourcePath, string gitPath)
		{
			gitTask = new Thread(() =>
			 {
				 fullSyncCycle = 0;
				 _stopSync = false;
				 while (!_stopSync)
				 {
					 if (!string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(gitPath))
					 {
						 if (!IsSourceAndGitCompatible(new DirectoryInfo(sourcePath), new DirectoryInfo(gitPath)))
						 {
							 Alert("Warning: Git sync cannot be started because the source and git directories contain different projects. Check the settings!");
							 return;
						 }

						 var oldestFile = DateTime.MinValue;
						 var hasSynchronized = false;
#if !DEBUG
							try
							{
#endif
						 var memoryFile = new StringCollection();
						 SyncGit(ref oldestFile, ref hasSynchronized, Scan.LocalDrive, sourcePath, gitPath, ref memoryFile);
						 DeleteRemovedFiles(Scan.LocalDrive, memoryFile, sourcePath, gitPath);
						 memoryFile = new StringCollection();
						 SyncGit(ref oldestFile, ref hasSynchronized, Scan.RemoteDrive, gitPath, sourcePath, ref memoryFile);
						 DeleteRemovedFiles(Scan.RemoteDrive, memoryFile, gitPath, sourcePath);
						 fullSyncCycle++;
#if !DEBUG
							}
							catch (Exception e)
							{
								// If the sync process fails, there will be an attempt in the next round
								Debug.Write(e.Message);
								Debugger.Break();
						}
#endif
						 if (hasSynchronized)
						 {
							 _backupOfTheChange.Change(60000, -1);
						 }
#if !DEBUG
						// If I don't see any recent changes, loosen the monitoring of the files so as not to stress the disk
						if ((DateTime.UtcNow - oldestFile).TotalMinutes > 30)
							Thread.Sleep(60000);
#endif
					 }
					 else
					 {
						 Thread.Sleep(60000);
					 }
				 }
				 _stopSync = true;
			 })
			{ Priority = ThreadPriority.Lowest };
			gitTask.Start();
		}
		private bool _stopSync = true;

		private static bool IsSourceAndGitCompatible(DirectoryInfo source, DirectoryInfo git)
		{

			if (source.Exists)
			{
				var subSource = git.GetDirectories("*.*", SearchOption.TopDirectoryOnly);
				var subSourceList = subSource.ToList().FindAll(x => (x.Attributes & FileAttributes.Hidden) == 0);
				if (subSourceList.Count == 0)
					return true;

				if (git.Exists)
				{
					var subGit = git.GetDirectories("*.*", SearchOption.TopDirectoryOnly);

					var subGitList = subGit.ToList().FindAll(x => (x.Attributes & FileAttributes.Hidden) == 0);


					if (subGitList.Count == 0)
						return true;
					var exists = 0;
					foreach (var sub in subGitList)
					{
						if (subSourceList.Find(x => x.Name == sub.Name) != null)
							exists++;
					}
					foreach (var sub in subSourceList)
					{
						if (subGitList.Find(x => x.Name == sub.Name) != null)
							exists++;
					}
					if (exists == 0)
						return false;
				}
			}
			return true;
		}


		public void StopSyncGit()
		{
			_stopSync = true;
		}

		public bool SyncGitRunning => !_stopSync;

		private StringCollection LocalFiles;
		private StringCollection RemoteFiles;
		void SyncGit(ref DateTime returnOldestFile, ref bool hasSynchronized, Scan scan, string sourcePath, string targetPath, ref StringCollection memoryFile, string sourceRoot = null, DateTime? compilationTime = null)
		{
			if (_stopSync)
				return;
			if (sourceRoot == null)
			{
				sourceRoot = sourcePath;
			}
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
					try
					{
						Directory.CreateDirectory(targetDirName);
					}
					catch (Exception e) { WriteOutput(e.Message); }
				}
				else
				{
					targetFiles = dirTarget.GetFiles();
				}

				DirectoryInfo localDir = scan == Scan.LocalDrive ? dir : dirTarget;


				if (compilationTime == null)
					compilationTime = UpdateDateLimit(localDir);// Copy to remote only the files of the latest version working at compile time

				memoryFile.Add(dir.FullName);
				foreach (var file in dir.GetFiles())
				{

					if (_stopSync) break;

					if (file.Attributes.HasFlag(FileAttributes.Hidden))
						continue;
					memoryFile.Add(file.FullName);

					var targetFile = Path.Combine(targetDirName, file.Name);
					var target = targetFiles?.ToList().Find(x => x.Name == targetFile) ?? new FileInfo(targetFile);
					var localFile = scan == Scan.LocalDrive ? file : target;

					if (file.Exists && file.LastWriteTimeUtc > returnOldestFile)
						returnOldestFile = file.LastWriteTimeUtc;
					if (target.Exists && target.LastWriteTimeUtc > returnOldestFile)
						returnOldestFile = target.LastWriteTimeUtc;

					FileInfo from = null;
					FileInfo to = null;
					var copy = CopyType.None;
					if (!target.Exists || RoundDate(file.LastWriteTimeUtc) > RoundDate(target.LastWriteTimeUtc))
					{

						copy = scan == Scan.LocalDrive ? CopyType.CopyToRemote : CopyType.CopyFromRemote;
						if (!target.Exists)
						{
							var memoryOfTarget = scan == Scan.LocalDrive ? RemoteFiles : LocalFiles;
							if (memoryOfTarget?.Contains(target.FullName) == true)
								continue; // It is not a new file but it is a deleted file!
						}
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


					if (copy == CopyType.CopyToRemote && compilationTime != null && compilationTime != DateTime.MinValue && from.LastWriteTimeUtc > compilationTime) // Copy to remote only the files of the latest version working at compile time
					{
						if (IsTextFiles(from))
							MyPendingFiles.Add(from);
					}
					else
					{
						try
						{
							// Check if this file exists in the visual studio backup, If yes, then a change is in progress on the local computer!
							var visualStudioRecoveryFile = (copy == CopyType.CopyFromRemote && compilationTime != null) ? FindVisualStudioRecoveryFile(to) : null; //NOTE: compilationTime != null is the file is a visual studio file
							if (copy == CopyType.CopyFromRemote && IsTextFiles(from) && (visualStudioRecoveryFile != null || MyPendingFiles.Contain(to)))
							{
								WriteOutput("Merge " + @from.FullName + " => " + to.FullName);
								Merge(from, to, visualStudioRecoveryFile);
								hasSynchronized = true;
							}
							else
							{
								WriteOutput("copy " + @from.FullName + " => " + to.FullName);
								try
								{
									File.Copy(from.FullName, to.FullName, true);
								}
								catch (Exception ex) { WriteOutput(ex.Message); }
#if DEBUG
								var verify = new FileInfo(to.FullName);
								if (RoundDate(verify.LastWriteTimeUtc) != RoundDate(from.LastWriteTimeUtc))
									Debugger.Break();
								//else
								//	Debugger.Break();
#endif
								hasSynchronized = true;
								if (compilationTime != null)
									MyPendingFiles.Remove(localFile);
							}
						}
						catch (Exception e)
						{
						}
					}
				}
				foreach (var sourceDir in Directory.GetDirectories(sourcePath))
				{
					SyncGit(ref returnOldestFile, ref hasSynchronized, scan, sourceDir, targetPath, ref memoryFile, sourceRoot, compilationTime);
				}
			}
		}

		private void DeleteRemovedFiles(Scan scan, StringCollection memoryFile, string sourcePath, string targetPath)
		{
			// Check if any files or directories have been deleted
			StringCollection oldMemoryFiles;

			if (scan == Scan.LocalDrive)
			{
				oldMemoryFiles = LocalFiles;
				if (MemoryFileIsChanged(LocalFiles, memoryFile))
				{
					SaveMemoryFile(memoryFile, sourcePath);
				}
				LocalFiles = memoryFile;
			}
			else
			{
				oldMemoryFiles = RemoteFiles;
				if (MemoryFileIsChanged(RemoteFiles, memoryFile))
				{
					SaveMemoryFile(memoryFile, sourcePath);
				}
				RemoteFiles = memoryFile;
			}

			if (oldMemoryFiles != null)
			{
				var removedFromSource = new List<string>();
				foreach (var oldMemoryFile in oldMemoryFiles)
				{
					if (!memoryFile.Contains(oldMemoryFile))
						removedFromSource.Add(oldMemoryFile);
				}

				if (fullSyncCycle > 1 && removedFromSource.Count > 1)
				{
					Alert("More than one file has been deleted, for security reasons we will not synchronize if more than one file is deleted!");
				}
				else
				{
					foreach (var item in removedFromSource)
					{
						var target = targetPath + item.Substring(sourcePath.Length);
						var fileInfo = new FileInfo(target);
						if (fileInfo.Exists)
						{
							try
							{
								fileInfo.Delete();
							}
							catch (Exception e)
							{
								WriteOutput(e.Message);
							}

						}
						else
						{
							var dirInfo = new FileInfo(target);
							if (dirInfo.Exists)
							{
								try
								{
									dirInfo.Delete();
								}
								catch (Exception e)
								{
									WriteOutput(e.Message);
								}
							}
						}
					}
				}
			}
		}



		private static bool MemoryFileIsChanged(StringCollection memory1, StringCollection memory2)
		{
			if (memory1 == null || memory2 == null)
				return true;
			if (memory1.Count != memory2.Count)
				return true;
			foreach (var item in memory1)
			{
				if (!memory2.Contains(item))
					return true;
			}
			foreach (var item in memory2)
			{
				if (!memory1.Contains(item))
					return true;
			}
			return false;
		}

		private void SaveMemoryFile(StringCollection memory, string path)
		{
			if (memory != null && !string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(_appDir.FullName, GetHashCode(path) + ".txt");
				File.WriteAllLines(file, memory.Cast<string>().ToArray());
			}
		}

		private void DeleteMemoryFile(string path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(_appDir.FullName, GetHashCode(path) + ".txt");
				var fileInfo = new FileInfo(file);
				if (fileInfo.Exists)
				{
					fileInfo.Delete();
				}
			}
		}

		private StringCollection LoadMemoryFile(string path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(_appDir.FullName, GetHashCode(path) + ".txt");
				if (new FileInfo(file).Exists)
				{
					var list = File.ReadAllLines(file);
					var collection = new StringCollection();
					collection.AddRange(list);
					return collection;
				}
			}
			return null;
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

		private static void Merge(FileInfo from, FileInfo to, FileInfo visualStudioBackupFile = null)
		{
			var local = visualStudioBackupFile ?? to;
			var listFrom = LoadTextFiles(from);
			var listTo = LoadTextFiles(local);

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
			try
			{
				File.WriteAllLines(to.FullName, lines);
			}
			catch (Exception e)
			{
				// If the attempt fails it will be updated to the next round!
			}
		}

		private static List<line> LoadTextFiles(FileInfo file)
		{
			var fileLines = File.ReadAllLines(file.FullName);
			var listFile = new List<line>();
			fileLines.ToList().ForEach(x => listFile.Add(new line() { text = x, hash = GetHashCode(x) }));
			return listFile;
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
			// You can add more file from this list: https://fileinfo.com/software/microsoft/visual_studio
			var textExtensions = new string[] { ".cs", ".txt", ".json", ".xml", ".csproj", ".vb", ".xaml", ".xamlx", "xhtml", ".sln", ".cs", ".resx", ".asm", ".c", ".cc", ".cpp", ".asp", ".asax", ".aspx", ".cshtml", ".htm", ".html", ".master", ".js", ".config" };
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
			// FAT / VFAT has a maximum resolution of 2s
			// NTFS has a maximum resolution of 100 ns

			var add = dt.Millisecond < 500 ? 0 : 1; // 0 - 499 round to lowers, 500 - 999 to upper
			return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second + add);
		}

		/// <summary>
		/// Update only files with data lower than that of the last final compilation (files not yet compiled locally will not be sent to the remote repository to avoid having versions that do not work because with compilation errors)
		/// </summary>
		/// <param name="dir"></param>
		/// <returns>Date of the last working compilation of the project</returns>
		private static DateTime? UpdateDateLimit(DirectoryInfo dir)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "bin")))
			{
				var bin = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
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

		private static List<FileInfo> VisualStudioBackupFile;
		private static DateTime lastUpdateVSBR = DateTime.MinValue;
		private static bool _showVBSuggest;
		private static FileInfo FindVisualStudioRecoveryFile(FileInfo original)
		{
			// For MacOs implementation see this: https://superuser.com/questions/1406367/where-does-visual-studio-code-store-unsaved-files-on-macos
#if !MAC
			if (IsTextFiles(original))
			{
				try
				{
					var vsDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\VisualStudio\BackupFiles\");
					if ((DateTime.UtcNow - lastUpdateVSBR).TotalSeconds > 30)
					{
						VisualStudioBackupFile = new List<FileInfo>(vsDir.GetFiles(@"*.*", SearchOption.AllDirectories));
						lastUpdateVSBR = DateTime.UtcNow;
					}

					if (VisualStudioBackupFile.Count != 0 && !_showVBSuggest)
					{
						_showVBSuggest = true;
						WriteOutput("Suggest: It is recommended setting Visual Studio Auto Recovery to 1 minute: tools->Options->Environment-AutoRecover");
					}

					//var candidates = vsDir.GetFiles("~AutoRecover." + original.Name + "*", SearchOption.AllDirectories);
					var candidates = VisualStudioBackupFile.FindAll(x => x.Name.StartsWith(@"~AutoRecover." + original.Name));
					var listOriginal = LoadTextFiles(original);
					foreach (var candidate in candidates)
					{
						if (candidate.LastWriteTimeUtc > original.LastWriteTimeUtc)
						{
							var listCandidate = LoadTextFiles(candidate);
							if (FilesAreSimilar(listOriginal, listCandidate))
							{
								return candidate;
							}
						}
					}
				}
				catch (Exception e)
				{
				}
			}
#endif
			return null;
		}

		private static bool FilesAreSimilar(List<line> list1, List<line> list2, double limit = 0.6)
		{

			var find = 0;
			var notFind = 0;
			foreach (var item in list1)
			{
				if (list2.Find(x => x.hash == item.hash) != null)
					find++;
				else
					notFind++;
			}

			foreach (var item in list2)
			{
				if (list1.Find(x => x.hash == item.hash) != null)
					find++;
				else
					notFind++;
			}
			var total = (find + notFind);
			return total == 0 ? false : (double)find / (double)total > limit;
		}
	}
}
