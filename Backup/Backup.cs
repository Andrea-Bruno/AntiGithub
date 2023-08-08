using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using static BackupLibrary.Support;

namespace BackupLibrary
{
    public class Backup
    {
        /// <summary>
        /// Initializer
        /// </summary>
        /// <param name="createSymbolicLink">Function that allows you to create symbolic links on disk (if omitted the terminal-based internal function will be used)</param>
        public Backup()
        {
        }
        public DateTime LastBackup { get; private set; }
        public bool DailyBckupIsRunning => BackupThreadDaily != null && BackupThreadDaily.IsAlive;
        public bool OnChangeBckupIsRunning => BackupThreadOnChange != null && BackupThreadOnChange.IsAlive;
        private Thread BackupThreadDaily;
        private Thread BackupThreadOnChange;
        internal bool StopBackup = false;
        public int BackupRunning { get; private set; }
        public delegate void AllertNotification(string description, bool important);
        static public event AllertNotification OnAlert;
        internal static void InvokeOnAlert(string description, bool important) => OnAlert?.Invoke(description, important);
        public enum BackupType
        {
            Daily,
            OnChange,
            OnChangeNoDelay
        }
        private Dictionary<string, System.Timers.Timer> Timers = new Dictionary<string, System.Timers.Timer>();
        public Outcome Start(string sourceDir, string targetDir, BackupType backupType = BackupType.Daily, bool overwriteDailyBackup = false)
        {
            if (!overwriteDailyBackup && backupType == BackupType.Daily && new DateTime(LastBackup.Year, LastBackup.Month, LastBackup.Day) == new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day))
                return Outcome.AlreadyDone;
            var BackupThread = backupType == BackupType.Daily? BackupThreadDaily : BackupThreadOnChange;
            if (BackupThread == null || !BackupThread.IsAlive) // Prevents the backup from running if it is already in progress
            {
                if (!string.IsNullOrEmpty(sourceDir) && !string.IsNullOrEmpty(targetDir))
                {
                    if (!new DirectoryInfo(sourceDir).Exists)
                    {
                        //Context.WriteOutput(Resources.Dictionary.SourceNotFound);
                        return Outcome.SourceNotFound;
                    }
                    if (!new DirectoryInfo(targetDir).Exists)
                    {
                        try
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    if (!new DirectoryInfo(targetDir).Exists)
                    {
                        //Context.WriteOutput(Resources.Dictionary.TargetNotFound);
                        return Outcome.TargetNotFound;
                    }
                    if (backupType == BackupType.OnChange)
                    {
                        var key = sourceDir + targetDir;
                        if (Timers.ContainsKey(key))
                            return Outcome.AlreadyScheduled;
#if DEBUG
                        var delay = 5000;
#else
                        var delay = 60000;
#endif
                        var timer = new System.Timers.Timer(delay)
                        {
                            AutoReset = false
                        };
                        timer.Elapsed += (o, e) =>
                        {
                            Start(sourceDir, targetDir, BackupType.OnChangeNoDelay);
                            Timers.Remove(key);
                        };                       
                        Timers.Add(key, timer);
                        timer.Start();
                        return Outcome.Scheduled;
                    }
                    BackupThread = new Thread(() =>
                    {
                        var today = DateTime.Now;
                        today.AddSeconds(-30); // prevent backups set for midnight from having a random date
                        LastBackup = today.ToUniversalTime();
                        BackupRunning++;
                        var targetPath = backupType == BackupType.Daily ? Path.Combine(targetDir, today.ToString("yyyy MM dd", CultureInfo.InvariantCulture)) : Path.Combine(targetDir, "today", today.ToString("HH mm", CultureInfo.InvariantCulture));
                        DirectoryInfo target;
                        if (backupType == BackupType.Daily)
                        {
                            target = new DirectoryInfo(targetDir);
                        }
                        else
                        {
                            var yesterday = new DirectoryInfo(Path.Combine(targetDir, "yesterday"));
                            if (yesterday.Exists && yesterday.CreationTime.DayOfYear != DateTime.Now.AddDays(-1).DayOfYear)
                            {
                                ForceDeleteDirectory(yesterday);
                            }
                            var todayInfo = new DirectoryInfo(Path.Combine(targetDir, "today"));
                            if (todayInfo.Exists && todayInfo.CreationTime.DayOfYear != DateTime.Now.DayOfYear)
                            {
                                yesterday = new DirectoryInfo(Path.Combine(targetDir, "yesterday"));
                                ForceDeleteDirectory(yesterday);
                                todayInfo.MoveTo(yesterday.FullName);
                            }
                            todayInfo = new DirectoryInfo(Path.Combine(targetDir, "today"));
                            if (!todayInfo.Exists)
                            {
                                todayInfo.Create();
                            }
                            target = todayInfo;
                        }
                        DirectoryInfo laseBackupDirectoty = LastBackupDirectory(target, targetPath, backupType);
                        var lastTargetPath = laseBackupDirectoty?.FullName;
                        ExecuteBackup(sourceDir, targetPath, lastTargetPath, new List<FileOperation>());
                        BackupRunning--;
                    })
                    {
                        Priority = ThreadPriority.Lowest,
                        Name = nameof(Backup) + backupType.ToString()
                    };
                    BackupThread.Start();
                    if (backupType == BackupType.Daily)
                        BackupThreadDaily = BackupThread;
                    else
                        BackupThreadOnChange = BackupThread;
                }
            }
            return Outcome.Successful;
        }

