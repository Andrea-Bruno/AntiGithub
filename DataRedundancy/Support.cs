using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DataRedundancy
{
    internal static class Support
    {
        private const int ErrorSharingViolation = unchecked((int)0x80070020);
        private const int HrErrorHandleDiskFull = unchecked((int)0x80070027);
        private const int HrErrorDiskFull = unchecked((int)0x80070070);
        public static bool IsDiskFull(Exception ex)
        {
            return ex.HResult == HrErrorHandleDiskFull || ex.HResult == HrErrorDiskFull;
        }
        public static readonly string[] ExcludeFiles = { };
        public static readonly string[] ExcludeDir = { "bin", "obj", ".vs", "packages", "apppackages" };

        private static bool IsFileLocked(FileInfo file)
        {
            if (file.Exists && file.Length == 0)
            {
                try
                {
                    using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        stream.Close();
                    }
                }
                catch (IOException ex)
                {
                    //the file is unavailable because it is:
                    //still being written to
                    //or being processed by another thread
                    if (ex.HResult == ErrorSharingViolation)
                        return true;
                }
            }
            //file is not locked
            return false;
        }

        public static FileInfo WaitFileUnlocked(FileInfo file, Action<Exception> alert)
        {
            int attempt = 0;
            bool isLocked;
            do
            {
                isLocked = IsFileLocked(file);
                if (isLocked)
                {
                    attempt++;
                    if (attempt == 320) // 5 min - Note: It may be that a copy is in progress, or other normal operations use the file, before giving a warning we wait for the normal operations to be finished
                    {
                        alert?.Invoke(new Exception("File Locked " + file.FullName));
                    }
                    System.Threading.Thread.Sleep(1000);
                    file = new FileInfo(file.FullName);
                }
            } while (isLocked);
            return file;
        }

        public static bool DirToExclude(string dirName)
        {
            return dirName.StartsWith(".") || ExcludeDir.Contains(dirName.ToLower());
        }
        public static bool FileToExclude(string fileName)
        {
            return fileName.StartsWith(".") || ExcludeFiles.Contains(fileName.ToLower());
        }


        public static bool IsLink(DirectoryInfo path)
        {
            return path.Exists && path.GetFileSystemInfos().ToList().Find(x => x.Name == "target.lnk") != null;
        }

        public static bool IsLink(string pathName)
        {
            return IsLink(new DirectoryInfo(pathName));
        }

        public static bool IsFtpPath(DirectoryInfo path)
        {
            return path.FullName.StartsWith("ftp:", StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsFtpPath(string pathName)
        {
            return IsFtpPath(new DirectoryInfo(pathName));
        }

        public static bool IsLocalPath(DirectoryInfo path)
        {
            var drive = new DriveInfo(path.Root.FullName);
            return drive.DriveType != DriveType.Network;
        }

        public static bool IsLocalPath(string pathName)
        {
            return IsLocalPath(new DirectoryInfo(pathName));
        }

        /// <summary>
        /// https://stackoverflow.com/questions/611921/how-do-i-delete-a-directory-with-read-only-files-in-c
        /// </summary>
        /// <param name="path">Directory to delete</param>
        internal static void ForceDeleteDirectory(DirectoryInfo directory)
        {
            if (directory.Exists)
            {
                directory.Attributes = FileAttributes.Normal;
                foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
                    info.Attributes = FileAttributes.Normal;
                directory.Delete(true);
            }
        }
    }
}
