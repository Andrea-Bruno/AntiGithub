
namespace BackupCreator
{
	partial class Form1
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
			this.Source = new System.Windows.Forms.TextBox();
			this.Target = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.backup = new System.Windows.Forms.Button();
			this.selectSource = new System.Windows.Forms.Button();
			this.selectTarget = new System.Windows.Forms.Button();
			this.label3 = new System.Windows.Forms.Label();
			this.Git = new System.Windows.Forms.TextBox();
			this.SelectGit = new System.Windows.Forms.Button();
			this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
			this.Minimize = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// Source
			// 
			this.Source.Location = new System.Drawing.Point(12, 25);
			this.Source.Name = "Source";
			this.Source.Size = new System.Drawing.Size(638, 20);
			this.Source.TabIndex = 0;
			this.Source.TextChanged += new System.EventHandler(this.Source_TextChanged);
			// 
			// Target
			// 
			this.Target.Location = new System.Drawing.Point(12, 64);
			this.Target.Name = "Target";
			this.Target.Size = new System.Drawing.Size(638, 20);
			this.Target.TabIndex = 0;
			this.Target.TextChanged += new System.EventHandler(this.Target_TextChanged);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(41, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "Source";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 48);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(38, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "Target";
			// 
			// backup
			// 
			this.backup.Location = new System.Drawing.Point(12, 129);
			this.backup.Name = "backup";
			this.backup.Size = new System.Drawing.Size(75, 23);
			this.backup.TabIndex = 3;
			this.backup.Text = "Backup";
			this.backup.UseVisualStyleBackColor = true;
			this.backup.Click += new System.EventHandler(this.backup_Click);
			// 
			// selectSource
			// 
			this.selectSource.Location = new System.Drawing.Point(657, 21);
			this.selectSource.Name = "selectSource";
			this.selectSource.Size = new System.Drawing.Size(75, 23);
			this.selectSource.TabIndex = 4;
			this.selectSource.Text = "select";
			this.selectSource.UseVisualStyleBackColor = true;
			this.selectSource.Click += new System.EventHandler(this.selectSource_Click);
			// 
			// selectTarget
			// 
			this.selectTarget.Location = new System.Drawing.Point(656, 61);
			this.selectTarget.Name = "selectTarget";
			this.selectTarget.Size = new System.Drawing.Size(75, 23);
			this.selectTarget.TabIndex = 4;
			this.selectTarget.Text = "select";
			this.selectTarget.UseVisualStyleBackColor = true;
			this.selectTarget.Click += new System.EventHandler(this.selectTarget_Click);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(12, 87);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(20, 13);
			this.label3.TabIndex = 2;
			this.label3.Text = "Git";
			// 
			// Git
			// 
			this.Git.Location = new System.Drawing.Point(12, 103);
			this.Git.Name = "Git";
			this.Git.Size = new System.Drawing.Size(638, 20);
			this.Git.TabIndex = 0;
			this.Git.TextChanged += new System.EventHandler(this.Git_TextChanged);
			// 
			// SelectGit
			// 
			this.SelectGit.Location = new System.Drawing.Point(656, 100);
			this.SelectGit.Name = "SelectGit";
			this.SelectGit.Size = new System.Drawing.Size(75, 23);
			this.SelectGit.TabIndex = 4;
			this.SelectGit.Text = "select";
			this.SelectGit.UseVisualStyleBackColor = true;
			this.SelectGit.Click += new System.EventHandler(this.SelectGit_Click);
			// 
			// notifyIcon1
			// 
			this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
			this.notifyIcon1.Text = "notifyIcon1";
			this.notifyIcon1.Visible = true;
			this.notifyIcon1.DoubleClick += new System.EventHandler(this.notifyIcon1_DoubleClick);
			// 
			// Minimize
			// 
			this.Minimize.Location = new System.Drawing.Point(656, 160);
			this.Minimize.Name = "Minimize";
			this.Minimize.Size = new System.Drawing.Size(75, 23);
			this.Minimize.TabIndex = 5;
			this.Minimize.Text = "Minimize";
			this.Minimize.UseVisualStyleBackColor = true;
			this.Minimize.Click += new System.EventHandler(this.Minimize_Click);
			// 
			// Form1
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(772, 206);
			this.Controls.Add(this.Minimize);
			this.Controls.Add(this.SelectGit);
			this.Controls.Add(this.selectTarget);
			this.Controls.Add(this.selectSource);
			this.Controls.Add(this.backup);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.Git);
			this.Controls.Add(this.Target);
			this.Controls.Add(this.Source);
			this.Name = "Form1";
			this.ShowInTaskbar = false;
			this.Text = "Backup creator";
			this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
			this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.Form1_FormClosed);
			this.Load += new System.EventHandler(this.Form1_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox Source;
		private System.Windows.Forms.TextBox Target;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Button backup;
		private System.Windows.Forms.Button selectSource;
		private System.Windows.Forms.Button selectTarget;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TextBox Git;
		private System.Windows.Forms.Button SelectGit;
		private System.Windows.Forms.NotifyIcon notifyIcon1;
		private System.Windows.Forms.Button Minimize;
	}
}

