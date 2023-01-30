﻿//===============================================
// by Bruno Andrea
//===============================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BackupLibrary;
#if !MAC
using System.Runtime.InteropServices;
#endif
using DataRedundancy;
using static BackupLibrary.Support;

namespace AntiGitLibrary
{
    public class Context
    {
        public readonly string Info = DataRedundancy.Resources.Dictionary.Info;
        private readonly Backup Backup;
        private readonly Git Git;

        public Context(Action<string> alert = null, CreateLink createSymbolicLink = null, bool setCurrentDateTime = true)
        {
            WriteOutput(Info);
            Backup = new Backup(createSymbolicLink);

            Git = new Git(Alert, BackupOfTheChange);
            _alert = alert;
#if TEST
			_sourceDir = @"c:\test";
			_targetDir = "";
			_gitDir = @"\\share\G\test";
#else
            _sourceDir = getValue("source"); // ?? Directory.GetCurrentDirectory();
            _targetDir = getValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");
            if (string.IsNullOrEmpty(_targetDir))
                _targetDir = GetDefaultBackupPosition();
            _gitDir = getValue("git");
#endif
            if (setCurrentDateTime)
                SetCurrentDateTime();
            BackupTimer = new Timer(x =>
            {
                StartBackup();
            }, null, TimeSpan.Zero, new TimeSpan(1, 0, 0, 0));
            SyncGit();
            Monitoring = new PathMonitoring(_sourceDir, BackupOfTheChange);
        }
        private PathMonitoring Monitoring;
        public void StopSyncGit() => Git.StopSyncGit();
        public bool SyncGitRunning => Git.SyncGitRunning;
        public Backup.Outcome StartBackup(bool overwriteExisting = false)
        {
            return Backup.Start(SourceDir, TargetDir, Backup.BackupType.Daily, overwriteExisting);
        }

        public bool BackupRunning => Backup.BackupRunning != 0;

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
                    WriteOutput(DataRedundancy.Resources.Dictionary.TimeIncorrect + " " + currentDateTime.ToLocalTime().ToString("HH mm ss") + " " + DataRedundancy.Resources.Dictionary.ComputerClockIs + " " + DateTime.Now.ToString("HH mm ss"));
                    if (SetDateTime(currentDateTime))
                    {
                        WriteOutput(DataRedundancy.Resources.Dictionary.ClockFixed);
                    }
                    else
                    {
                        WriteOutput(DataRedundancy.Resources.Dictionary.PleaseAdjustClock);
                        RequestAdministrationMode(DataRedundancy.Resources.Dictionary.Warning4);
                    }
                }
            }
            else
            {
                WriteOutput(DataRedundancy.Resources.Dictionary.TimeNotCheckedOnline);
            }
        }

        // write here apple macOS code to change System Date Time
        private static bool SetDateTime(DateTime currentDateTime)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    //https://bensmann.no/changing-system-date-from-terminal-os-x-recovery/
                    //	Support.ExecuteMacCommand("date", "-u " + currentDateTime.ToString("MMddHHmmyy"));
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
            }
            catch (Exception)
            {
            }
            return false;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetSystemTime(ref SystemTime st);

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
            Backup.Start(SourceDir, TargetDir, Backup.BackupType.OnChange);
        }

        private readonly System.Timers.Timer _backupOfTheChange;

        internal static void WriteOutput(string text)
        {
            //Debug.WriteLine(text);
            Console.WriteLine(text);
        }
        public void SyncGit()
        {
            if (!string.IsNullOrEmpty(_gitDir))
            {
                string alert = null;
                if (!Directory.Exists(_gitDir))
                    Alert("Git " + DataRedundancy.Resources.Dictionary.DirectoryNotFound);
                else if (!Directory.Exists(_sourceDir))
                    Alert("Source " + DataRedundancy.Resources.Dictionary.DirectoryNotFound);
                else if (Git.CheckSourceAndGitDirectory(_sourceDir, _gitDir, out alert))
                {
                    Git.StartSync(_sourceDir, _gitDir);
                }
                if (alert != null)
                {
                    Alert(alert);
                }
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
                TrimPathName(ref value);
                if (_sourceDir != value)
                {
                    _sourceDir = value;
                    SetValue("source", value);
                    if (CheckSourceAndGit())
                    {
                        SyncGit();
                    }
                    Monitoring.Path = _sourceDir;
                }
            }
        }
        private string _targetDir;

        public string TargetDir
        {
            get => _targetDir;
            set
            {
                TrimPathName(ref value);
                if (_targetDir != value)
                {
                    _targetDir = value;
                    SetValue("target", value);
                    if (!string.IsNullOrEmpty(_targetDir) && !Directory.Exists(_targetDir))
                        WriteOutput("Target " + DataRedundancy.Resources.Dictionary.DirectoryNotFound);
                }
            }

        }
        private string _gitDir;

        public string GitDir
        {
            get => _gitDir;
            set
            {
                TrimPathName(ref value);
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

        private void TrimPathName(ref string path)
        {
            char[] chrs = { '\\', '/' };
            path = path.Trim();
            path = path.TrimEnd(chrs);
        }

        private bool CheckSourceAndGit()
        {
            if (!string.IsNullOrEmpty(_sourceDir) && !string.IsNullOrEmpty(_gitDir))
            {
                try
                {
                    Git.CheckSourceAndGitDirectory(_sourceDir, _gitDir, out string alert);
                    if (alert != null)
                        Alert(alert);
                    var source = new DirectoryInfo(_sourceDir);
                    var git = new DirectoryInfo(_gitDir);
                    var sourceCount = source.GetFileSystemInfos().Length;
                    var gitCount = git.GetFileSystemInfos().Length;
                    if (sourceCount != 0 && gitCount != 0)
                    {
                        //if (!IsSourceAndGitCompatible(source, git)) // This line has been removed to prevent a merge between different versions: If you use Context it is good practice that the first programmer synchronizes their version on git, and everyone else creates their own version locally by cloning the Git one. This software does it automatically!
                        //{
                        Alert(DataRedundancy.Resources.Dictionary.Error1);
                        return false;
                        //}
                    }
                }
                catch (Exception ex)
                {
                    Alert(ex.Message);
                    return false;
                }
            }
            return true;
        }

        //internal static void Alert(string message, bool waitClick = false)
        internal static void Alert(string message)
        {
            Console.WriteLine(message);
            //if (waitClick)
            //	_alert?.Invoke(message);
            // else 
            if (_alert != null)
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
#if !TEST
            if (!AppDir.Exists)
                AppDir.Create();
            var file = Path.Combine(AppDir.FullName, name + ".txt");
            File.WriteAllText(file, value);
#endif
        }

        private static bool _requestAdmin;
        internal static void RequestAdministrationMode(string descriprion)
        {
            if (_requestAdmin) return;
            Alert(DataRedundancy.Resources.Dictionary.Error2 + Environment.NewLine + descriprion);
            _requestAdmin = true;
        }
    }
}
