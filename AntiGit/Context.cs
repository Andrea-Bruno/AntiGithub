//===============================================
// by Bruno Andrea
//===============================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if !MAC
using System.Runtime.InteropServices;
#endif

namespace AntiGitLibrary
{
	public class Context
	{
		public readonly string Info = Resources.Dictionary.Info;
		private readonly Backup Backup;
		private readonly Git Git;

		public Context(Action<string> alert = null)
		{	
			WriteOutput(Info);
			Backup = new Backup(this);
			Git = new Git(this);
			_alert = alert;
			_sourceDir = getValue("source"); // ?? Directory.GetCurrentDirectory();
			_targetDir = getValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");

			if (string.IsNullOrEmpty(_targetDir))
				_targetDir = GetDefaultBackupPosition();
			_gitDir = getValue("git");

			SetCurrentDateTime();
			BackupTimer = new Timer(x =>
			{
				if (new DateTime(Backup.LastBackup.Year, Backup.LastBackup.Month, Backup.LastBackup.Day) != new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day))
				{
					StartBackup();
				}
			}, null, TimeSpan.Zero, new TimeSpan(1, 0, 0, 0));

			_backupOfTheChange = new Timer(x =>
			{
				Backup.Start(SourceDir, TargetDir, false);
			}, null, -1, -1);


			SyncGit();
		}

		public void StopSyncGit() => Git.StopSyncGit();
		public bool SyncGitRunning => Git.SyncGitRunning;
		public void StartBackup() => Backup.Start(SourceDir, TargetDir);
		public bool BackupRunning => Backup.BackupRunning !=0;
		public static readonly string[] ExcludeDir = { "bin", "obj", ".vs", "packages" };


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
		}

		private void SetCurrentDateTime()
		{
			if (GetAverageDateTimeFromWeb(out var currentDateTime))
			{
				if (Math.Abs((DateTime.UtcNow - currentDateTime).TotalSeconds) >= 3)
				{
					WriteOutput(Resources.Dictionary.TimeIncorrect + " " + currentDateTime.ToLocalTime().ToString("HH mm ss") + " " + Resources.Dictionary.ComputerClockIs + " " + DateTime.Now.ToString("HH mm ss"));
					if (SetDateTime(currentDateTime))
					{
						WriteOutput(Resources.Dictionary.ClockFixed);
					}
					else
					{
						WriteOutput(Resources.Dictionary.PleaseAdjustClock);
						RequestAdministrationMode(Resources.Dictionary.Warning4);
					}
				}
			}
			else
			{
				WriteOutput(Resources.Dictionary.TimeNotCheckedOnline);
			}
		}

#if MAC
		// write here apple macOS code to change System Date Time
		private static bool SetDateTime(DateTime currentDateTime)
		{
			//https://bensmann.no/changing-system-date-from-terminal-os-x-recovery/
			Support.ExecuteMacCommand("date", "-u " + currentDateTime.ToString("MMddHHmmyy"));
			return true;
		}
#else
		private static bool SetDateTime(DateTime currentDateTime)
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
			using (var client = new HttpClient())
			{
				client.Timeout = new TimeSpan(0, 0, 2);
				try
				{
					var result = client.GetAsync(fromWebsite, HttpCompletionOption.ResponseHeadersRead).Result;
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

		internal void BackupOfTheChange()
		{
			_backupOfTheChange.Change(60000, -1);
		}
		private readonly Timer _backupOfTheChange;

		internal static void WriteOutput(string text)
		{
			//Debug.WriteLine(text);
			Console.WriteLine(text);
		}
		public void SyncGit()
		{
			if (!string.IsNullOrEmpty(_gitDir))
			{
				if (!Directory.Exists(_gitDir))
					Alert("Git " + Resources.Dictionary.DirectoryNotFound);
				else if (!Directory.Exists(_sourceDir))
					Alert("Source " + Resources.Dictionary.DirectoryNotFound);
				else if (!Support.IsLocalPath(_sourceDir))
					Alert(Resources.Dictionary.Error3);
				else
					Git.FullSyncGit(_sourceDir, _gitDir);
			}
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
			catch (Exception e)
			{
				WriteOutput(e.Message);
			}
			return null;
		}


		private string _sourceDir;
		public string SourceDir
		{
			get => _sourceDir;
			set
			{
				if (_sourceDir != value)
				{
					_sourceDir = value;
					SetValue("source", value);
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
					SetValue("target", value);
					if (!string.IsNullOrEmpty(_targetDir) && !Directory.Exists(_targetDir))
						WriteOutput("Target " + Resources.Dictionary.DirectoryNotFound);
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
					_gitDir = value;
					SetValue("git", value);
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
					if (!Support.IsLocalPath(source))
					{
						Alert(Resources.Dictionary.Error3);
						return false;
					}
					var git = new DirectoryInfo(_gitDir);
					var sourceCount = source.GetFileSystemInfos().Length;
					var gitCount = git.GetFileSystemInfos().Length;
					if (sourceCount != 0 && gitCount != 0)
					{
						//if (!IsSourceAndGitCompatible(source, git)) // This line has been removed to prevent a merge between different versions: If you use Context it is good practice that the first programmer synchronizes their version on git, and everyone else creates their own version locally by cloning the Git one. This software does it automatically!
						//{
						Alert(Resources.Dictionary.Error1);
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

		internal static void Alert(string message, bool waitClick = false)
		{
			Console.WriteLine(message);
			if (waitClick)
				_alert?.Invoke(message);
			else if (_alert != null)
				Task.Run(() => _alert?.Invoke(message));
		}
		private static Action<string> _alert;

		private readonly Timer BackupTimer; // It is used to keep a reference of the timer so as not to be removed from the garbage collector

		internal static readonly DirectoryInfo AppDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Context");

		private string getValue(string name)
		{
			var file = Path.Combine(AppDir.FullName, name + ".txt");
			return File.Exists(file) ? File.ReadAllText(file) : null;
		}

		private void SetValue(string name, string value)
		{
			if (!AppDir.Exists)
				AppDir.Create();
			var file = Path.Combine(AppDir.FullName, name + ".txt");
			File.WriteAllText(file, value);
		}

		private static bool _requestAdmin;
		internal static void RequestAdministrationMode(string descriprion)
		{
			if (_requestAdmin) return;
			Alert(Resources.Dictionary.Error2 + Environment.NewLine + descriprion);
			_requestAdmin = true;
		}
	}
}