        public static DirectoryInfo LastBackupDirectory(DirectoryInfo target, string targetPath, BackupType backupType)
        {
            DirectoryInfo laseBackupDir = null;
            foreach (var dir in target.GetDirectories())
            {
                if (dir.FullName != targetPath && dir.Name.Split().Length == (backupType == BackupType.Daily ? 3 : 2))
                {
                    if (laseBackupDir == null || dir.CreationTimeUtc >= laseBackupDir.CreationTimeUtc)
                        laseBackupDir = dir;
                }
            }
            return laseBackupDir;
        }

        public enum Outcome { Successful, SourceNotFound, TargetNotFound, AlreadyDone, Scheduled, AlreadyScheduled };

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
                if (!DirToExclude(dir.Name) && (dir.Attributes & FileAttributes.Hidden) == 0)
                {
                    var relativeDirName = sourcePath.Substring(sourceRoot.Length);
                    var targetDirName = targetPath + relativeDirName;
                    spooler.Add(dirOperation);
                    var addToSpooler = new List<FileOperation>();
                    foreach (var fileInfo in dir.GetFiles())
                    {
                        var file = fileInfo;
                        file = WaitFileUnlocked(file);
                        if (!FileToExclude(file.Name) && (file.Attributes & FileAttributes.Hidden) == 0)
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
                if (IsDiskFull(e))
                    OnAlert?.Invoke(e.Message, true);
                //                    Context.Alert(e.Message, true);
                else
                    OnAlert?.Invoke(e.Message, false);
                //                Context.WriteOutput(e.Message);
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
                            //NOTE: Directories cannot have hardware links
                            var targetRelativeDir = GetRelativePath(Param1, Param2); // we use the relative position otherwise it gives an error if we rename the directory
                            CreateSymbolicLink(Param1, targetRelativeDir);
                            if (_checkedIsAdmin)
                            {
                                _checkedIsAdmin = true;
                                var dir = new DirectoryInfo(Param1);
                                if (!dir.Exists)
                                {
                                    throw new Exception("The application must be running in administrator mode in order to create symlinks");
                                }
                            }
                        }
                        break;
                    case TypeOfOperation.LinkFile:
                        var targetRelativeFile = GetRelativePath(Param1, Param2);
                        // CreateSymbolicLink(Param1, targetRelativeFile);
                        CreateHardLink(Param1, Param2); // These files from cannot be copied easily, you need to use the terminal command.  Relative path is not supported in Hard Link                                                                          
                        break;
                    case TypeOfOperation.CopyFile:
                        WaitFileUnlocked(Param1);
                        WaitFileUnlocked(Param2);
                        File.Copy(Param1, Param2, true);
                        break;
                }
            }
            private static bool _checkedIsAdmin;
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
            fileInfo1 = WaitFileUnlocked(fileInfo1);
            var fileInfo2 = new FileInfo(nameFile2);
            fileInfo2 = WaitFileUnlocked(fileInfo2);
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
