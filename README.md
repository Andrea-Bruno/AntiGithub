# AntiGithub
Intuitive program that backs up code changes and synchronizes code between developers with automatic merge

Easy and powerful software for source code maintenance and backup! It is a simple and intuitive program to synchronize the code of projects maintained by multiple developers. Synchronization happens automatically, and the merge is perfect! No more lost files. The program must be run in administrator mode because it tries to save space when possible: The version history makes use of hardware links on the disk for the unchanged code parts, this functionality on windows works only in administrator mode. The solution consists of 3 projects:

    AntiGit (the heart of the program, the library that does it all!)
    AntiGitConsole (you can run the application in console mode, without the graphical interface)
    BackupCreator (the version of the program with the graphic interface, only for Windows systems). BackupCreator adds an icon at the bottom right in the TaskBar, double click the mouse to open the graphical interface. BackupCreator and AntiGitConsole are the same application, the first with a graphical interface and the second in terminal mode.

Usage: When developers modify the code, for example through Visual Studio, BackupCreator or AntiGitConsole it must be running, and it must be launched in administrator mode! There is no button to press, Push or Pull to perform, everything will be synchronized and the merge will be perfect! Everything is very easy! Now you can scrap github!

Setting: The SOURCE directory is the one with the files to keep (your projects and your solutions must be here), in the TARGET directory the daily backups will be saved, the GIT directory is a remote directory accessible to all those who work on the same source files, for example, the git directory can correspond to a disk of network or to the address of a pen drive connected to the router, in this directory AntiGit will create a synchronized version of the source in real time.

Automatically synchronize your projects and solutions with other developers. Automatic merge always perfect! Automatic daily backup of the code. On windows the graphical interface opens by clicking on the application icon at the bottom right of the taskbar. The application must be run in administrator mode! Right-click the app shortcut and select Properties.

How to start the application with administrator privileges:

Create a shortcut to the application (.exe) Right click and open the properties for the shortcut. Click on the Shortcut tab. Click the Advanced button. Check the Run as administrator option Launch the application via shortcut to start it as an administrator!

NOTE: It is advisable to start the application automatically when the computer starts.

Instructions: I just published AntiGithub, a backup and maintenance software for the source code of software projects, which synchronizes and merges the code automatically and perfectly. Operation: All programmers working on the same projects must install the application, and set it to start automatically, and set it up with a shared GIT directory on the network (for example, GIT can be a pen drive connected to the router, and visible to all). The GIT directory can also be exposed externally for those who work remotely, by setting the VPN server features of your router and connecting to it with the VPN. NOTES: Never manually copy files to the shared git directory, the software will do it when needed. The parts of the code that cannot be compiled yet will not be included in the merge so that no developer can end up with projects that cannot be compiled due to work in progress by other colleagues! Easy and intuitive! There is no button to press, there are no Push and Pull, everything is automatic!
