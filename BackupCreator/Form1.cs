using System;
using System.Windows.Forms;

namespace BackupCreator
{
	public partial class Form1 : Form
	{


		public AntiGit.AntiGit AntiGit;

		public Form1()
		{
			InitializeComponent();
		}



		private void Form1_Load(object sender, EventArgs e)
		{
			// Define the border style of the form to a dialog box.
			FormBorderStyle = FormBorderStyle.FixedDialog;

			// Set the MaximizeBox to false to remove the maximize box.
			MaximizeBox = false;

			// Set the MinimizeBox to false to remove the minimize box.
			MinimizeBox = true;

			// Set the start position of the form to the center of the screen.
			StartPosition = FormStartPosition.CenterScreen;

			AntiGit = new AntiGit.AntiGit();
			Source.Text = AntiGit.SourceDir;
			Target.Text = AntiGit.TargetDir;
			Git.Text = AntiGit.GitDir;
			//Minimize_Click(null, null);
		}

		static void selectPath(System.Windows.Forms.TextBox textBox)
		{
			using (var fbd = new FolderBrowserDialog())
			{
				var result = fbd.ShowDialog();
				if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
				{
					textBox.Text = fbd.SelectedPath;
				}
			}
		}


		private void selectSource_Click(object sender, EventArgs e)
		{
			selectPath(Source);
		}

		private void selectTarget_Click(object sender, EventArgs e)
		{
			selectPath(Target);
		}

		private void SelectGit_Click(object sender, EventArgs e)
		{
			selectPath(Git);
		}

		private void Source_TextChanged(object sender, EventArgs e)
		{
			AntiGit.SourceDir = Source.Text;
		}

		private void Target_TextChanged(object sender, EventArgs e)
		{
			AntiGit.TargetDir = Target.Text;
		}

		private void Git_TextChanged(object sender, EventArgs e)
		{
			AntiGit.GitDir = Git.Text;
		}


		private void backup_Click(object sender, EventArgs e)
		{
			AntiGit.Startbackup();
		}


		private void Form1_FormClosed(object sender, FormClosedEventArgs e)
		{
			AntiGit.StopSyncGit();
		}

		private void notifyIcon1_DoubleClick(object sender, EventArgs e)
		{
			WindowState = FormWindowState.Normal;
			ShowInTaskbar = true;
			//Show();
			notifyIcon1.Visible = false;
		}

		private void Minimize_Click(object sender, EventArgs e)
		{
			WindowState = FormWindowState.Minimized;
			ShowInTaskbar = false;
			//Hide();
			notifyIcon1.Visible = true;
		}
	}





}


