using System;

namespace AntiGitConsole
{
	class Program
	{

		static readonly AntiGit.AntiGit _antigit = new AntiGit.AntiGit();

		static void Main(string[] args)
		{
			foreach (var arg in args)
			{
				ExecuteCommand(arg);
			}

			Console.WriteLine(_antigit.Info);
			PrintStatus();
			Console.WriteLine();
			PrintCommands();
			string command;
			do
			{
				command = Console.ReadLine();
				ExecuteCommand(command);
			} while (command != "q");
		}

		private static void ExecuteCommand(string command)
		{
			command = command.Trim();
			if (command.Length >= 2 && command[1] == '=')
			{
				var dir = command.Substring(2).Trim();
				if (System.IO.Directory.Exists(dir) || dir == "")
				{
					if (command.StartsWith("s="))
					{
						_antigit.SourceDir = dir;
						PrintSource();
					}
					else if (command.StartsWith("t="))
					{
						_antigit.TargetDir = dir;
						PrintTarget();
					}
					else if (command.StartsWith("g="))
					{
						_antigit.GitDir = dir;
						PrintGit();
					}
				}
				else
				{
					Console.WriteLine("Path not found");
				}
			}
			else if (command == "sb")
			{
				Console.WriteLine("Start backup");
				_antigit.StartBackup();
			}
			else if (command == "ssg")
			{
				Console.WriteLine("Stop sync git");
				_antigit.StopSyncGit();
			}
			else if (command == "sg")
			{
				Console.WriteLine("Sync git");
				_antigit.SyncGit();
			}
			else if (command == "s")
			{
				PrintStatus();
			}
			else if (command == "?")
			{
				PrintCommands();
			}
		}

		private static void PrintSource() => Console.WriteLine("Source = " + _antigit.SourceDir);
		private static void PrintTarget() => Console.WriteLine("Target = " + _antigit.TargetDir);
		private static void PrintGit() => Console.WriteLine("Git = " + _antigit.GitDir);

		private static void PrintStatus()
		{
			PrintSource();
			PrintTarget();
			PrintGit();
			Console.WriteLine("backup running = " + _antigit.BackupRunning);
			Console.WriteLine("sync git running = " + _antigit.SyncGitRunning);
		}
		private static void PrintCommands()
		{
			Console.WriteLine("commands available:");
			Console.WriteLine("? (help)");
			Console.WriteLine("s=source directory (set source directory)");
			Console.WriteLine("t=target directory (set target directory)");
			Console.WriteLine("g=git directory (set shared git directory)");
			Console.WriteLine("s (show current status)");
			Console.WriteLine("sb (start backup)");
			Console.WriteLine("sg (sync git)");
			Console.WriteLine("ssg (stop sync git)");
			Console.WriteLine("q (quit)");
		}
	}
}
