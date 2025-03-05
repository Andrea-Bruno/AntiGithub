using System;
using System.IO;

namespace BackupLibrary
{
    public class PathMonitoring
    {
        /// <summary>
        /// Daemon for monitoring changes in a directory
        /// </summary>
        /// <param name="path">Directories to monitor</param>
        /// <param name="onChange">Action to take when changes are revealed</param>
        public PathMonitoring(bool enabledAutoBackup, string path, Action onChange)
        {
            EnabledAutoBackup = enabledAutoBackup;
            OnChange = onChange;
            Path = path;
        }
        public string _Path;
        public string Path
        {
            get => _Path; set
            {
                if (pathWatcher != null)
                    pathWatcher.EnableRaisingEvents = false;
                _Path = value;
                if (_Path != null && Directory.Exists(_Path))
                    Watch();
                else
                    StopTryStartMonitoring();
            }
        }

        public bool EnabledAutoBackup { get; set; }

        private readonly Action OnChange;
        private FileSystemWatcher pathWatcher;
        private void Watch()
        {
            // If the directory to monitor is the path to a virtual disk, it will fail if the disk is not mounted, with a dimer then monitor when the file is mounted. 
            TryStartMonitoring = new System.Threading.Timer(StartMonitoring, null, 0, 60000);
        }
        private void StopTryStartMonitoring()
        {
            TryStartMonitoring?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            TryStartMonitoring = null;
        }
        private System.Threading.Timer TryStartMonitoring;
        private void StartMonitoring(object o)
        {
            try
            {
                pathWatcher = new FileSystemWatcher
                {
                    Path = _Path,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                    Filter = "*.*",
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };
                pathWatcher.Changed += (s, e) => RequestSynchronization(e);
                pathWatcher.Deleted += (s, e) => RequestSynchronization(e);
                StopTryStartMonitoring();
            }
            catch (Exception)
            {
            }
        }

        public bool Enabled { get { return EnabledAutoBackup && pathWatcher != null && pathWatcher.EnableRaisingEvents; } set { if (pathWatcher != null) pathWatcher.EnableRaisingEvents = value; } }
        public void RequestSynchronization(FileSystemEventArgs e)
        {
            if (EnabledAutoBackup)
            {
                FileInfo file = new FileInfo(System.IO.Path.Combine(e.FullPath, e.Name));
                if (file.Exists)
                {
                    if (file.Attributes.HasFlag(FileAttributes.Hidden))
                        return;
                    else if (file.Attributes.HasFlag(FileAttributes.Directory) && Support.DirToExclude(e.Name))
                        return;
                    else if (!file.Attributes.HasFlag(FileAttributes.Directory) && !file.Attributes.HasFlag(FileAttributes.Device) && Support.FileToExclude(e.Name))
                        return;
                }
                OnChange?.Invoke();
            }
        }
    }
}
