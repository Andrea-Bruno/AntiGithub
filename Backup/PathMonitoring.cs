using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BackupLibrary
{
    public class PathMonitoring
    {
        /// <summary>
        /// Demone per il monitoraggio delle modifiche in una directory
        /// </summary>
        /// <param name="path">Directories to monitor</param>
        /// <param name="onChange">Action to take when changes are revealed</param>
        public PathMonitoring(string path, Action onChange)
        {
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
                    Watch(_Path);
            }
        }
        private readonly Action OnChange;
        private FileSystemWatcher pathWatcher;
        private void Watch(string path)
        {
            pathWatcher = new FileSystemWatcher
            {
                Path = path,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                Filter = "*.*",
                EnableRaisingEvents = true,
                IncludeSubdirectories = true
            };
            pathWatcher.Changed += (s, e) => RequestSynchronization(e);
            pathWatcher.Deleted += (s, e) => RequestSynchronization(e);
        }
        public bool Enabled { get { return pathWatcher != null && pathWatcher.EnableRaisingEvents; } set { if (pathWatcher != null) pathWatcher.EnableRaisingEvents = value; } }
        public void RequestSynchronization(FileSystemEventArgs e)
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
