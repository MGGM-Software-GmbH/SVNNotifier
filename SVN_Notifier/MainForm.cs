//
// SVN Notifier
// Copyright 2007 SIA Computer Hardware Design (www.chd.lv)
//
// This file is part of SVN Notifier.
//
// SVN Notifier is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 3 of the License, or
// (at your option) any later version.
//
// SVN Notifier is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
//

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

namespace CHD.SVN_Notifier
{
	public partial class MainForm : Form
	{
		private readonly Dictionary<string, string> errorLog = new();
		private bool reupdateStatus;
		private readonly ManualResetEvent updateNotInProgress = new ManualResetEvent(true);
		private readonly ConcurrentQueue<SvnItem> forcedFolders = new();
		private bool timerEnabledWhenSuspended = true;


		public MainForm()
		{
			InitializeComponent();
			// Format grid.
			ItemListView.AutoGenerateColumns = false;
			ControlHelper.ApplyBorderStyle(ItemListView);
			ControlHelper.ApplyBorderStyle(MenuToolStrip);
			if (Config.HideOnStartup)
			{
				WindowState = FormWindowState.Minimized;
				ShowInTaskbar = false;
			}
			else
				ShowInTaskbar = Config.ShowInTaskbar;
			SvnTools.ErrorAdded += OnErrorAdded;
			AddPowerEventListener();
			FormInit();
			ApplyLocalization();
		}

		private void AddPowerEventListener()
		{
			Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
		}

		private void RemovePowerEventListener()
		{
			// Static events must be removed when application closes to avoid memory leaks
			Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
		}

		void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
		{
			switch (e.Mode)
			{
				case Microsoft.Win32.PowerModes.Suspend:
					timerEnabledWhenSuspended = StatusUpdateTimer.Enabled;
					StatusUpdateTimer.Stop();
					break;
				case Microsoft.Win32.PowerModes.Resume:
					PauseTimer.Stop();
					if (timerEnabledWhenSuspended)
					{
						PauseTimer.Interval = 1000 * Config.PauseAfterWindowsResumeInterval;
						PauseTimer.Start();
					}
					break;
			}
		}

		private void FormInit()
		{
			// Load and apply Windows Explorer-style sorting (StrCmpLogicalW).
			folders = new BindingList<SvnItem>(Config.ReadSvnFolders().OrderBy(x=>x.Path, WindowsPathComparer.Instance).ToList());
			ItemListView.DataSource = folders;
			UpdateFormSize();
			UpdateListViewFolderNames();

			if (Config.PauseAfterApplicationStartupInterval > 0)
			{
				PauseTimer.Interval = 1000 * Config.PauseAfterApplicationStartupInterval;
			}
			PauseTimer.Enabled = Config.DoPauseAfterApplicationStartup && Config.PauseAfterApplicationStartupInterval > 0;
		}


		private void UpdateFormSize()
		{
			int[] size = Config.ReadMainFormSize();

			if ((size[0] == 0) || (size[1] == 0))
			{
				Config.SaveMainFormSize(Width, Height);
			}
			else
			{
				Width = size[0];
				Height = size[1];
			}
		}

		//////////////////////////////////////////////////////////////////////////////

		#region Main menu handlers


		private void ExportConfigurationMenuItem_Click(object sender, EventArgs e)
		{
			SaveFileDialog sfd = new SaveFileDialog
			{
				FileName = "SVN_Notifier.ini",
				Filter = "Ini files (*.ini)|*.ini|All files (*.*)|*.*",
				RestoreDirectory = true
			};

			if (sfd.ShowDialog() == DialogResult.OK)
			{
				try
				{
					File.Copy(Config.iniFileName, sfd.FileName, true);
				}
				catch (Exception ex)
				{
					ShowError(ex.Message);
				}
			}
		}


