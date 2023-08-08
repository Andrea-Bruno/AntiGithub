using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static BackupLibrary.Backup;

namespace BackupLibrary
{
    public static class Support
    {
        private const int HrErrorHandleDiskFull = unchecked((int)0x80070027);
        private const int HrErrorDiskFull = unchecked((int)0x80070070);
        private const int ErrorSharingViolation = unchecked((int)0x80070020);

        //internal static void ExecuteCommand(string command, string arguments)
        //{
        //    var proc = new Process
        //    {
        //        StartInfo = {
        //            FileName =  command,
        //            Arguments = arguments,
        //            RedirectStandardInput = true,
        //            UseShellExecute = false,
        //            CreateNoWindow = true,
        //        }
        //    };
        //    try
        //    {
        //        proc.Start();
        //        proc.WaitForExit();
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //    }
        //}

        /// <summary>
        /// Run a bash terminal command (linux/mac) or cmd on windows.
        /// List of bash command https://www.logicweb.com/knowledgebase/linux/linux-bash-commands-a-z/
        /// </summary>
        /// <returns>The output of the operation</returns>
        public static string ExecuteSystemCommand(string command, Action<string> outputLine = null)
        {
            var startInfo = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "cmd";
                startInfo.Arguments = "/c \"" + command + "\"";
            }
            else
            {
                var escapedArgs = command.Replace("\"", "\\\"");
                startInfo.FileName = "/bin/bash";
                startInfo.Arguments = $"-c \"{escapedArgs}\"";
            }

            var process = new Process
            {
                StartInfo = startInfo
            };
            process.Start();

            string result = null;
            while (!process.StandardOutput.EndOfStream)
            {
                string line = process.StandardOutput.ReadLine();
                outputLine?.Invoke(line);
                if (result != null)
                {
                    result += Environment.NewLine;
                }
                result += line;
            }
            process.WaitForExit();
            return result;
        }

        public static bool IsDiskFull(Exception ex)
        {
            return ex.HResult == HrErrorHandleDiskFull
                         || ex.HResult == HrErrorDiskFull;
        }

        /// <summary>
        /// Cross-platform function to create a hard link
        /// </summary>
        /// <param name="linkFileName">File name</param>
        /// <param name="targetFileName">Existing file name (existing)</param>
        /// <returns>Outcome of the operation</returns>
        public delegate FileSystemInfo CreateLink(string linkFileName, string targetFileName);

        /// <summary>
        /// Cross-platform function to create a symbolic link
        /// </summary>
        /// <param name="linkFileName">File name</param>
        /// <param name="targetFileName">Existing file name (existing)</param>
        static public CreateLink CreateSymbolicLink => _CreateSymbolicLink;

        /// <summary>
        /// Cross-platform function to create a hard link
        /// </summary>
        /// <param name="linkFileName">File name</param>
        /// <param name="targetFileName">Existing file name (existing)</param>

        static public CreateLink CreateHardLink => _CreateHardLink;

        static private FileSystemInfo _CreateSymbolicLink(string linkFileName, string targetFileName)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    ExecuteSystemCommand("mklink /d \"" + linkFileName + "\" \"" + targetFileName + "\"");
                }
                else
                {
                    ExecuteSystemCommand("ln -s \"" + targetFileName + "\" \"" + linkFileName + "\"");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return new FileInfo(linkFileName);
        }

        static private FileSystemInfo _CreateHardLink(string linkFileName, string targetFileName)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    ExecuteSystemCommand("mklink /h \"" + linkFileName + "\" \"" + targetFileName + "\"");
                }
                else
                {
                    ExecuteSystemCommand("ln \"" + targetFileName + "\" \"" + linkFileName + "\"");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return new FileInfo(linkFileName);
        }

        private static DateTime RoundDate(DateTime dt)
        {
            // FAT / VFAT has a maximum resolution of 2s
            // NTFS has a maximum resolution of 100 ms
            var add = 0;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                add = dt.Millisecond < 500 ? 0 : 1; // 0 - 499 round to lowers, 500 - 999 to upper
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second + add);
        }
        public static readonly string[] ExcludeFiles = { };
        public static readonly string[] ExcludeDir = { "bin", "obj", ".vs", "packages", "apppackages" };
        public static bool DirToExclude(string dirName)
        {
            return dirName.StartsWith(".") || dirName.StartsWith("_") || ExcludeDir.Contains(dirName.ToLower());
        }
        public static bool FileToExclude(string fileName)
        {
            return fileName.StartsWith(".") || fileName.StartsWith("_") || ExcludeFiles.Contains(fileName.ToLower());
        }

        /// <summary>
        /// Creates a relative path from one file or folder to another.
        /// </summary>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path or <c>toPath</c> if the paths are not related.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal static string GetRelativePath(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (string.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");
            var fromUri = new Uri(fromPath);
            var toUri = new Uri(toPath);
            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }
            return relativePath;
        }
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

        public static FileInfo WaitFileUnlocked(FileInfo file)
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
                        Backup.InvokeOnAlert("File Locked " + file.FullName, true);
                    }
                    System.Threading.Thread.Sleep(1000);
                    file = new FileInfo(file.FullName);
                }
            } while (isLocked);
            return file;
        }
        internal static void WaitFileUnlocked(string fileName)
        {
            _ = WaitFileUnlocked(new FileInfo(fileName));
        }

        /// <summary>
        /// Forcibly delete the directory even if it contains read-only items
        /// https://stackoverflow.com/questions/611921/how-do-i-delete-a-directory-with-read-only-files-in-c
        /// </summary>
        /// <param name="path">Directory to delete</param>
        internal static void ForceDeleteDirectory(DirectoryInfo directory, AllertNotification OnAlert = null)
        {
            try
            {
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) // is a link
                {
                    directory.Delete();
                    return;
                }
                foreach (var subDir in directory.GetDirectories())
                {
                    ForceDeleteDirectory(subDir, OnAlert);
                }
                foreach (var file in directory.GetFileSystemInfos())
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                        file.Attributes = FileAttributes.Normal;
                }
                directory.Delete(true);
            }
            catch (Exception ex)
            {
                OnAlert?.Invoke(ex.Message, true);
            }
        }
    }
}
