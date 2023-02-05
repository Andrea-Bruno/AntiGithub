using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DataRedundancy
{
    /// <summary>
    /// Redundant data sync unit. It allows the synchronization of two paths of one type, both local and on the network
    /// </summary>
    public class Git
    {
        /// <summary>
        /// Instance initializer
        /// </summary>
        /// <param name="alert">Event that is invoked when there are warnings</param>
        /// <param name="onDataChanged">Event that is called when a data change is intercepted</param>
        public Git(Action<string> alert, Action onDataChanged = null)
        {
            if (!AppDir.Exists)
                AppDir.Create();
            Alert = alert;
            OnDataChanged = onDataChanged;
#if DEBUG
            //var x1 = Merge.LoadTextFiles(new FileInfo(@"C:\test\text1.txt"));
            //Merge.ExecuteMerge(new FileInfo(@"C:\test\t1.txt"), new FileInfo(@"C:\test\t2.txt"), null, new FileInfo(@"C:\test\Result.txt"));
            //var x2 = Merge.LoadTextFiles(new FileInfo(@"C:\test\result.txt"));
            //Debugger.Break();
#endif
        }
        internal static readonly DirectoryInfo AppDir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + nameof(DataRedundancy));
        private readonly Action<string> Alert;
        private Thread gitTask;
        private int FullSyncCycle;
#if DEBUG
        const int SleepMs = 5000;
#else
        const int SleepMs = 60000;
#endif
        /// <summary>
        /// If possible it suggests a directory to store data redundantly, otherwise it returns null
        /// </summary>
        /// <returns></returns>
        public static string GetDefaultGitDirectory()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Reverse();

                var currentDrive = drives.FirstOrDefault(x => (x.RootDirectory.FullName.Length > 1 && AppDomain.CurrentDomain.BaseDirectory.StartsWith(x.RootDirectory.FullName)));

                string result = null;
                foreach (var drive in drives)
                {
                    if (drive.IsReady && drive.TotalSize >= 274877906944 && drive.RootDirectory.FullName != currentDrive.RootDirectory.FullName && drive.AvailableFreeSpace != currentDrive.AvailableFreeSpace) // >= 256 gb
                    {
                        result = Path.Combine(drive.Name, "redundancy");
                        break;
                    }
                }
                return result;
            }
            catch (Exception)
            {
            }
            return null;
        }

        public static bool CheckSourceAndGitDirectory(string sourceDir, string gitDir, out string alert)
        {
            try
            {
                var source = new DirectoryInfo(sourceDir);
                if (!Support.IsLocalPath(source))
                {
                    alert = Resources.Dictionary.Error3;
                    return false;
                }
                if (Support.IsLink(source))
                {
                    alert = (Resources.Dictionary.Error5 + ":" + Environment.NewLine + source.FullName);
                    return false;
                }
                var git = new DirectoryInfo(gitDir);
                if (Support.IsLink(git))
                {
                    alert = (Resources.Dictionary.Error5 + ":" + Environment.NewLine + git.FullName);
                    return false;
                }
                if (Support.IsFtpPath(git))
                {
                    alert = (Resources.Dictionary.Error4);
                    return false;
                }
            }
            catch (Exception ex)
            {
                alert = ex.Message;
                return false;
            }
            alert = null;
            return true;
        }
        public void StartSync(string sourcePath, string gitPath)
        {
            gitTask = new Thread(() =>
            {
                if (LocalFiles == null)
                    LocalFiles = LoadMemoryFile(sourcePath);
                if (RemoteFiles == null)
                    RemoteFiles = LoadMemoryFile(gitPath);
                FullSyncCycle = 0;
                _stopSync = false;

                StringCollection localFiles = null;
                StringCollection remoteFiles = null;

                while (!_stopSync)
                {
                    if (!string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(gitPath))
                    {
                        var oldestFile = DateTime.MinValue;
                        var someFilesHaveChanged = false;
#if RELEASE
                        try
                        {
#endif
                        if (!IsSourceAndGitCompatible(new DirectoryInfo(sourcePath), new DirectoryInfo(gitPath)))
                        {
                            Alert?.Invoke(Resources.Dictionary.Warning1);
                            return;
                        }
                        var toBeDeleted = new List<string>();
                        var skip = false;
                        localFiles = null;
                        SyncGit(ref oldestFile, ref someFilesHaveChanged, Scan.LocalDrive, sourcePath, gitPath, ref skip, ref toBeDeleted, ref localFiles);
                        SaveMemoryFile(Scan.RemoteDrive, remoteFiles, gitPath);
                        DeleteRemovedFiles(toBeDeleted, Scan.LocalDrive, sourcePath, gitPath);
                        toBeDeleted = new List<string>();
                        skip = false;
                        remoteFiles = null;
                        SyncGit(ref oldestFile, ref someFilesHaveChanged, Scan.RemoteDrive, gitPath, sourcePath, ref skip, ref toBeDeleted, ref remoteFiles);
                        SaveMemoryFile(Scan.LocalDrive, localFiles, sourcePath);
                        DeleteRemovedFiles(toBeDeleted, Scan.RemoteDrive, gitPath, sourcePath);
                        FullSyncCycle++;
#if RELEASE
                        }
                        catch (Exception e)
                        {
                            // If the sync process fails, there will be an attempt in the next round
                            if (LastErrorTime != DateTime.MinValue && (DateTime.UtcNow - LastErrorTime).TotalMinutes > 10) // In the span of 10 minutes, it shows only one alert to avoid warning loops
                            {
                                if (e.HResult == -2147024832) // Network no longer available
                                {
                                    Alert?.Invoke(e.Message);
                                }
                                else if (e.HResult == -2147024837) // An unexpected network error occurred
                                {
                                    Alert?.Invoke(e.Message);
                                }
                            }
                            Debug.Write(e.Message);
                            Debugger.Break();
                            LastErrorTime = DateTime.UtcNow;
                        }
#endif
                        if (someFilesHaveChanged)
                        {
                            OnDataChanged?.Invoke();
                        }
#if RELEASE
                        // If I don't see any recent changes, loosen the monitoring of the files so as not to stress the disk
                        if ((DateTime.UtcNow - oldestFile).TotalMinutes > 30)
                            Thread.Sleep(SleepMs);
#endif
                    }
                    else
                    {
                        Thread.Sleep(SleepMs);
                    }
                }
                _stopSync = true;
            })
            { Priority = ThreadPriority.Lowest };
            gitTask.Start();
        }
        private bool _stopSync = true;
