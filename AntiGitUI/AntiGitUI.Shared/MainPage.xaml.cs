using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Popups;
// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AntiGitUI
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class MainPage : Page
	{
		public AntiGitLibrary.Context AntiGithub;

		public MainPage()
		{
			this.InitializeComponent();

			void Alert(string Message)
			{
				var dialog = new MessageDialog(Message);
				_ = dialog.ShowAsync();
			}
			AntiGithub = new AntiGitLibrary.Context(Alert);
			Source.Text = AntiGithub.SourceDir ?? "";
			Target.Text = AntiGithub.TargetDir ?? "";
			Git.Text = AntiGithub.GitDir ?? "";
			Unloaded += MainPage_Unloaded;
		}

		private void MainPage_Unloaded(object sender, RoutedEventArgs e)
		{
			AntiGithub.StopSyncGit();
		}

		private async Task<string> PickDir()
		{
			var folderPicker = new FolderPicker();
			folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
			folderPicker.FileTypeFilter.Add("*");

			Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
			if (folder != null)
			{
				//Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder);
				return folder.Path;
			}
			return "";
		}

		public async void SelectSource_Click(object sender, RoutedEventArgs e)
		{
			Source.Text = await PickDir();
		}
		public async void SelectTarget_Click(object sender, RoutedEventArgs e)
		{
			Target.Text = await PickDir();
		}
		public async void SelectGit_Click(object sender, RoutedEventArgs e)
		{
			Git.Text = await PickDir();
		}

		private void Source_TextChanged(object sender, object e)
		{
			AntiGithub.SourceDir = Source.Text;
			Source.Text = AntiGithub.SourceDir;
		}

		private void Target_TextChanged(object sender, object e)
		{
			AntiGithub.TargetDir = Target.Text;
		}

		private void Git_TextChanged(object sender, object e)
		{
			AntiGithub.GitDir = Git.Text;
			Git.Text = AntiGithub.GitDir;
		}

		private void Backup_Click(object sender, RoutedEventArgs e)
		{
			AntiGithub.StartBackup();
		}

	}
}