		private void ImportConfigurationMenuItem_Click(object sender, EventArgs e)
		{
			OpenFileDialog ofd = new OpenFileDialog
			{
				Filter = "Ini files (*.ini)|*.ini|All files (*.*)|*.*",
				RestoreDirectory = true
			};

			if (ofd.ShowDialog() == DialogResult.OK)
			{
				if ((folders.Count > 0) && MessageBox.Show("All current settings will be lost.\n\nDo you really want to change the settings?", Loc.Msg_Attention, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
					return;

				try
				{
					File.Copy(ofd.FileName, Config.iniFileName, true);
					Config.Init();

					// Check for correct import configs
					if (!Config.IsSettingsOK())
					{
						Hide();
						StatusUpdateTimer.Stop();
						TrayNotifyIcon.Icon = trayIcon_Unknown;

						if (new SettingsForm().ShowDialog() != DialogResult.OK)
						{
							Application.Exit();
							return;
						}
						ShowInTaskbar = Config.ShowInTaskbar;
						Show();
					}

					FormInit();
				}
				catch (Exception ex)
				{
					ShowError(ex.Message);
				}
			}
		}


		private void SettingsMenuItem_Click(object sender, EventArgs e)
		{
			new SettingsForm().ShowDialog(this);
			ShowInTaskbar = Config.ShowInTaskbar;
			UpdateTray(false);                      // To enable/disable "Update All" command
		}


		private void ExitMenuItem_Click(object sender, EventArgs e)
		{
			Application.Exit();
		}


		private void AboutMenuItem_Click(object sender, EventArgs e)
		{
			new AboutForm().ShowDialog(this);
		}


		private void UpdateAllMenuItem_Click(object sender, EventArgs e)
		{
			WindowState = FormWindowState.Minimized;
			UpdateAll();
		}

		private void AddFolder(string path)
		{
			if (!folders.Any(x => x.Path == path))
			{
				if (Directory.Exists(path + @"\.svn") || Directory.Exists(path + @"\_svn"))
				{
					var folder = new SvnItem(path, PathType.Directory);
					folders.Add(folder);
					UpdateListViewFolderNames();
					Config.SaveSvnFolders(folders);
					UpdateTray(false);
					BeginUpdateFolderStatuses();
				}
				else
				{
					MessageBox.Show(Loc.Msg_FolderNotSvn, "SVN Notifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			else
				SelectFolder(path);
		}

		private void AddFolderButton_Click(object sender, EventArgs e)
		{
			if (MainFolderBrowserDialog.ShowDialog() == DialogResult.OK)
			{
				string path = MainFolderBrowserDialog.SelectedPath;

				AddFolder(path);
			}
		}

		private void AddFile(string fileName)
		{
			string path = Path.GetDirectoryName(fileName);

			if (!folders.Any(x => x.Path == fileName))
			{
				if (Directory.Exists(path + @"\.svn") || Directory.Exists(path + @"\_svn"))
				{
					var item = new SvnItem(fileName, PathType.File);
					folders.Add(item);
					UpdateListViewFolderNames();
					Config.SaveSvnFolders(folders);
					UpdateTray(false);
					BeginUpdateFolderStatuses();
				}
				else
				{
					MessageBox.Show(Loc.Msg_FileNotSvn, "SVN Notifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			else
				SelectFolder(fileName);
		}

		private void AddFileButton_Click(object sender, EventArgs e)
		{
			if (MainOpenFileDialog.ShowDialog() == DialogResult.OK)
			{
				string fileName = MainOpenFileDialog.FileName;

				AddFile(fileName);
			}
		}


		private void RemoteButton_Click(object sender, EventArgs e)
		{
			if (ItemListView.SelectedRows.Count == 0)
				return;
			int selectedIndex = ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0]);
			string path = folders[selectedIndex].Path;

			if (MessageBox.Show(Loc.Msg_ConfirmRemove(path), "SVN Notifier", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
				return;
			folders.RemoveAt(selectedIndex);
			UpdateListViewFolderNames();
			newNonUpdatedFolders.Clear();
			Config.SaveSvnFolders(folders);
			UpdateTray(true);
			BeginUpdateFolderStatuses();
		}

		#endregion

		//////////////////////////////////////////////////////////////////////////////

		#region Toolbar button handlers

		private void UpdateButton_Click(object sender, EventArgs e)
		{
			UpdateFolder();
		}


		private void CommitButton_Click(object sender, EventArgs e)
		{
			CommitFolder();
		}


		private void OpenButton_Click(object sender, EventArgs e)
		{
			OpenFolder();
		}


		private void ChangeLogButton_Click(object sender, EventArgs e)
		{
			ShowChangeLog();
		}


		private void LogButton_Click(object sender, EventArgs e)
		{
			ShowFullLog();
		}


		#endregion

		//////////////////////////////////////////////////////////////////////////////

		#region Context menu handlers

		private void CheckNowMenuItem_Click(object sender, EventArgs e)
		{
			if (ItemListView.SelectedRows.Count == 0)
				return;
			int selectedIndex = ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0]);
			forcedFolders.Enqueue(folders[selectedIndex]);
			BeginUpdateFolderStatuses();
		}


		private void UpdateMenuItem_Click(object sender, EventArgs e)
		{
			UpdateFolder();
		}


		private void CommitMenuItem_Click(object sender, EventArgs e)
		{
			CommitFolder();
		}


		private void OpenMenuItem_Click(object sender, EventArgs e)
		{
			OpenFolder();
		}


		private void ChangeLogMenuItem_Click(object sender, EventArgs e)
		{
			ShowChangeLog();
		}


		private void LogMenuItem_Click(object sender, EventArgs e)
		{
			ShowFullLog();
		}


		private void PropertiesButton_Click(object sender, EventArgs e)
		{

			int selectedIndex = ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0]);
			SvnItem folder = folders[selectedIndex];
			if (new PropertiesForm(folder).ShowDialog(this) == DialogResult.OK)
			{
				newNonUpdatedFolders.Clear();
				UpdateListViewFolderNames();
				Config.SaveSvnFolders(folders);
				UpdateTray(true);
				BeginUpdateFolderStatuses();
			}
		}


		private void ItemMenu_Opening(object sender, CancelEventArgs e)
		{
			UpdateContextMenuItem();
		}


		private void UpdateContextMenuItem()
		{
			CheckNowMenuItem.Enabled =
				CommitMenuItem.Enabled =
				UpdateMenuItem.Enabled =
				OpenMenuItem.Enabled =
				ChangeLogMenuItem.Enabled =
				LogMenuItem.Enabled =
				PropertiesButton.Enabled =
				PropertiesMenuItem.Enabled = false;

			if (ItemListView.SelectedRows.Count == 0)
				return;

			int selectedIndex = ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0]);

			switch (folders[selectedIndex].Status)
			{
				case SvnStatus.NeedUpdate:
					CheckNowMenuItem.Enabled =
						UpdateMenuItem.Enabled =
						OpenMenuItem.Enabled =
						ChangeLogMenuItem.Enabled =
						LogMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;

				case SvnStatus.NeedUpdate_Modified:
					CheckNowMenuItem.Enabled =
						CommitMenuItem.Enabled =
						UpdateMenuItem.Enabled =
						OpenMenuItem.Enabled =
						ChangeLogMenuItem.Enabled =
						LogMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;

				case SvnStatus.UpToDate_Modified:
					CheckNowMenuItem.Enabled =
						CommitMenuItem.Enabled =
						OpenMenuItem.Enabled =
						LogMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;

				case SvnStatus.UpToDate:
					CheckNowMenuItem.Enabled =
						OpenMenuItem.Enabled =
						LogMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;

				case SvnStatus.Unknown:
					CheckNowMenuItem.Enabled =
						OpenMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;

				case SvnStatus.Error:
					CheckNowMenuItem.Enabled =
						OpenMenuItem.Enabled =
						PropertiesButton.Enabled =
						PropertiesMenuItem.Enabled = true;
					break;
			}
		}

		#endregion

		//////////////////////////////////////////////////////////////////////////////

		#region Tray menu handlers

		private void TrayShowProgramMenuItem_Click(object sender, EventArgs e)
		{
			ShowFolderList();
		}


		private void TrayExitMenuItem_Click(object sender, EventArgs e)
		{
			Application.Exit();
		}

		private void TrayUpdateAllMenuItem_Click(object sender, EventArgs e)
		{
			UpdateAll();
		}

		#endregion

		////////////////////////////////////////////////////////////////////////////////////

		#region listViewFolders handlers

		private void ItemListView_SelectedIndexChanged(object sender, EventArgs e)
		{
			var en = ItemListView.SelectedRows.Count > 0;
			RemoveMenuItem.Enabled = en;
			RemoteButton.Enabled = en;
			PropertiesButton.Enabled = en;
			if (en)
			{
				var folder = folders[ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0])];
				ChangeLogButton.Enabled = UpdateButton.Enabled = LogButton.Enabled = false;
				if (folder.Status == SvnStatus.NeedUpdate || folder.Status == SvnStatus.NeedUpdate_Modified)
					ChangeLogButton.Enabled = UpdateButton.Enabled = LogButton.Enabled = true;
				else if (folder.Status == SvnStatus.UpToDate || folder.Status == SvnStatus.UpToDate_Modified)
					LogButton.Enabled = true;

				if (folder.Status == SvnStatus.NeedUpdate_Modified || folder.Status == SvnStatus.UpToDate_Modified)
					CommitButton.Enabled = true;
				OpenButton.Enabled = Directory.Exists(folder.Path) || File.Exists(folder.Path);
				Text = $"{Application.ProductName} {Application.ProductVersion}";
				if (folder.RepositoryUrl != null)
					Text += $" - {folder.RepositoryUrl}";
			}
			else
			{
				ChangeLogButton.Enabled = false;
				UpdateButton.Enabled = false;
				CommitButton.Enabled = false;
				OpenButton.Enabled = false;
				LogButton.Enabled = false;
				Text = $"{Application.ProductName} {Application.ProductVersion}";
			}
		}


