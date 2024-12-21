//===============================================
// by Bruno Andrea
//===============================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BackupLibrary;
using DataRedundancy;
using static BackupLibrary.Backup;

namespace AntiGitLibrary
{
    public class Context
    {
        public readonly string Info = DataRedundancy.Resources.Dictionary.Info;
        private readonly Backup Backup;
        private readonly Git Git;
        private readonly int IdContext;
        /// <summary>
        /// Context initializer that manages the underlying libraries for remote file redundancy and backups
        /// </summary>
        /// <param name="alert">Function that acts as an event for displaying alert logs that can be created by the underlying libraries. When something noteworthy happens in the underlying libraries, the called company is executed reporting a description of what happened</param>
        /// <param name="setCurrentDateTime">The file synchronization and backup functions, if running on a machine that has the incorrect date and time, can work anomalously, as the files may carry incorrect data about their creation. By setting this parameter to true, the date and time of the device will be updated in order to obtain correct operation of the application. Updating the date and time usually requires the application to run in administrator mode.</param>
        /// <param name="id">The id is used to create multiple instances of the class, this in the case of applications that need to virtualize multiple functions and mount multiple backup units.</param>
        /// <param name="sourceDir">It is the source directory to be backed up and the source of redundant remote file synchronization</param>
        public Context(Action<Exception> alert = null, bool setCurrentDateTime = true, int id = 0, string sourceDir = null)
        {
            IdContext = id;
            // WriteOutput(Info);
            Backup = new Backup();

            Git = new Git(Alert, BackupOfTheChange);
            _alert = alert;
            if (!string.IsNullOrEmpty(sourceDir))
                _sourceDir = sourceDir;
            else
                _sourceDir = GetValue("source"); // ?? Directory.GetCurrentDirectory();
            _targetDir = GetValue("target"); // ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "backup");
            if (_targetDir == null)
                _targetDir = GetDefaultBackupPosition();
#if DEBUG
            _targetDir = ""; // disabled backup in debug mode
#endif
            _gitDir = GetValue("git");
            if (setCurrentDateTime)
                SetCurrentDateTime();
            BackupTimer = new Timer(x => StartBackup(), null, (int)TimeSpan.FromMinutes(20).TotalMilliseconds, (int)TimeSpan.FromHours(1).TotalMilliseconds);

            //BackupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(10).TotalMilliseconds);
            //BackupTimer.Elapsed += (s, e) => StartBackup();
            //BackupTimer.AutoReset = false;
            //BackupTimer.Enabled = true;


            SyncGit();
            Monitoring = new PathMonitoring(_sourceDir, BackupOfTheChange);
        }
        private readonly PathMonitoring Monitoring;
        public void StopSyncGit() => Git.StopSyncGit();
        public bool SyncGitRunning => Git.SyncGitRunning;

        /// <summary>
        /// Start daily backup
        /// </summary>
        /// <param name="overwriteExisting">True to overwrite any existing backup made on the same day</param>
        /// <returns>Outcome of the operation</returns>
        public Outcome StartBackup(bool overwriteExisting = false)
        {
            //BackupTimer.Change((int)TimeSpan.FromDays(1).TotalMilliseconds, Timeout.Infinite); // Next backup after 1 day
            //BackupTimer.Interval = TimeSpan.FromDays(1).TotalMilliseconds; // Next backup after 1 day
            var outcome = Backup.Start(SourceDir, TargetDir, Backup.BackupType.Daily, overwriteExisting);//  
            LastDailyBackupResult = new Tuple<DateTime, Outcome>(DateTime.Now, outcome);
            return outcome;
        }

        /// <summary>
        /// Start daily backup with text result description
        /// </summary>
        /// <param name="result">textual outcome of the operation</param>
        /// <param name="overwriteExisting">True to overwrite any existing backup made on the same day</param>
        public void StartBackup2(out string result, bool overwriteExisting = false)
        {
            Outcome outcome = StartBackup(overwriteExisting);
            result = outcome.ToString();
        }

        // If not null, indicates the outcome of the last daily backup and when it was performed
        public Tuple<DateTime, Outcome> LastDailyBackupResult { get; private set; }

