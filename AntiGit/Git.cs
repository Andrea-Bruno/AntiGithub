using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AntiGit
{
	class Git
	{
		public Git(AntiGit context)
		{
			Context = context;
		}

		private AntiGit Context;
		private Thread gitTask;
		private int fullSyncCycle;


		internal void FullSyncGit(string sourcePath, string gitPath)
		{
			LocalFiles = LoadMemoryFile(sourcePath);
			RemoteFiles = LoadMemoryFile(gitPath);
			gitTask = new Thread(() =>
			{
				fullSyncCycle = 0;
				_stopSync = false;
				while (!_stopSync)
				{
					if (!string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(gitPath))
					{
						if (!IsSourceAndGitCompatible(new DirectoryInfo(sourcePath), new DirectoryInfo(gitPath)))
						{
							AntiGit.Alert("Warning: Git sync cannot be started because the source and git directories contain different projects. Check the settings!");
							return;
						}

						var oldestFile = DateTime.MinValue;
						var hasSynchronized = false;
#if !DEBUG
							try
							{
#endif
						var memoryFile = new StringCollection();
						SyncGit(ref oldestFile, ref hasSynchronized, Scan.LocalDrive, sourcePath, gitPath, ref memoryFile);
						DeleteRemovedFiles(Scan.LocalDrive, memoryFile, sourcePath, gitPath);
						memoryFile = new StringCollection();
						SyncGit(ref oldestFile, ref hasSynchronized, Scan.RemoteDrive, gitPath, sourcePath, ref memoryFile);
						DeleteRemovedFiles(Scan.RemoteDrive, memoryFile, gitPath, sourcePath);
						fullSyncCycle++;
#if !DEBUG
							}
							catch (Exception e)
							{
								// If the sync process fails, there will be an attempt in the next round
								Debug.Write(e.Message);
								Debugger.Break();
						}
#endif
						if (hasSynchronized)
						{
							Context.BackupOfTheChange();
						}
#if !DEBUG
						// If I don't see any recent changes, loosen the monitoring of the files so as not to stress the disk
						if ((DateTime.UtcNow - oldestFile).TotalMinutes > 30)
							Thread.Sleep(60000);
#endif
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


		public void StopSyncGit()
		{
			_stopSync = true;
		}

		public bool SyncGitRunning => !_stopSync;

		private StringCollection LocalFiles;
		private StringCollection RemoteFiles;
		void SyncGit(ref DateTime returnOldestFile, ref bool hasSynchronized, Scan scan, string sourcePath, string targetPath, ref StringCollection memoryFile, string sourceRoot = null, DateTime? compilationTime = null)
		{
			if (_stopSync)
				return;
			if (sourceRoot == null)
			{
				sourceRoot = sourcePath;
			}
			var relativeDirName = sourcePath.Substring(sourceRoot.Length);
			var targetDirName = targetPath + relativeDirName;

			var dir = new DirectoryInfo(sourcePath);
			var nameDir = dir.Name.ToLower();
			if (!AntiGit.ExcludeDir.Contains(nameDir) && (dir.Attributes & FileAttributes.Hidden) == 0)
			{
				var dirTarget = new DirectoryInfo(targetDirName);
				FileInfo[] targetFiles = null;
				if (!dirTarget.Exists)
				{
					AntiGit.WriteOutput("create directory  " + targetDirName);
					try
					{
						Directory.CreateDirectory(targetDirName);
					}
					catch (Exception e) { AntiGit.WriteOutput(e.Message); }
				}
				else
				{
					targetFiles = dirTarget.GetFiles();
				}

				DirectoryInfo localDir = scan == Scan.LocalDrive ? dir : dirTarget;


				if (compilationTime == null)
					compilationTime = UpdateDateLimit(localDir);// Copy to remote only the files of the latest version working at compile time

				memoryFile.Add(dir.FullName);
				foreach (var file in dir.GetFiles())
				{

					if (_stopSync) break;

					if (file.Attributes.HasFlag(FileAttributes.Hidden))
						continue;
					memoryFile.Add(file.FullName);

					var targetFile = Path.Combine(targetDirName, file.Name);
					var target = targetFiles?.ToList().Find(x => x.Name == targetFile) ?? new FileInfo(targetFile);
					var localFile = scan == Scan.LocalDrive ? file : target;

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
						if (!target.Exists)
						{
							var memoryOfTarget = scan == Scan.LocalDrive ? RemoteFiles : LocalFiles;
							if (memoryOfTarget?.Contains(target.FullName) == true)
								continue; // It is not a new file but it is a deleted file!
						}
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
							var visualStudioRecoveryFile = (copy == CopyType.CopyFromRemote && compilationTime != null) ? FindVisualStudioRecoveryFile(to) : null; //NOTE: compilationTime != null is the file is a visual studio file
							if (copy == CopyType.CopyFromRemote && IsTextFiles(from) && (visualStudioRecoveryFile != null || MyPendingFiles.Contain(to)))
							{
								//AntiGit.WriteOutput("Merge " + @from.FullName + " => " + to.FullName);
								Merge(from, to, visualStudioRecoveryFile);
								hasSynchronized = true;
							}
							else
							{
								//AntiGit.WriteOutput("copy " + @from.FullName + " => " + to.FullName);
								try
								{
									File.Copy(from.FullName, to.FullName, true);
								}
								catch (Exception ex) { AntiGit.WriteOutput(ex.Message); }
#if DEBUG
								var verify = new FileInfo(to.FullName);
								if (RoundDate(verify.LastWriteTimeUtc) != RoundDate(from.LastWriteTimeUtc))
									Debugger.Break();
								//else
								//	Debugger.Break();
#endif
								hasSynchronized = true;
								if (compilationTime != null)
									MyPendingFiles.Remove(localFile);
							}
						}
						catch (Exception e)
						{
							AntiGit.WriteOutput(e.Message);
						}
					}
				}
				foreach (var sourceDir in Directory.GetDirectories(sourcePath))
				{
					SyncGit(ref returnOldestFile, ref hasSynchronized, scan, sourceDir, targetPath, ref memoryFile, sourceRoot, compilationTime);
				}
			}
		}

		private void DeleteRemovedFiles(Scan scan, StringCollection memoryFile, string sourcePath, string targetPath)
		{
			// Check if any files or directories have been deleted
			StringCollection oldMemoryFiles;

			if (scan == Scan.LocalDrive)
			{
				oldMemoryFiles = LocalFiles;
				if (MemoryFileIsChanged(LocalFiles, memoryFile))
				{
					SaveMemoryFile(memoryFile, sourcePath);
				}
				LocalFiles = memoryFile;
			}
			else
			{
				oldMemoryFiles = RemoteFiles;
				if (MemoryFileIsChanged(RemoteFiles, memoryFile))
				{
					SaveMemoryFile(memoryFile, sourcePath);
				}
				RemoteFiles = memoryFile;
			}

			if (oldMemoryFiles != null)
			{
				var removedFromSource = new List<string>();
				foreach (var oldMemoryFile in oldMemoryFiles)
				{
					if (!memoryFile.Contains(oldMemoryFile))
						removedFromSource.Add(oldMemoryFile);
				}

				if (fullSyncCycle > 1 && removedFromSource.Count > 1)
				{
					AntiGit.Alert("More than one file has been deleted, for security reasons we will not synchronize if more than one file is deleted!");
				}
				else
				{
					foreach (var item in removedFromSource)
					{
						var target = targetPath + item.Substring(sourcePath.Length);
						var fileInfo = new FileInfo(target);
						if (fileInfo.Exists)
						{
							try
							{
								fileInfo.Delete();
							}
							catch (Exception e)
							{
								AntiGit.WriteOutput(e.Message);
							}

						}
						else
						{
							var dirInfo = new FileInfo(target);
							if (dirInfo.Exists)
							{
								try
								{
									dirInfo.Delete();
								}
								catch (Exception e)
								{
									AntiGit.WriteOutput(e.Message);
								}
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

		private void SaveMemoryFile(StringCollection memory, string path)
		{
			if (memory != null && !string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(AntiGit.AppDir.FullName, GetHashCode(path) + ".txt");
				File.WriteAllLines(file, memory.Cast<string>().ToArray());
			}
		}

		private void DeleteMemoryFile(string path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(AntiGit.AppDir.FullName, GetHashCode(path) + ".txt");
				var fileInfo = new FileInfo(file);
				if (fileInfo.Exists)
				{
					fileInfo.Delete();
				}
			}
		}

		private StringCollection LoadMemoryFile(string path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(AntiGit.AppDir.FullName, GetHashCode(path) + ".txt");
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

		private static int LinePosition(List<Line> listFrom, int line, List<Line> listTo)
		{
			var insertTo = -1;
			var positions = new List<int>();
			int n = 0;
			var hash = listFrom[line].Hash;
			for (int i = 0; i < listTo.Count; i++)
			{
				if (listTo[i].Hash == hash)
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
				hash = listFrom[line - n].Hash;
				foreach (var item in positions.ToArray())
				{
					var pos = item - n;
					if (pos < 0 || listTo[pos].Hash != hash)
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
			listTo.ForEach(x => lines.Add(x.Text));
			try
			{
				File.WriteAllLines(to.FullName, lines);
			}
			catch (Exception e)
			{
				// If the attempt fails it will be updated to the next round!
				AntiGit.WriteOutput(e.Message);
			}
		}

		private static List<Line> LoadTextFiles(FileInfo file)
		{
			var fileLines = File.ReadAllLines(file.FullName);
			var listFile = new List<Line>();
			fileLines.ToList().ForEach(x => listFile.Add(new Line { Text = x, Hash = GetHashCode(x) }));
			return listFile;
		}

		private class Line
		{
			public string Text;
			public ulong Hash;
		}

		public static ulong GetHashCode(string input)
		{
			input = input.Replace("\t", "");
			input = input.Replace(" ", "");
			const ulong value = 3074457345618258791ul;
			var hashedValue = value;
			foreach (var t in input)
			{
				hashedValue += t;
				hashedValue *= value;
			}
			return hashedValue;
		}


		private static bool IsTextFiles(FileInfo file)
		{
			var extension = file.Extension;
			// You can add more file from this list: https://fileinfo.com/software/microsoft/visual_studio
			var textExtensions = new[] { ".cs", ".txt", ".json", ".xml", ".csproj", ".vb", ".xaml", ".xamlx", "xhtml", ".sln", ".cs", ".resx", ".asm", ".c", ".cc", ".cpp", ".asp", ".asax", ".aspx", ".cshtml", ".htm", ".html", ".master", ".js", ".config" };
			return textExtensions.Contains(extension);
		}

		private readonly PendingFiles MyPendingFiles = new PendingFiles();

		private class PendingFiles : List<string>
		{
			public void Add(FileInfo file)
			{
				Remove(file);
				Add(file.FullName.ToLower());
			}
			public void Remove(FileInfo file)
			{
				var filename = file.FullName.ToLower();
				var pending = Find(x => x == filename);
				if (pending != null)
				{
					Remove(pending);
				}
			}
			public bool Contain(FileInfo file)
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

		private static DateTime RoundDate(DateTime dt)
		{
			// FAT / VFAT has a maximum resolution of 2s
			// NTFS has a maximum resolution of 100 ns

			var add = dt.Millisecond < 500 ? 0 : 1; // 0 - 499 round to lowers, 500 - 999 to upper
			return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second + add);
		}

		/// <summary>
		/// Update only files with data lower than that of the last final compilation (files not yet compiled locally will not be sent to the remote repository to avoid having versions that do not work because with compilation errors)
		/// </summary>
		/// <param name="dir"></param>
		/// <returns>Date of the last working compilation of the project</returns>
		private static DateTime? UpdateDateLimit(DirectoryInfo dir)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "bin")))
			{
				var bin = new DirectoryInfo(Path.Combine(dir.FullName, "bin"));
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
						AntiGit.WriteOutput("Suggest: It is recommended setting Visual Studio Auto Recovery to 1 minute: tools->Options->Environment-AutoRecover");
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
					AntiGit.WriteOutput(e.Message);
				}
			}
#endif
			return null;
		}

		private static bool FilesAreSimilar(List<Line> list1, List<Line> list2, double limit = 0.6)
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
			return total == 0 ? false : find / (double)total > limit;
		}
	}
}