		private void ItemListView_KeyDown(object sender, KeyEventArgs e)
		{
			switch (e.KeyCode)
			{
				case Keys.Delete:
					RemoteButton_Click(sender, null);
					break;

				case Keys.Insert:
					AddFolderButton_Click(sender, null);
					break;

				case Keys.Enter:
					ItemListView_DoubleClick(sender, null);
					break;
			}
		}


		private void ItemListView_DoubleClick(object sender, EventArgs e)
		{
			switch (Config.ItemDoubleClickAction)
			{
				case Config.Action.openAction:
					if (OpenButton.Enabled)
						OpenFolder();
					break;

				case Config.Action.logAction:
					if (ChangeLogButton.Enabled)
						ShowChangeLog();
					else if (LogButton.Enabled)
						ShowFullLog();
					break;

				case Config.Action.updateAction:
					if (UpdateButton.Enabled)
						UpdateFolder();
					break;

				case Config.Action.commitAction:
					if (CommitButton.Enabled)
						CommitFolder();
					break;

				case Config.Action.checkNow:
					int selectedIndex = ItemListView.Rows.IndexOf(ItemListView.SelectedRows[0]);
					forcedFolders.Enqueue(folders[selectedIndex]);
					BeginUpdateFolderStatuses();
					break;
			}
		}


