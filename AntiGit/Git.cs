using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AntiGitLibrary
{
	class Git
	{
		public Git(Context context)
		{
			Context = context;
#if DEBUG
			//var x1 = Merge.LoadTextFiles(new FileInfo(@"C:\test\text1.txt"));
			//Merge.ExecuteMerge(new FileInfo(@"C:\test\t1.txt"), new FileInfo(@"C:\test\t2.txt"), null, new FileInfo(@"C:\test\Result.txt"));
			//var x2 = Merge.LoadTextFiles(new FileInfo(@"C:\test\result.txt"));
			//Debugger.Break();
#endif
		}

		private readonly Context Context;
		private Thread gitTask;
		private int FullSyncCycle;
#if DEBUG
		const int SleepMs = 5000;
#else
		const int SleepMs = 60000;
#endif
		internal void FullSyncGit(string sourcePath, string gitPath)
		{
			gitTask = new Thread(() =>
			{
				if (LocalFiles == null)
					LocalFiles = LoadMemoryFile(sourcePath);
				if (RemoteFiles == null)
					RemoteFiles = LoadMemoryFile(gitPath);
				FullSyncCycle = 0;
				_stopSync = false;
				while (!_stopSync)
				{
					if (!string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(gitPath))
					{

						var oldestFile = DateTime.MinValue;
						var hasSynchronized = false;
						//#if !DEBUG
						try
						{
							//#endif

							if (!IsSourceAndGitCompatible(new DirectoryInfo(sourcePath), new DirectoryInfo(gitPath)))
							{
								Context.Alert(Resources.Dictionary.Warning1);
								return;
							}
							var newMemoryFile = new StringCollection();
							SyncGit(ref oldestFile, ref hasSynchronized, Scan.LocalDrive, sourcePath, gitPath, ref newMemoryFile);
							DeleteRemovedFiles(Scan.LocalDrive, newMemoryFile, sourcePath, gitPath);
							newMemoryFile = new StringCollection();
							SyncGit(ref oldestFile, ref hasSynchronized, Scan.RemoteDrive, gitPath, sourcePath, ref newMemoryFile);
							DeleteRemovedFiles(Scan.RemoteDrive, newMemoryFile, gitPath, sourcePath);
							FullSyncCycle++;
							//#if !DEBUG
						}
						catch (Exception e)
						{
							// If the sync process fails, there will be an attempt in the next round
							if ((DateTime.UtcNow - LastErrorTime).TotalMinutes > 10)
							{
								if (e.HResult == -2147024832) // Network no longer available
								{
									Context.Alert(e.Message);
								}
							}
							Debug.Write(e.Message);
							Debugger.Break();
							LastErrorTime = DateTime.UtcNow;
						}
						//#endif
						if (hasSynchronized)
						{
							Context.BackupOfTheChange();
						}
						//#if !DEBUG
						// If I don't see any recent changes, loosen the monitoring of the files so as not to stress the disk
						if ((DateTime.UtcNow - oldestFile).TotalMinutes > 30)
							Thread.Sleep(SleepMs);
						//#endif
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
		private DateTime LastErrorTime = default;
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
		void SyncGit(ref DateTime returnOldestFile, ref bool hasSynchronized, Scan scan, string sourcePath, string targetPath, ref StringCollection returnNewMemoryFile, string sourceRoot = null, DateTime? compilationTime = null)
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
#if DEBUG
			// code for testing
			//if (dir.FullName.EndsWith(@"BitBoxLab\Localization\Resources", StringComparison.InvariantCultureIgnoreCase))
			//	Debugger.Break();
#endif

			var nameDir = dir.Name.ToLower();
			if (!Context.ExcludeDir.Contains(nameDir) && (dir.Attributes & FileAttributes.Hidden) == 0)
			{
				var dirTarget = new DirectoryInfo(targetDirName);
				FileInfo[] targetFiles = null;
				var oldSourceMemoryFile = scan == Scan.LocalDrive ? LocalFiles : RemoteFiles;
				var oldTargetMemoryFile = scan == Scan.LocalDrive ? RemoteFiles : LocalFiles;
				if (!dirTarget.Exists)
				{
					if (oldSourceMemoryFile != null && oldSourceMemoryFile.Contains(dir.FullName) && (oldTargetMemoryFile != null && oldTargetMemoryFile.Contains(dirTarget.FullName)))
						return; //The directory has been deleted: So we don't create a removed directory again
					try
					{
						Directory.CreateDirectory(targetDirName);
					}
					catch (Exception e) { Context.WriteOutput(e.Message); }
				}
				else
				{
					targetFiles = dirTarget.GetFiles();
				}

				DirectoryInfo localDir = scan == Scan.LocalDrive ? dir : dirTarget;


				if (compilationTime == null)
					compilationTime = UpdateDateLimit(localDir);// Copy to remote only the files of the latest version working at compile time

				returnNewMemoryFile.Add(dir.FullName);
				foreach (var fileInfo in dir.GetFiles())
				{
					var file = fileInfo;
#if DEBUG
					// code for testing
					//if (file.FullName.EndsWith(@"\ResxGenerator\Form1.cs", StringComparison.InvariantCultureIgnoreCase))
					//	Debugger.Break();
#endif
					if (_stopSync) break;

					if (file.Attributes.HasFlag(FileAttributes.Hidden))
						continue;
					returnNewMemoryFile.Add(file.FullName);

					var targetFile = Path.Combine(targetDirName, file.Name);
					var target = targetFiles?.ToList().Find(x => x.Name == targetFile) ?? new FileInfo(targetFile);
					var localFile = scan == Scan.LocalDrive ? file : target;

					file = Support.WaitFileUnlocked(file);
					target = Support.WaitFileUnlocked(target);

					if (file.Exists && file.LastWriteTimeUtc > returnOldestFile)
						returnOldestFile = file.LastWriteTimeUtc;
					if (target.Exists && target.LastWriteTimeUtc > returnOldestFile)
						returnOldestFile = target.LastWriteTimeUtc;

					FileInfo from = null;
					FileInfo to = null;
					var copy = CopyType.None;

					var isDeletedFromUser = !target.Exists && oldSourceMemoryFile != null && oldSourceMemoryFile.Contains(file.FullName) && oldTargetMemoryFile != null && oldTargetMemoryFile.Contains(target.FullName);
					if (isDeletedFromUser) continue;

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

					//var oldSourceFileExists = oldMemoryFile.Contains(dir.FullName);



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
								Merge.ExecuteMerge(from, to, visualStudioRecoveryFile);
								hasSynchronized = true;
							}
							else
							{
								var attempt = 0;
								do
								{
									try
									{
										File.Copy(from.FullName, to.FullName, true);
										attempt = 0;
									}
									catch (Exception e)
									{
										attempt++;
										//if (e.HResult == -2147024864) // File already opened by another task
										if (attempt == 10)
										{
											Context.Alert(e.Message + " " + to.FullName);
										}
										Thread.Sleep(1000);
										Debugger.Break();
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
								hasSynchronized = true;
								if (compilationTime != null)
									MyPendingFiles.Remove(localFile);
							}
						}
						catch (Exception e)
						{
							// If the attempt fails it will be updated to the next round!
							if (Support.IsDiskFull(e))
								Context.Alert(e.Message, true);
						}
					}
				}
				var subDirectories = Directory.GetDirectories(sourcePath);
				foreach (var sourceDir in subDirectories)
				{
					SyncGit(ref returnOldestFile, ref hasSynchronized, scan, sourceDir, targetPath, ref returnNewMemoryFile, sourceRoot, compilationTime);
				}
			}
		}
		private const int maxFileAllowToDeleteInOneTime = 3;
		private void DeleteRemovedFiles(Scan scan, StringCollection memoryFile, string sourcePath, string targetPath)
		{
			if (_stopSync)
				return;
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

				if (FullSyncCycle > 1 && removedFromSource.Count > maxFileAllowToDeleteInOneTime)
				{
					var fileDeleted = "";
					for (var index = 0; index < removedFromSource.Count; index++)
					{
						if (index == 4 && removedFromSource.Count > 4)
						{
							fileDeleted += Environment.NewLine + "...";
							break;
						}
						var item = removedFromSource[index];
						fileDeleted += Environment.NewLine + item;
					}
					Context.Alert(Resources.Dictionary.Warning2 + fileDeleted, true);
				}
				else
				{
					bool isShow = false;
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
								Context.WriteOutput(e.Message);
							}
						}
						else
						{
							var dirInfo = new DirectoryInfo(target);
							if (dirInfo.Exists)
							{
								try
								{
									if (dirInfo.GetFiles().Count() + dirInfo.GetDirectories().Count() == 0)
									{
										dirInfo.Delete();
									}
									else
									{
										if (!isShow)
										{
											isShow = true;
											Context.Alert(Resources.Dictionary.Warning5 + Environment.NewLine + target, true);
										}
									}
								}
								catch (Exception e)
								{
									Context.WriteOutput(e.Message);
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
				var file = Path.Combine(Context.AppDir.FullName,  Support.GetHashCode(path) + ".txt");
				File.WriteAllLines(file, memory.Cast<string>().ToArray());
			}
		}

		private void DeleteMemoryFile(string path)
		{
			if (!string.IsNullOrEmpty(path))
			{
				var file = Path.Combine(Context.AppDir.FullName, Support.GetHashCode(path) + ".txt");
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
				var file = Path.Combine(Context.AppDir.FullName, Support.GetHashCode(path) + ".txt");
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
						Context.WriteOutput(Resources.Dictionary.Suggest1);
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
					Context.WriteOutput(e.Message);
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
			return total == 0 ? false : find / (double)total > limit;
		}
	}
}
