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


		public AntiGit( Action<string> alert = null)
		{
			Alert = alert;
			_sourceDir = getValue("source"); // ?? Directory.GetCurrentDirectory();
			_targetDir = getValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");

			if (string.IsNullOrEmpty(_targetDir))
				_targetDir = GetDefaultBackupPosition();
			_gitDir = getValue("git");
			setCurrentDateTime();
			backupTimer = new Timer((x) =>
			{
				if (LastBackup.Day != DateTime.UtcNow.Day)
				{
					StartBackup();
				}
			}, null, TimeSpan.Zero, new TimeSpan(1, 0, 0, 0));

			backupOfTheChange = new Timer((x) =>
			{
				StartBackup(false);
			}, null, -1, -1);


			//StartBackup();
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
					WriteOutput("The current computer time is incorrect: It is " + currentDateTime.ToLocalTime().ToString("hh mm ss") + " the computer clock is " + DateTime.Now.ToString("hh mm ss"));
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
			SystemTime st = new SystemTime();
			st.Year = (short)currentDateTime.Year;
			st.Month = (short)currentDateTime.Month;
			st.Day = (short)currentDateTime.Day;
			st.Hour = (short)currentDateTime.Hour;
			st.Minute = (short)currentDateTime.Minute;
			st.Second = (short)currentDateTime.Second;
			st.Milliseconds = (short)currentDateTime.Millisecond;
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
						times.Add((DateTime) time);
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

		private Timer backupOfTheChange;

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
			set
			{
				if (_sourceDir != value)
				{
					_sourceDir = value;
					CheckSourceAndGit();
					setValue("source", value);
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
			set { _targetDir = value; setValue("target", value); }

		}
		private string _gitDir;

		public string GitDir
		{
			get => _gitDir;
			set
			{
				if (_gitDir != value)
				{
					_gitDir = value;
					CheckSourceAndGit();
					setValue("git", value);
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
						Alert(
							"Error: During the first setup Git and Source cannot contain files at the same time: If you want to synchronize this computer with the shared Git, then Git must contain the files and the Source directory must be empty. If you want to create a shared Git, then Git must be empty and Source must contain the files you want to share.");
						return false;
					}
				}
				catch (Exception e)
				{
					Alert(e.Message);
				}
			}

			return true;
		}

		static private void _alert(string message)
		{
			Console.WriteLine(message);
			Alert?.Invoke(message);
		}
		private static Action<string> Alert;

		private System.Threading.Timer backupTimer;

		private string getValue(string name)
		{
			var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".txt");
			return File.Exists(file) ? File.ReadAllText(file) : null;
		}

		private void setValue(string name, string value)
		{
			var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".txt");
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
					backupThread = new Thread(() =>
						{
							LastBackup = DateTime.UtcNow;
							BackupRunning = true;
							var today = DateTime.Now;
							var targetPath = daily ? Path.Combine(TargetDir, today.ToString("dd MM yyyy", CultureInfo.InvariantCulture)) : Path.Combine(TargetDir, "today", today.ToString("hh mm", CultureInfo.InvariantCulture));
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
							Spooler.ForEach(operation => { operation.execute(); });
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
									RequestAdministrationMode();
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

		private static bool _requestAdmin;
		static void RequestAdministrationMode()
		{
			if (_requestAdmin) return;
			_alert("Error: The application must be run in administrator mode");
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
						var oldestFile = DateTime.MinValue;
						bool hasSynchronized = false;
						SyncGit(ref oldestFile, ref hasSynchronized, Scan.LocalDrive, sourcePath, targetPath);
						SyncGit(ref oldestFile, ref hasSynchronized, Scan.RemoteDrive, targetPath, sourcePath);
						if (hasSynchronized)
						{
							backupOfTheChange.Change(60000, -1);
						}
						if ((DateTime.UtcNow - oldestFile).TotalMinutes > 30)
							Thread.Sleep(60000);
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

		public void StopSyncGit()
		{
			_stopSync = true;
		}

		public bool SyncGitRunning => !_stopSync;

		void SyncGit(ref DateTime returnOldestFile, ref bool hasSynchronized, Scan scan, string sourcePath, string targetPath, string sourceRoot = null, DateTime? compilationTime = null)
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
				if (compilationTime == null)
					compilationTime = UpdateDateLimit(localDir);// Copy to remote only the files of the latest version working at compile time

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
							var visualStudioBackupFile = (copy == CopyType.CopyFromRemote && compilationTime != null) ? FindVisualStudioBackupFile(to) : null; //NOTE: compilationTime != null is the file is a visual studio file
							if (copy == CopyType.CopyFromRemote && IsTextFiles(from) && (visualStudioBackupFile != null || MyPendingFiles.Contain(to)))
							{
								WriteOutput("Merge " + @from.FullName + " => " + to.FullName);
								Merge(from, to, visualStudioBackupFile);
								hasSynchronized = true;
							}
							else
							{
								WriteOutput("copy " + @from.FullName + " => " + to.FullName);
								File.Copy(from.FullName, to.FullName, true);
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
					SyncGit(ref returnOldestFile, ref hasSynchronized, scan, sourceDir, targetPath, sourceRoot, compilationTime);
				}
			}

#if RELEASE
			}
			catch (System.Exception ex)
			{
				Alert(ex.Message);
			}
#endif
			return;
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
			File.WriteAllLines(to.FullName, lines);
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

		private static List<FileInfo> VisualStudioBackupFile;
		private static DateTime lastUpdateVSBF = DateTime.MinValue;
		private static FileInfo FindVisualStudioBackupFile(FileInfo original)
		{
			// For MacOs implementation see this: https://superuser.com/questions/1406367/where-does-visual-studio-code-store-unsaved-files-on-macos
#if !MAC
			if (IsTextFiles(original))
			{
				try
				{
					var vsDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\VisualStudio\BackupFiles\");
					if ((DateTime.UtcNow - lastUpdateVSBF).TotalSeconds > 30)
					{
						VisualStudioBackupFile = new List<FileInfo>(vsDir.GetFiles(@"*.*", SearchOption.AllDirectories));
						lastUpdateVSBF = DateTime.UtcNow;
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