#if RELEASE
        private DateTime LastErrorTime = DateTime.MinValue;
#endif
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

        /// <summary>
        /// Event that is called when a data change is intercepted
        /// </summary>
        private Action OnDataChanged;


        public void StopSyncGit()
        {
            _stopSync = true;
        }

        public bool SyncGitRunning => !_stopSync;

        private StringCollection LocalFiles;
        private StringCollection RemoteFiles;
        void SyncGit(ref DateTime returnOldestFile, ref bool someFilesHaveChanged, Scan scan, string sourcePath, string targetPath, ref bool skip, ref List<string> toBeDeleted, ref StringCollection newMemoryFile, string sourceRoot = null, DateTime? compilationTime = null)
        {
            if (_stopSync || skip)
                return;
            var root = false;
            if (sourceRoot == null)
            {
                root = true;
                newMemoryFile = new StringCollection();
                sourceRoot = sourcePath;
            }
            var relativeDirName = sourcePath.Substring(sourceRoot.Length);
            var targetDirName = targetPath + relativeDirName;
            var dir = new DirectoryInfo(sourcePath);
#if DEBUG
            //if (sourcePath.Equals(@"C:\test\a", StringComparison.InvariantCultureIgnoreCase))
            //    Debugger.Break();
            // code for testing
            //if (dir.FullName.EndsWith(@"\Banking", StringComparison.InvariantCultureIgnoreCase))
            //    Debugger.Break();
#endif
            if (scan == Scan.RemoteDrive && dir.Attributes.HasFlag(FileAttributes.Hidden))
                try
                {
                    Debugger.Break();
                    dir.Delete(true);
                }
                catch (Exception ex) { Debug.WriteLine(ex.Message); }
            if (Support.DirToExclude(dir.Name) || dir.Attributes.HasFlag(FileAttributes.Hidden))
            {
                if (!dir.Exists)
                    skip = true;
            }
            else
            {
                var dirTarget = new DirectoryInfo(targetDirName);
                DirectoryInfo localDir = scan == Scan.LocalDrive ? dir : dirTarget;
                if (compilationTime == null)
                    compilationTime = UpdateCompilationTime(localDir);// Copy to remote only the files of the latest version working at compile time
                var oldSourceMemoryFile = scan == Scan.LocalDrive ? LocalFiles : RemoteFiles;
                var oldTargetMemoryFile = scan == Scan.LocalDrive ? RemoteFiles : LocalFiles;
                FileInfo[] targetFiles = null;
                if (!dirTarget.Exists)
                {
                    // Check if the directory has been deleted: So we don't create a directory again
                    var isDeleted = oldSourceMemoryFile != null && oldSourceMemoryFile.Contains(dir.FullName) && oldTargetMemoryFile != null && oldTargetMemoryFile.Contains(dirTarget.FullName);
                    if (isDeleted)
                    {
                        toBeDeleted.Add(dir.FullName);
                    }
                    else if (compilationTime != null && dir.CreationTimeUtc < compilationTime)
                    {
                        try
                        {
                            Directory.CreateDirectory(targetDirName);
                        }
                        catch (Exception e) { Console.WriteLine(e.Message); }
                    }
                }
                else
                {
                    targetFiles = dirTarget.GetFiles();
                }

                newMemoryFile.Add(dir.FullName);
                foreach (var fileInfo in dir.GetFiles())
                {
                    var file = fileInfo;
#if DEBUG
                    // code for testing
                    //if (file.FullName.EndsWith(@"\ResxGenerator\Form1.cs", StringComparison.InvariantCultureIgnoreCase))
                    //	Debugger.Break();
#endif
                    if (_stopSync) break;
                    if (scan == Scan.RemoteDrive && file.Attributes.HasFlag(FileAttributes.Hidden))
                        try
                        {
                            file.Delete();
                        }
                        catch (Exception ex) { Debug.WriteLine(ex.Message); }


                    if (file.Attributes.HasFlag(FileAttributes.Hidden) || Support.FileToExclude(file.Name))
                        continue;
                    newMemoryFile.Add(file.FullName);
                    var targetFile = Path.Combine(targetDirName, file.Name);
                    var target = targetFiles?.ToList().Find(x => x.Name == targetFile) ?? new FileInfo(targetFile);
                    var localFile = scan == Scan.LocalDrive ? file : target;
                    file = Support.WaitFileUnlocked(file, Alert);
                    target = Support.WaitFileUnlocked(target, Alert);

                    if (file.Exists && file.LastWriteTimeUtc > returnOldestFile)
                        returnOldestFile = file.LastWriteTimeUtc;
                    if (target.Exists && target.LastWriteTimeUtc > returnOldestFile)
                        returnOldestFile = target.LastWriteTimeUtc;
                    bool isDeleted = !target.Exists && oldSourceMemoryFile != null && oldSourceMemoryFile.Contains(file.FullName) && oldTargetMemoryFile != null && oldTargetMemoryFile.Contains(targetFile);
                    if (isDeleted)
                    {
                        toBeDeleted.Add(file.FullName);
                    }
                    else
                    {
                        FileInfo from = null;
                        FileInfo to = null;
                        var copy = CopyType.None;
                        if (!target.Exists || file.LastWriteTimeUtc.AddSeconds(-2) > target.LastWriteTimeUtc)
                        {
                            copy = scan == Scan.LocalDrive ? CopyType.CopyToRemote : CopyType.CopyFromRemote;
                            from = file;
                            to = target;
                        }
                        else if (file.LastWriteTimeUtc.AddSeconds(2) < target.LastWriteTimeUtc)
                        {
                            copy = scan == Scan.LocalDrive ? CopyType.CopyFromRemote : CopyType.CopyToRemote;
                            from = target;
                            to = file;
                        }
                        if (copy == CopyType.None) continue;
                        var fromLastWriteTimeUtc = from.LastWriteTimeUtc < from.CreationTimeUtc ? from.CreationTimeUtc : from.LastWriteTimeUtc; // In windows there is a bug: new files have the property of the last writing, wrong
                        if (copy == CopyType.CopyToRemote && compilationTime != null && fromLastWriteTimeUtc > compilationTime) // Copy to remote only the files of the latest version working at compile time
                        {
                            if (IsTextFiles(from))
                                if (MyPendingSync.Add(from))
                                    someFilesHaveChanged = true;
                        }
                        else
                        {
                            try
                            {
                                someFilesHaveChanged = true;
                                // Check if this file exists in the visual studio backup, If yes, then a change is in progress on the local computer!
                                var visualStudioRecoveryFile = (copy == CopyType.CopyFromRemote && compilationTime != null) ? FindVisualStudioRecoveryFile(to) : null; //NOTE: compilationTime != null is the file is a visual studio file                            
                                if (copy == CopyType.CopyFromRemote && IsTextFiles(from) && (visualStudioRecoveryFile != null || MyPendingSync.Contains(to)))
                                {
                                    Merge.ExecuteMerge(from, to, Alert, visualStudioRecoveryFile);
                                }
                                else
                                {
                                    var attempt = 0;
                                    do
                                    {
                                        void showError(Exception e, string path)
                                        {
                                            attempt++;
                                            //if (e.HResult == -2147024864) // File already opened by another task
                                            if (attempt == 10)
                                            {
                                                if (e.HResult == -2147023779)
                                                    Alert?.Invoke(e.Message + " " + path + Environment.NewLine + Resources.Dictionary.Suggest2);
                                                else
                                                    Alert?.Invoke(e.Message + " " + path);
                                            }
                                            Thread.Sleep(1000);
                                            Debugger.Break();
                                        }
                                        try
                                        {
                                            if (!to.Directory.Exists)
                                                to.Directory.Create();
                                            try
                                            {
                                                File.Copy(from.FullName, to.FullName, true);
                                                attempt = 0;

                                            }
                                            catch (Exception e)
                                            {
                                                showError(e, to.FullName);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            showError(e, to.Directory.FullName);
                                        }

                                    } while (attempt != 0);
#if MAC
								    File.SetCreationTimeUtc(to.FullName, from.CreationTimeUtc); // bug: If I don't change this parameter the next command has no effect!
								    File.SetLastWriteTimeUtc(to.FullName, from.CreationTimeUtc);
#endif
#if DEBUG
                                    var verifyFron = new FileInfo(from.FullName);
                                    var verifyTo = new FileInfo(to.FullName);
                                    if (Math.Abs((verifyTo.LastWriteTime - verifyFron.LastWriteTime).TotalSeconds) > 1) // Check if date is different
                                        Debugger.Break();
#endif
                                    if (compilationTime != null)
                                        MyPendingSync.Remove(localFile);
                                }
                            }
                            catch (Exception e)
                            {
                                // If the attempt fails it will be updated to the next round!
                                if (Support.IsDiskFull(e))
                                    Alert?.Invoke(e.Message);
                            }
                        }

                    }
                }
                var subDirectories = Directory.GetDirectories(sourcePath);
                foreach (var sourceDir in subDirectories)
                {
                    SyncGit(ref returnOldestFile, ref someFilesHaveChanged, scan, sourceDir, targetPath, ref skip, ref toBeDeleted, ref newMemoryFile, sourceRoot, compilationTime);
                }
            }
            if (root)
            {
                //if (!_stopSync && !skip && FullSyncCycle > 0)
                if (_stopSync || skip && FullSyncCycle == 0)
                    newMemoryFile = null;
            }
        }
        private const int maxFileAllowToDeleteInOneTime = 3;
        private void DeleteRemovedFiles(List<string> removedFromSource, Scan scan, string sourcePath, string targetPath)
        {
            var isStartup = FullSyncCycle == 0;
            var totalFile = scan == Scan.LocalDrive ? LocalFiles == null ? 0 : LocalFiles.Count : RemoteFiles == null ? 0 : RemoteFiles.Count;
            string fileDeleted()
            {
                var deleted = "";
                for (var index = 0; index < removedFromSource.Count; index++)
                {
                    if (index == 4 && removedFromSource.Count > 4)
                    {
                        deleted += Environment.NewLine + "...";
                        break;
                    }
                    var item = removedFromSource[index];
                    deleted += Environment.NewLine + targetPath + item.Substring(sourcePath.Length);
                }
                return deleted;
            }
            if (removedFromSource.Count >= totalFile - 1) // prevents synchronizing a completely deleted route
                return;
            if (_stopSync)
                return;
            if (isStartup && removedFromSource.Count > 0 && scan == Scan.RemoteDrive)
            {
                Alert?.Invoke(Resources.Dictionary.Warning6 + fileDeleted());
            }
            else if (!isStartup && removedFromSource.Count > maxFileAllowToDeleteInOneTime)
            {
                Alert?.Invoke(Resources.Dictionary.Warning2 + fileDeleted());
            }
            else
            {
                bool isShow = false;
                foreach (var item in removedFromSource)
                {
                    //var target = targetPath + item.Substring(sourcePath.Length);
                    //var fileInfo = new FileInfo(target);
                    var fileInfo = new FileInfo(item);
                    if (fileInfo.Exists)
                    {
                        try
                        {
                            fileInfo.Delete();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                    else
                    {
                        //var dirInfo = new DirectoryInfo(target);
                        var dirInfo = new DirectoryInfo(item);
                        if (dirInfo.Exists)
                        {
                            try
                            {
                                if (isStartup && scan == Scan.LocalDrive || dirInfo.GetFiles().Count() + dirInfo.GetDirectories().Count() == 0)
                                {
                                    dirInfo.Delete(true);
                                }
                                else
                                {
                                    if (!isShow)
                                    {
                                        isShow = true;
                                        Alert?.Invoke(Resources.Dictionary.Warning5 + Environment.NewLine + item);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
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

        private void SaveMemoryFile(Scan scan, StringCollection memory, string path)
        {
            if (memory != null)
            {
                bool isChanged;
                if (scan == Scan.LocalDrive)
                {
                    isChanged = MemoryFileIsChanged(LocalFiles, memory);
                    LocalFiles = memory;
                }
                else
                {
                    isChanged = MemoryFileIsChanged(RemoteFiles, memory);
                    RemoteFiles = memory;
                }

                if (isChanged && !string.IsNullOrEmpty(path))
                {
                    var file = Path.Combine(AppDir.FullName, Merge.GetHashCode(path) + ".txt");
                    File.WriteAllLines(file, memory.Cast<string>().ToArray());
                }
            }
        }

        private StringCollection LoadMemoryFile(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var file = Path.Combine(AppDir.FullName, Merge.GetHashCode(path) + ".txt");
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


        private static bool IsTextFiles(FileInfo file)
        {
            var extension = file.Extension;
            // You can add more file from this list: https://fileinfo.com/software/microsoft/visual_studio
            var textExtensions = new[] { ".cs", ".txt", ".json", ".xml", ".csproj", ".vb", ".xaml", ".xamlx", "xhtml", ".sln", ".cs", ".resx", ".asm", ".c", ".cc", ".cpp", ".asp", ".asax", ".aspx", ".cshtml", ".htm", ".html", ".master", ".js", ".config" };
            return textExtensions.Contains(extension);
        }

        private readonly PendingFiles MyPendingSync = new PendingFiles();
        private class PendingFiles : StringCollection
        {
            public bool Add(FileSystemInfo file)
            {
                var filename = file.FullName.ToLower();
                var exists = Contains(filename);
                if (exists)
                    Remove(filename);
                return !exists;
            }
            public bool Remove(FileSystemInfo file)
            {
                var filename = file.FullName.ToLower();
                if (Contains(filename))
                {
                    Remove(filename);
                    return true;
                }
                return false;
            }
            public bool Contains(FileSystemInfo file)
            {
                return Contains(file.FullName.ToLower());
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

        /// <summary>
        /// Update only files with data lower than that of the last final compilation (files not yet compiled locally will not be sent to the remote repository to avoid having versions that do not work because with compilation errors)
        /// </summary>
        /// <param name="dir"></param>
        /// <returns>Date of the last working compilation of the project</returns>
        private static DateTime? UpdateCompilationTime(DirectoryInfo dir)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "bin")))
            {
                var bin = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
                var dlls = bin.GetFiles("*.dll", SearchOption.AllDirectories);
                var result = DateTime.MinValue;
                foreach (var fileInfo in dlls)
                {
                    if (fileInfo.LastAccessTimeUtc > result)
                        result = fileInfo.LastAccessTimeUtc;
                }
                return result;
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
                        Console.WriteLine(Resources.Dictionary.Suggest1);
                    }

                    //var candidates = vsDir.GetFiles("~AutoRecover." + original.Name + "*", SearchOption.AllDirectories);
                    var candidates = VisualStudioBackupFile.FindAll(x => x.Name.StartsWith(@"~AutoRecover." + original.Name));
                    var listOriginal = Merge.LoadTextFiles(original);
                    foreach (var candidate in candidates)
                    {
                        if (candidate.LastWriteTimeUtc > original.LastWriteTimeUtc)
                        {
                            var listCandidate = Merge.LoadTextFiles(candidate);
                            if (FilesAreSimilar(listOriginal, listCandidate))
                            {
                                return candidate;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
#endif
            return null;
        }

        private static bool FilesAreSimilar(List<Merge.Line> list1, List<Merge.Line> list2, double limit = 0.6)
        {

            var find = 0;
            var notFind = 0;
            foreach (var item in list1)
            {
                if (list2.Find(x => x.Hash == item.Hash) != null)
                    find++;
                else
                    notFind++;
            }

            foreach (var item in list2)
            {
                if (list1.Find(x => x.Hash == item.Hash) != null)
                    find++;
                else
                    notFind++;
            }
            var total = (find + notFind);
            return total != 0 && find / (double)total > limit;
        }

        private static readonly Dictionary<string, CompilerMonitor> CompiledList = new Dictionary<string, CompilerMonitor>();
        class CompilerMonitor
        {
            public DateTime Time;
            public bool IsNewCompiled;
            public static bool IsCompiled(Scan scan, string binPath, DateTime time)
            {

                if (!CompiledList.TryGetValue(binPath, out CompilerMonitor last))
                {
                    last = new CompilerMonitor() { Time = time };
                    CompiledList.Add(binPath, last);
                }
                if (scan == Scan.LocalDrive)
                    last.IsNewCompiled = false;
                last.IsNewCompiled = time != last.Time;
                return last.IsNewCompiled;
            }
        }

    }

}
