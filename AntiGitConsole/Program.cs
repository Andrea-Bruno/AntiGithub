using System;
using System.Data;

namespace AntiGitConsole
{
	class Program
	{

		static AntiGit.AntiGit Antigit = new AntiGit.AntiGit();

		static void Main(string[] args)
		{
			foreach (var arg in args)
			{
				ExecuteCommand(arg);
			}

			Console.WriteLine(Antigit.Info);
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
						Antigit.SourceDir = dir;
						PrintSource();
					}
					else if (command.StartsWith("t="))
					{
						Antigit.TargetDir = dir;
						PrintTarget();
					}
					else if (command.StartsWith("g="))
					{
						Antigit.GitDir = dir;
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
				Antigit.Startbackup();
			}
			else if (command == "ssg")
			{
				Console.WriteLine("Stop sync git");
				Antigit.StopSyncGit();
			}
			else if (command == "sg")
			{
				Console.WriteLine("Sync git");
				Antigit.SyncGit();
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

		private static void PrintSource() => Console.WriteLine("Source = " + Antigit.SourceDir);
		private static void PrintTarget() => Console.WriteLine("Target = " + Antigit.TargetDir);
		private static void PrintGit() => Console.WriteLine("Git = " + Antigit.GitDir);

		private static void PrintStatus()
		{
			PrintSource();
			PrintTarget();
			PrintGit();
			Console.WriteLine("backup running = " + Antigit.BackupRunning);
			Console.WriteLine("sync git running = " + Antigit.SyncGitRunning);
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