        public bool BackupRunning => Backup.BackupRunning != 0;
        public bool DailyBckupIsRunning => Backup.DailyBackupIsRunning;
        public bool OnChangeBckupIsRunning => Backup.OnChangeBackupIsRunning;

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
        /// <summary>
        /// Start a backup to save the changes made in the now.
        /// </summary>
        internal void BackupOfTheChange()
        {
            Backup.Start(SourceDir, TargetDir, Backup.BackupType.OnChange);
        }

        internal static void WriteOutput(string text)
        {
            //Debug.WriteLine(text);
            Console.WriteLine(text);
        }

        /// <summary>
        /// Function that when called starts data synchronization in real time (mirroring), between source directory and remote directory on the network path (also called git directory).
        /// </summary>
        public void SyncGit()
        {
            if (!string.IsNullOrEmpty(_gitDir))
            {
                string alert = null;
                if (!Directory.Exists(_gitDir))
                    Alert(new Exception("Git " + DataRedundancy.Resources.Dictionary.DirectoryNotFound));
                else if (!Directory.Exists(_sourceDir))
                    Alert(new Exception("Source " + DataRedundancy.Resources.Dictionary.DirectoryNotFound));
                else if (Git.CheckSourceAndGitDirectory(_sourceDir, _gitDir, out alert))
                {
                    Git.StartSync(_sourceDir, _gitDir);
                }

                Alert(alert == null ? null : new Exception(alert));
            }
        }

        private static string GetDefaultBackupPosition()
        {
            try
            {
                string result = null;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        result = Path.Combine(drive.Name, nameof(Backup));
#if DEBUG
                        result += "Test";
#endif
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

        /// <summary>
        /// Source directory, which you want to backup and have data redundancy remotely.
        /// </summary>
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

        /// <summary>
        /// The directory where backups are saved
        /// </summary>
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

        /// <summary>
        /// Network location for real-time data redundancy. The data of the source directory will also be replicated in this path, in which multiple users will be able to work indirectly and take advantage of the merge functions. For more information see the underlying library DataRedundancy
        /// </summary>
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
                        Alert(new Exception(alert));
                    var source = new DirectoryInfo(_sourceDir);
                    var git = new DirectoryInfo(_gitDir);
                    var sourceCount = source.GetFileSystemInfos().Length;
                    var gitCount = git.GetFileSystemInfos().Length;
                    if (sourceCount != 0 && gitCount != 0)
                    {
                        //if (!IsSourceAndGitCompatible(source, git)) // This line has been removed to prevent a merge between different versions: If you use Context it is good practice that the first programmer synchronizes their version on git, and everyone else creates their own version locally by cloning the Git one. This software does it automatically!
                        //{
                        Alert(new Exception(DataRedundancy.Resources.Dictionary.Error1));
                        return false;
                        //}
                    }
                }
                catch (Exception ex)
                {
                    Alert(ex);
                    return false;
                }
            }
            return true;
        }

        //internal static void Alert(string message, bool waitClick = false)
        internal static void Alert(Exception ex)
        {
            if (ex != null)
                Console.WriteLine(ex.Message);
            //if (waitClick)
            //	_alert?.Invoke(message);
            // else 
            if (_alert != null)
                Task.Run(() => _alert?.Invoke(ex));
        }
        private static Action<Exception> _alert;

        private readonly Timer BackupTimer; // It is used to keep a reference of the timer so as not to be removed from the garbage collector

        internal static readonly DirectoryInfo AppDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Context");

        private string GetValue(string name)
        {
            var file = Path.Combine(AppDir.FullName, IdContext == 0 ? "" : IdContext + name + ".txt");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        private void SetValue(string name, string value)
        {
#if !TEST
            if (!AppDir.Exists)
                AppDir.Create();
            var file = Path.Combine(AppDir.FullName, IdContext == 0 ? "" : IdContext + name + ".txt");
            File.WriteAllText(file, value);
#endif
        }

        private static bool _requestAdmin;
        internal static void RequestAdministrationMode(string descriprion)
        {
            if (_requestAdmin) return;
            Alert(new Exception(DataRedundancy.Resources.Dictionary.Error2 + Environment.NewLine + descriprion));
            _requestAdmin = true;
        }
    }
}