		private void ItemListView_DragDrop(object sender, DragEventArgs e)
		{
			// Determine whether dropped object is a file or folder.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] filePath = ((string[])e.Data.GetData(DataFormats.FileDrop));
				foreach (string p in filePath)
				{
					if (File.Exists(p))
						AddFile(p);
					else if (Directory.Exists(p))
						AddFolder(p);
				}
			}
		}


		#endregion

		////////////////////////////////////////////////////////////////////////////////////

		#region NotifyIcon handlers

		private void TrayNotifyIcon_MouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
			{
				ShowFolderList();
				ShowInTaskbar = Config.ShowInTaskbar;
			}
		}


		private void TrayNotifyIcon_BalloonTipClicked(object sender, EventArgs e)
		{
			ShowFolderList();
			ShowInTaskbar = Config.ShowInTaskbar;

			if (firstBalloonPath != null)
				SelectFolder(firstBalloonPath);
		}
		#endregion

		////////////////////////////////////////////////////////////////////////////////////

		#region MainForm handlers

		private void MainForm_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
				Hide();

			e.Handled = false;
		}


		private void MainForm_Closing(object sender, CancelEventArgs e)
		{
			if (!sessionEndInProgress)
			{
				e.Cancel = true;
				Hide();
			}
		}

		private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
		{
			RemovePowerEventListener();
		}

		private void MainForm_Activated(object sender, EventArgs e)
		{
			FormStateChanged(true);
			BeginUpdateFolderStatuses();
		}


		private void MainForm_Deactivate(object sender, EventArgs e)
		{
			FormStateChanged(false);
		}


		private void MainForm_ResizeEnd(object sender, EventArgs e)
		{
			if (WindowState == FormWindowState.Normal)
				Config.SaveMainFormSize(Width, Height);
		}


		#endregion

		////////////////////////////////////////////////////////////////////////////////////


		////////////////////////////////////////////////////////////////////////////////////

		private void FormStateChanged(bool _formIsActive)
		{
			formIsActive = _formIsActive;

			bool startTimer = StatusUpdateTimer.Enabled;
			StatusUpdateTimer.Stop();

			if (startTimer) StartTimer();
		}


				private void ShowFolderList()
		{
			Show();
			if (WindowState == FormWindowState.Minimized)
				WindowState = FormWindowState.Normal;
			Activate();
		}

		////////////////////////////////////////////////////////////////////////////////////

		private readonly BindingList<SvnItem> newNonUpdatedFolders = new BindingList<SvnItem>();
		private delegate void UpdateListViewMethod(SvnItem folder, SvnStatus folderStatus, DateTime statusTime);
		private delegate void SetStatusBarTextMethod(string text);
		private delegate void UpdateErrorLogMethod(string path, string error);
		private delegate void CheckedInvokeMethod(Delegate method, object[] args);
		private delegate void ShowUpdateErrorsMethod(SvnFolderProcess sfp);

		internal static Thread? statusThread;
		string firstBalloonPath;
		private bool formIsActive;
		private bool repeatInvoke;

		////////////////////////////////////////////////////////////////////////////////////

		private void FormStateChangedrmStateChanged(bool _formIsActive)
		{
			formIsActive = _formIsActive;

			bool startTimer = StatusUpdateTimer.Enabled;
			StatusUpdateTimer.Stop();

			if (startTimer) StartTimer();
		}


		private void StartTimer()
		{
			PauseTimer.Stop(); // Could be running if Form is activated shortly after startup

			int intervalMs = SvnItem.FindNextStatusUpdateTimeMs(folders, formIsActive);

			if ((intervalMs > 333) && (SvnTools.svnFolderProcesses.Count > 0))
				intervalMs = 333;   // Wait commit process(es) finish at least 3 times per second

			StatusUpdateTimer.Interval = intervalMs == 0 ? 1 : intervalMs;
			StatusUpdateTimer.Start();
		}


		private void statusUpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
		{
			BeginUpdateFolderStatuses();
		}

		private void PauseTimer_Tick(object sender, EventArgs e)
		{
			PauseTimer.Stop();
			StartTimer();
		}

		private void BeginUpdateFolderStatuses()
		{
			if (folders.Count > 0)
			{
				StatusUpdateTimer.Stop();

				lock (this)
				{
					if (statusThread == null)
					{
						reupdateStatus = false;

						statusThread = new Thread(StatusUpdateThread_Run);
						statusThread.IsBackground = true;
						statusThread.Start();
					}
					else
						reupdateStatus = true;
				}
			}
		}


		/// <summary>
		/// Executed on working thread
		/// </summary>
		private void StatusUpdateThread_Run()
		{
			try
			{
				while (!Created) Thread.Sleep(10);

				while (true)
				{
					SafeInvoke(new MethodInvoker(BeginUpdateListView));
					foreach (SvnItem folder in (SvnItem[])folders.ToArray().Clone())
					{
						if (!folder.Enabled)
							continue;
						updateNotInProgress.WaitOne();
						bool skipUpdateStatus = false;
						// Check commit and update processes for finishing
						for (int i = 0; i < SvnTools.svnFolderProcesses.Count; i++)
						{
							var sfp = (SvnFolderProcess)SvnTools.svnFolderProcesses[i];

							if (sfp.process.HasExited)
							{
								if (sfp.isUpdateCommand && sfp.updateError)
									SafeInvoke(new ShowUpdateErrorsMethod(ShowUpdateErrors), new object[] { sfp }, Int32.MaxValue);

								UpdateFolderStatus(sfp.folder);
								SvnTools.svnFolderProcesses.RemoveAt(i--);
							}
							else if ((folder.Path == sfp.folder.Path) && sfp.isUpdateCommand)
							{
								skipUpdateStatus = true;        // Because updating is still in progress
								SvnTools.ReadProcessOutput(sfp);
							}
						}

						while (forcedFolders.TryDequeue(out var forcedFolder))
							UpdateFolderStatus(forcedFolder);

						if ((folder.StatusTime + new TimeSpan(0, 0, folder.GetInterval(formIsActive)) <= DateTime.Now) && !skipUpdateStatus)
							UpdateFolderStatus(folder);
					}



					lock (this)
					{
						if (!reupdateStatus)
						{
							statusThread = null;
							break;
						}
						else
							reupdateStatus = false;
					}
				}

				SafeInvoke(new MethodInvoker(EndUpdateListView));
			}
			catch (Exception e)     // Otherwise it will just lost
			{
				ShowError(Loc.Msg_StatusThreadError(e.ToString()));
				Application.Exit();
			}
		}


		/// <summary>
		/// Executed on working thread
		/// </summary>
		private void SafeInvoke(Delegate method)
		{
			SafeInvoke(method, null);
		}


		/// <summary>
		/// Executed on working thread
		/// </summary>
		private void SafeInvoke(Delegate method, object[] args)
		{
			SafeInvoke(method, args, 10000);
		}


		/// <summary>
		/// Executed on working thread
		/// </summary>
		private void SafeInvoke(Delegate method, object[] args, int timeoutMs)
		{
			// Begin/EndInvoke usage is workaround for "form.Invoke hungs sometimes" problem
			repeatInvoke = true;
			do
			{
				IAsyncResult ar;
				try
				{
					ar = BeginInvoke(new CheckedInvokeMethod(CheckedInvoke), new object[] { method, args });
				}
				catch   // Avoid exceptions for not yet created or disposed form
				{
					return;
				}

				if (ar.AsyncWaitHandle.WaitOne(timeoutMs, false))       // This timeout should be increased when debugging
				{
					try
					{
						EndInvoke(ar);
					}
					catch   // Avoid exceptions for not yet created or disposed form
					{
						return;
					}
				}
				//				else
				//					MessageBox.Show ("form.Invoke timeout!!! Repeat = " + repeatInvoke);		// Note: both "True" and "False" where observed
			}
			while (repeatInvoke);
		}


		private void CheckedInvoke(Delegate method, object[] args)
		{
			method.DynamicInvoke(args);
			repeatInvoke = false;
		}


		/// <summary>
		/// Executed on working thread
		/// </summary>
		private void UpdateFolderStatus(SvnItem folder)
		{
			SafeInvoke(new SetStatusBarTextMethod(SetStatusBarText), new object[] { Loc.Status_Checking(folder.Path) });
			DateTime statusTime = DateTime.Now;
			if (sessionEndInProgress) return;       // Need to avoid error on svn.exe invoking
			SvnStatus status = SvnTools.GetSvnFolderStatus(folder);
			SafeInvoke(new UpdateListViewMethod(UpdateListView), new object[] { folder, status, statusTime });
		}


		private void BeginUpdateListView()
		{
			newNonUpdatedFolders.Clear();
		}


		private void SetStatusBarText(string text)
		{
			StatusLabel.Text = text;
		}


		private void UpdateListView(SvnItem folder, SvnStatus folderStatus, DateTime statusTime)
		{
			int i = folders.IndexOf(folder);
			if (i < 0) return;

			if (statusTime < folder.StatusTime)
				return;

			if (folder.Status != folderStatus)
			{
				folder.Status = folderStatus;
				if ((folderStatus == SvnStatus.NeedUpdate) ||
					(folderStatus == SvnStatus.NeedUpdate_Modified))
				{
					newNonUpdatedFolders.Add(folder);
					UpdateTray(true);
					
					// Auto-Update: if enabled for this folder, update it silently in the background
					if (folder.AutoUpdate)
					{
						folder.Status = SvnStatus.Unknown;
						folder._IsAutoUpdating = true;
						SvnTools.BeginUpdateSilently(folder);
					}
				}
				else
				{
					// If auto-update was in progress and is now complete, show toast
					if (folder._IsAutoUpdating)
					{
						folder._IsAutoUpdating = false;
						TrayNotifyIcon.ShowBalloonTip(3000, Loc.Tooltip_AutoUpdate, Loc.Toast_AutoUpdateCompleted(folder.VisiblePath), ToolTipIcon.Info);
					}
					UpdateTray(false);
				}

				// Refresh buttons
				ItemListView_SelectedIndexChanged(null, null);
			}
			else
				folder.Status = folderStatus;       // Update status time only
		}


		private void EndUpdateListView()
		{
			StatusLabel.Text = "";

			// Reduce used memory
			GC.Collect();

			if (!PauseTimer.Enabled)
			{
				// If the pauseTimer is running it will start up 
				// the statusUpdateTimer once the pause is done.
				StartTimer();
			}
		}


		private void UpdateTray(bool newNonUpdatedFoldersChanged)
		{
			// Update tray ToolTip

			const int MaxTrayTextLen = 63 - 4;          // "\n...".Length == 4
			string updateTrayText = "";
			string errorTrayText = "";

			foreach (SvnItem folder in folders)
			{
				if (folder.Enabled && (folder.Status == SvnStatus.Error))
				{
					if (errorTrayText == "")
						errorTrayText = "Failed:";

					if ((MaxTrayTextLen - errorTrayText.Length) > (1 + folder.VisiblePath.Length))
					{
						errorTrayText += "\n" + folder.VisiblePath;
					}
					else if (errorTrayText != "")
					{
						errorTrayText += "\n...";
						break;
					}
				}
			}

			foreach (SvnItem folder in folders)
			{
				if ((folder.Status == SvnStatus.NeedUpdate) || (folder.Status == SvnStatus.NeedUpdate_Modified))
				{
					if (updateTrayText == "")
					{
						if ((MaxTrayTextLen - errorTrayText.Length) > 15)   // "\nUpdate needed:".Length == 15
							updateTrayText = (errorTrayText != "" ? "\n" : "") + "Update needed:";
						else
							break;
					}

					if ((MaxTrayTextLen - (updateTrayText.Length + errorTrayText.Length)) > (1 + folder.VisiblePath.Length))
					{
						updateTrayText += "\n" + folder.VisiblePath;
					}
					else if (updateTrayText != "")
					{
						updateTrayText += "\n...";
						break;
					}
				}
			}

			TrayNotifyIcon.Text = errorTrayText + updateTrayText;

			if (TrayNotifyIcon.Text == "")
				TrayNotifyIcon.Text = Application.ProductName;

			// Update tray icon

			Icon icon;
			if (folders.Any(x => x.Status == SvnStatus.Error))
			{
				icon = trayIcon_Error;
			}
			else if (folders.Any(x => x.Status == SvnStatus.NeedUpdate) ||
				folders.Any(x => x.Status == SvnStatus.NeedUpdate_Modified))
			{
				icon = trayIcon_NeedUpdate;
			}
			else if (folders.Any(x => x.Status == SvnStatus.Unknown))
			{
				icon = trayIcon_Unknown;
			}
			else if (folders.Any(x => x.Status == SvnStatus.UpToDate) ||
				folders.Any(x => x.Status == SvnStatus.UpToDate_Modified))
			{
				icon = trayIcon_UpToDate;
			}
			else
				icon = trayIcon_Unknown;

			if (icon != Icon)
				Icon = TrayNotifyIcon.Icon = icon;


			// Update menu

			if ((folders.Any(x => x.Status == SvnStatus.NeedUpdate)
				|| folders.Any(x => x.Status == SvnStatus.NeedUpdate_Modified))
				&& !Config.ChangeLogBeforeUpdate)
			{
				UpdateAllMenuItem.Enabled = true;
				TrayUpdateAllMenuItem.Enabled = true;
			}
			else
			{
				UpdateAllMenuItem.Enabled = false;
				TrayUpdateAllMenuItem.Enabled = false;
			}

			if (newNonUpdatedFoldersChanged)
			{
				// Update tray balloon
				if (newNonUpdatedFolders.Count > 0)
				{
					string[] nonUpdatedFolders = new string[newNonUpdatedFolders.Count];
					for (int i = 0; i < newNonUpdatedFolders.Count; i++)
						nonUpdatedFolders[i] = newNonUpdatedFolders[i].VisiblePath;
					firstBalloonPath = nonUpdatedFolders[0];
					string balloonMessage = String.Join(Environment.NewLine, nonUpdatedFolders);
					TrayNotifyIcon.ShowBalloonTip(Config.ShowBalloonInterval, Loc.Balloon_UpdateNeeded, balloonMessage, ToolTipIcon.Info);
				}
			}
		}


		private void ShowUpdateErrors(SvnFolderProcess sfp)
		{
			new UpdateLogForm(sfp).ShowDialog(this);
		}


		private void OnErrorAdded(string path, string error)
		{
			SafeInvoke(new UpdateErrorLogMethod(UpdateErrorLog), new object[] { path, error });
		}


		public void UpdateErrorLog(string path, string error)
		{
			WarningStatusLabel.DisplayStyle = ToolStripItemDisplayStyle.Image;
			if (path == null)
				path = "Checking for new version";                          // TODO: Strange realization
			string s = $"[{DateTime.Now}] {Environment.NewLine}{path}:{Environment.NewLine}{error.Replace("\n", Environment.NewLine)}{Environment.NewLine}{Environment.NewLine}";

			errorLog[path] = s;
		}


		private void WarningStatusLabel_Click(object sender, EventArgs e)
		{
			if ((errorLog.Count > 0) && (new ErrorLogForm(errorLog).ShowDialog(this) == DialogResult.Abort))
			{
				WarningStatusLabel.DisplayStyle = ToolStripItemDisplayStyle.None;
				errorLog.Clear();
			}
		}


		//////////////////////////////////////////////////////////////////////////////

		private const int WM_QUERYENDSESSION = 0x11;
		private const int WM_ENDSESSION = 0x16;

		private static bool sessionEndInProgress;


		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WM_QUERYENDSESSION)
				sessionEndInProgress = true;

			if ((m.Msg == WM_ENDSESSION) && ((int)m.WParam == 0))   // if "session end" was canceled (by some other reasons)
				sessionEndInProgress = false;

			base.WndProc(ref m);
		}

		//////////////////////////////////////////////////////////////////////////////

		[STAThread]
		private static void Main(string[] args)
		{
			// Load config early so font settings are available before any UI is created
			const string iniFileName = "SVN_Notifier.ini";
			Config.Init(File.Exists(iniFileName)
				? iniFileName
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), iniFileName));

			Application.SetDefaultFont(new System.Drawing.Font(Config.FontFamily, Config.FontSize));
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			try
			{
				if ((args.Length == 1) && (args[0] == "start"))     // Used to start application during install
				{
					ProcessStartInfo psi = new ProcessStartInfo
					{
						FileName = Application.ExecutablePath,
						UseShellExecute = false,
						WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath)
					};
					Process.Start(psi);
					return;
				}

				if (!Config.IsSettingsOK())
					if (new SettingsForm().ShowDialog() != DialogResult.OK)
						return;

				MainForm form = new MainForm();

				Thread.CurrentThread.Name = "Main";
				Application.ThreadException += OnThreadException;
				Application.Run(form);
			}
			catch (Exception e)
			{
				FatalError(e);
			}
		}


		private static void OnThreadException(object sender, ThreadExceptionEventArgs t)
		{
			FatalError(t.Exception);
			Application.Exit();
		}


		private static void FatalError(Exception e)
		{
			ShowError(e.ToString());
			Config.WriteLog("Error", e.ToString());
		}


		private static void ShowError(string s)
		{
			MessageBox.Show(s, "SVN Notifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void ItemListView_DragEnter(object sender, DragEventArgs e)
		{
			//only inforce accepting dropped files, rest is handled by parent class.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
		}

		private void ItemListView_DragOver(object sender, DragEventArgs e)
		{
			//only inforce accepting dropped files, rest is handled by parent class.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
		}

		private void ItemListView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
		{
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			var grid = (DataGridView)sender;
			var row = grid.Rows[e.RowIndex];
			var column = grid.Columns[e.ColumnIndex];
			var item = (SvnItem)row.DataBoundItem;
			if (column == StatusColumn)
			{
				e.Value = Loc.SvnStatusText(item.Status, item.Enabled);
				Color color = Color.Gray;
				switch (item.Status)
				{
					case SvnStatus.Unknown:
						color = Color.Gray;
						break;
					case SvnStatus.Error:
						color = Color.Gray;
						break;
					case SvnStatus.NeedUpdate:
						color = Color.Gray;
						break;
					case SvnStatus.NeedUpdate_Modified:
						color = Color.Gray;
						break;
					case SvnStatus.UpToDate:
						color = Color.Gray;
						break;
					case SvnStatus.UpToDate_Modified:
						color = Color.Gray;
						break;
					default:
						color = Color.Gray;
						break;
				}
				e.CellStyle.ForeColor = color;
			}
			if (column == ImageColumn)
			{
				if (item.Enabled)
				{
					switch (item.Status)
					{
						case SvnStatus.Unknown:
							e.Value = Properties.Resources.FolderStatus_Unknown;
							break;
						case SvnStatus.Error:
							e.Value = Properties.Resources.FolderStatus_Error;
							break;
						case SvnStatus.NeedUpdate:
							e.Value = Properties.Resources.FolderStatus_NeedUpdate;
							break;
						case SvnStatus.NeedUpdate_Modified:
							e.Value = Properties.Resources.FolderStatus_NeedUpdate_Modified;
							break;
						case SvnStatus.UpToDate:
							e.Value = Properties.Resources.FolderStatus_UpToDate;
							break;
						case SvnStatus.UpToDate_Modified:
							e.Value = Properties.Resources.FolderStatus_UpToDate_Modified;
							break;
						default:
							e.Value = Properties.Resources.FolderStatus_Unknown;
							break;
					}
				}
				else
				{
					e.Value = Properties.Resources.FolderStatus_Unknown;
				}

			}
			if (column == ColumnAutoUpdate)
			{
				// Show green checkmark if AutoUpdate is enabled
				e.Value = (bool?)e.Value == true ? "✓" : "";
				e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
				e.CellStyle.BackColor = Color.FromArgb(200, 240, 200);
				e.CellStyle.SelectionBackColor = Color.FromArgb(100, 200, 100);
				e.CellStyle.ForeColor = Color.FromArgb(34, 139, 34);  // Forest Green
				e.CellStyle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
			}
		}

		private void ItemListView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			var grid = (DataGridView)sender;
			var row = grid.Rows[e.RowIndex];
			var column = grid.Columns[e.ColumnIndex];
			// Save configuration when AutoUpdate checkbox changed
			if (column == ColumnAutoUpdate)
			{
				Config.SaveSvnFolders(folders);
			}
		}

		private void ItemListView_CellClick(object sender, DataGridViewCellEventArgs e)
		{
			// Toggle AutoUpdate when user clicks on the AutoUpdate column
			if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
			{
				var grid = (DataGridView)sender;
				var column = grid.Columns[e.ColumnIndex];
				if (column == ColumnAutoUpdate)
				{
					var row = grid.Rows[e.RowIndex];
					var item = (SvnItem)row.DataBoundItem;
					item.AutoUpdate = !item.AutoUpdate;
					Config.SaveSvnFolders(folders);
					grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
				}
			}
		}

		private void ItemListView_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
		{
			if (e.RowIndex < 0 || e.ColumnIndex < 0)
				return;
			var grid = (DataGridView)sender;
			var column = grid.Columns[e.ColumnIndex];
			
			if (column == ColumnAutoUpdate)
			{
				e.ToolTipText = Loc.Tooltip_AutoUpdate;
			}
		}

		public void ApplyLocalization()
		{
			// Dialogs
			MainFolderBrowserDialog.Description = Loc.Dlg_SelectSvnFolder;
			MainOpenFileDialog.Title = Loc.Dlg_SelectSvnFile;

			// Main menu
			FileMenuItem.Text      = Loc.Menu_File;
			AddFolderMenuItem.Text = Loc.Menu_AddFolder;
			AddFileMenuItem.Text   = Loc.Menu_AddFile;
			RemoveMenuItem.Text    = Loc.Menu_Remove;
			ImportConfigurationMenuItem.Text = Loc.Menu_ImportConfig;
			ExportConfigurationMenuItem.Text = Loc.Menu_ExportConfig;
			ExitMenuItem.Text      = Loc.Menu_Exit;
			ToolsMenuItem.Text     = Loc.Menu_Tools;
			UpdateAllMenuItem.Text = Loc.Menu_UpdateAll;
			SettingsMenuItem.Text  = Loc.Menu_Settings;
			HelpMenuItem.Text      = Loc.Menu_Help;
			AboutMenuItem.Text     = Loc.Menu_About;

			// Tray menu
			TrayShowProgramMenuItem.Text = Loc.Tray_ShowProgram;
			TrayUpdateAllMenuItem.Text   = Loc.Tray_UpdateAll;
			TrayExitMenuItem.Text        = Loc.Tray_Exit;

			// Toolbar
			AddFolderButton.Text  = Loc.Btn_AddFolder;
			AddFileButton.Text    = Loc.Btn_AddFile;
			RemoteButton.Text     = Loc.Btn_Remove;
			PropertiesButton.Text = Loc.Btn_Properties;
			UpdateButton.Text     = Loc.Btn_Update;
			ChangeLogButton.Text  = Loc.Btn_ChangeLog;
			LogButton.Text        = Loc.Btn_Log;
			OpenButton.Text       = Loc.Btn_Open;
			CommitButton.Text     = Loc.Btn_Commit;

			// Grid columns
			StatusColumn.HeaderText      = Loc.Col_Status;
			ColumnVisiblePath.HeaderText = Loc.Col_VisiblePath;
			PathColumn.HeaderText        = Loc.Col_Path;

			// Context menu
			CheckNowMenuItem.Text    = Loc.Ctx_CheckNow;
			ChangeLogMenuItem.Text   = Loc.Ctx_ChangeLog;
			UpdateMenuItem.Text      = Loc.Ctx_Update;
			CommitMenuItem.Text      = Loc.Ctx_Commit;
			OpenMenuItem.Text        = Loc.Ctx_Open;
			LogMenuItem.Text         = Loc.Ctx_Log;
			PropertiesMenuItem.Text  = Loc.Ctx_Properties;
		}
	}
}
