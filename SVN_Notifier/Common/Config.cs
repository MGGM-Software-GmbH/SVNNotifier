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
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CHD.SVN_Notifier
{
	public class Config
	{
		/// <summary>
		/// Path to svn.exe
		/// </summary>
		public static string SVNpath;


		/// <summary>
		/// Path to TortoiseProc.exe
		/// </summary>
		public static string TortoiseSVNpath;


		/// <summary>
		/// Status check interval (in seconds) when MainForm is activated.
		/// </summary>
		public static int DefaultActiveStatusUpdateInterval;


		/// <summary>
		/// Status check interval (in seconds) when MainForm is not activated.
		/// </summary>
		public static int DefaultIdleStatusUpdateInterval;


		/// <summary>
		/// Interval (in seconds) to suspend status check after application startup.
		/// </summary>
		public static int PauseAfterApplicationStartupInterval;


		/// <summary>
		/// Interval (in seconds) to suspend status check after Windows resume.
		/// </summary>
		public static int PauseAfterWindowsResumeInterval;

		/// <summary>
		/// Pause after startup is active
		/// </summary>
		public static bool DoPauseAfterApplicationStartup;

		/// <summary>
		/// Pause after Windows resume is active
		/// </summary>
		public static bool DoPauseAfterWindowsResume;

		/// <summary>
		/// ShowBalloon hide interval in milliseconds.
		/// </summary>
		public static int ShowBalloonInterval;


		/// <summary>
		/// Item double click action.
		/// </summary>
		public enum Action
		{
			openAction = 0,
			logAction = 1,
			updateAction = 2,
			commitAction = 3,
			checkNow = 4
		}


		public static Action ItemDoubleClickAction;

		/// <summary>
		/// Main window status on startup
		/// </summary>
		public static bool HideOnStartup;
		public static bool ShowInTaskbar;
		public static bool CheckForNewVersion;

		/// <summary>Steuert neue Windows-Benachrichtigungen unabhängig von Repository-Prüfung und Tray.</summary>
		public static bool ShowNotifications;


		/// <summary>
		/// Force to see Change Log before Update
		/// </summary>
		public static bool ChangeLogBeforeUpdate;


		/// <summary>
		/// Don't show the status during all updating
		/// </summary>
		public static bool UpdateAllSilently;


		/// <summary>
		/// Close the progress dialog at the end of a update command automatically.
		/// </summary>
		public static int UpdateWindowAction;

		/// <summary>
		/// UI language: 0 = English, 1 = German (Deutsch)
		/// </summary>
		public static int Language;

		/// <summary>
		/// Application font family name (e.g. "Microsoft Sans Serif")
		/// </summary>
		public static string FontFamily;

		/// <summary>
		/// Application font size in points
		/// </summary>
		public static float FontSize;

		/// <summary>
		/// Timeout in seconds for SVN command execution (0 = unlimited)
		/// </summary>
		public static int SvnCommandTimeout;
		internal const int MaximumSvnCommandTimeout = 86400;


		public static void Init(string fileName)
		{
			iniFileName = fileName;
			logFileName = Path.ChangeExtension(fileName, "log");
			Init();
		}


		public static void Init()
		{
			var source = new IniFile(iniFileName, false);
			MigrateIfNecessary(source);
			ApplySettings(source, ReadSettings(source));
		}

		/// <summary>Liest und validiert Einstellungen ohne Änderung des aktiven Konfigurationskontexts.</summary>
		private static System.Collections.Generic.Dictionary<string, object> ReadSettings(IniFile source)
		{
			var values = new System.Collections.Generic.Dictionary<string, object>();
			// Read settings

			values[nameof(SVNpath)] = source.ReadString("Settings", "SVN_path", defaultSVNpath);
			values[nameof(TortoiseSVNpath)] = source.ReadString("Settings", "TortoiseSVN_path", defaultTortoiseSVN_path);

			values[nameof(DefaultActiveStatusUpdateInterval)] = source.ReadInteger("Settings", "DefaultActiveStatusUpdateInterval", 5);
			values[nameof(DefaultIdleStatusUpdateInterval)] = source.ReadInteger("Settings", "DefaultIdleStatusUpdateInterval", 60);

			values[nameof(PauseAfterApplicationStartupInterval)] = source.ReadInteger("Settings", "PauseAfterApplicationStartupInterval", 15);
			values[nameof(DoPauseAfterApplicationStartup)] = source.ReadBoolean("Settings", "DoPauseAfterApplicationStartup", true);
			values[nameof(PauseAfterWindowsResumeInterval)] = source.ReadInteger("Settings", "PauseAfterWindowsResumeInterval", 15);
			values[nameof(DoPauseAfterWindowsResume)] = source.ReadBoolean("Settings", "DoPauseAfterWindowsResume", true);

			values[nameof(ItemDoubleClickAction)] = (Action)source.ReadInteger("Settings", "ItemDoubleClickAction", 0);
			values[nameof(ShowBalloonInterval)] = source.ReadInteger("Settings", "ShowBalloonInterval", 10000);
			values[nameof(HideOnStartup)] = source.ReadBoolean("Settings", "HideOnStartup", false);
			values[nameof(ShowInTaskbar)] = source.ReadBoolean("Settings", "ShowInTaskbar", false);
			values[nameof(CheckForNewVersion)] = source.ReadBoolean("Settings", "CheckForNewVersion", true);
			values[nameof(ShowNotifications)] = source.ReadBoolean("Settings", "ShowNotifications", true);
			values[nameof(UpdateAllSilently)] = source.ReadBoolean("Settings", "UpdateAllSilently", true);
			values[nameof(UpdateWindowAction)] = source.ReadInteger("Settings", "UpdateWindowAction", 2);

			values[nameof(ChangeLogBeforeUpdate)] = IsTortoiseVersion_1_5_orHigher() && source.ReadBoolean("Settings", "ChangeLogBeforeUpdate", false);

			values[nameof(Language)] = source.ReadInteger("Settings", "Language", 0);

			string timeoutText = source.ReadString("Settings", "SvnCommandTimeout", "60");
			if (!int.TryParse(timeoutText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int timeout)
				|| timeout < 0 || timeout > MaximumSvnCommandTimeout)
				throw new FormatException($"Ungültiges SVN-Zeitlimit: {timeoutText}. Zulässig sind 0 bis {MaximumSvnCommandTimeout} Sekunden.");
			values[nameof(SvnCommandTimeout)] = timeout;
			values[nameof(FontFamily)] = source.ReadString("Settings", "FontFamily", "Segoe UI");
			var fontSizeStr = source.ReadString("Settings", "FontSize", "8.5");
			values[nameof(FontSize)] = float.TryParse(fontSizeStr, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var fs) ? fs : 8.5f;
			foreach (string name in new[] { nameof(DefaultActiveStatusUpdateInterval), nameof(DefaultIdleStatusUpdateInterval),
				nameof(PauseAfterApplicationStartupInterval), nameof(PauseAfterWindowsResumeInterval) })
				ValidateInterval((int)values[name], name);
			return values;
		}

		/// <summary>Übernimmt für Import und Programmstart dieselben Intervallgrenzen wie die Eingabedialoge.</summary>
		internal static void ValidateInterval(int value, string name, bool allowDefault = false)
		{
			if (value < (allowDefault ? -1 : 0) || value > 359999)
				throw new FormatException($"Ungültiges Prüfintervall {name}: {value}. Zulässig sind {(allowDefault ? -1 : 0)} bis 359999 Sekunden.");
		}

		/// <summary>Übernimmt ausschließlich einen bereits validierten Konfigurationsstand.</summary>
		private static void ApplySettings(IniFile source, System.Collections.Generic.Dictionary<string, object> values)
		{
			foreach (var pair in values) typeof(Config).GetField(pair.Key)!.SetValue(null, pair.Value);
			iniFile = source;
			loadedSettings = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
		}


		/// <summary>Validiert den Import getrennt. Der Aufrufer muss neue SVN-Aufträge sperren und laufende Aktionen abwarten.</summary>
		public static void Import(string source)
		{
			string target = Path.GetFullPath(iniFileName);
			if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase)) return;
			string candidate = Path.Combine(Path.GetDirectoryName(target)!, ".svnnotifier-import-" + Guid.NewGuid().ToString("N") + ".ini");
			try
			{
				File.Copy(source, candidate);
				var imported = new IniFile(candidate, true);
				MigrateIfNecessary(imported);
				var values = ReadSettings(imported);
				ReadSvnFolders(imported);
				// Auch privater INI-Cache, Dateizuordnung und Fenstermaße gehören zum unveränderten aktiven Zustand.
				IniFile.WithFileLock(target, () =>
				{
					File.Move(candidate, target, true);
					ApplySettings(new IniFile(target, true), values);
				});
				formWidth = formHeight = 0;
			}
			finally
			{
				if (File.Exists(candidate)) File.Delete(candidate);
			}
		}

		/// <summary>Exportiert die gespeicherte Konfiguration einschließlich Repository-Zuordnungen ohne Änderung des aktiven Kontexts.</summary>
		public static void Export(string destination) => File.Copy(iniFileName, destination, true);

		public static bool IsSettingsOK()
		{
			return File.Exists(SVNpath) && File.Exists(TortoiseSVNpath);
		}


		/// <summary>Speichert nur fachlich geänderte Optionen, auch wenn deren Standardwerte zuvor nicht in der INI standen.</summary>
		public static void SaveSettings()
		{
			if (loadedSettings is null) throw new InvalidOperationException("Die Konfiguration ist nicht initialisiert.");
			var current = loadedSettings.Keys.ToDictionary(name => name, name => typeof(Config).GetField(name)!.GetValue(null));
			iniFile.Update(file =>
			{
				foreach (var pair in current)
				{
					if (Equals(loadedSettings[pair.Key], pair.Value)) continue;
					string key = pair.Key switch { nameof(SVNpath) => "SVN_path", nameof(TortoiseSVNpath) => "TortoiseSVN_path", _ => pair.Key };
					object? value = pair.Value is Enum enumeration ? Convert.ToInt32(enumeration) : pair.Value;
					file.Write("Settings", key, value is null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
				}
			});
			loadedSettings = current;
		}


		public static void MigrateIfNecessary() => MigrateIfNecessary(iniFile);

		private static void MigrateIfNecessary(IniFile source)
			=> source.Update(_ => MigrateCore(source));

		/// <summary>Migriert zusammengehörige INI-Werte innerhalb einer gemeinsamen Schreibtransaktion.</summary>
		private static void MigrateCore(IniFile source)
		{
			var iniVersion = source.ReadString("Settings", "Version");
			if (iniVersion == IniFileVersion) return;

			#region Migrate earlier ini-file versions to 1.5

			if (iniVersion == "")
			{
				for (int i = 1; ; i++)
				{
					string s = source.ReadString("Folders", "Folder" + i);
					if (s.Length == 0) break;

					string[] p = s.Split(',');

					if ((p.Length == 0) || (p.Length == 2) || (p.Length > 4))
						throw new ApplicationException("INI-file: Bad format for Folder" + i);

					//	For SVN_Notifier versions < 1.?.?
					if (p.Length == 1)
						s += ",-1,-1";

					//	For SVN_Notifier versions < 1.3.0
					if (p.Length <= 3)
						s += ",False";

					//	For SVN_Notifier versions < 1.5.0
					if (p.Length <= 4)
						s += "," + (File.Exists(p[0]) ? (int)PathType.File : (int)PathType.Directory);

					source.Write("Folders", "Folder" + i, s);
				}

				iniVersion = "1.5";
			}

			#endregion

			#region Migrate 1.5 to 1.7

			if (iniVersion == "1.5")
			{
				for (int i = 1; ; i++)
				{
					string s = source.ReadString("Folders", "Folder" + i);
					if (s.Length == 0) break;

					s = s.Replace(',', '|');
					source.Write("Folders", "Folder" + i, s);
				}

				iniVersion = "1.7";
			}

			#endregion

			#region Migrate 1.7 to 1.8

			if (iniVersion == "1.7")
			{
				// Fix misspelling: "ShowBallonInterval" -> "ShowBalloonInterval"
				var showBalloonInterval = source.ReadInteger("Settings", "ShowBallonInterval", 10000);
				source.Write("Settings", "ShowBalloonInterval", showBalloonInterval);
				source.DeleteKey("Settings", "ShowBallonInterval");

				// Switch to SVN command line client included in TortoiseSVN installation (if such installed)
				var tortoiseSvnPath = source.ReadString("Settings", "TortoiseSVN_path");
				if (tortoiseSvnPath.EndsWith("TortoiseProc.exe") && File.Exists(tortoiseSvnPath.Replace("TortoiseProc.exe", "svn.exe")))
					source.Write("Settings", "SVN_path", tortoiseSvnPath.Replace("TortoiseProc.exe", "svn.exe"));

				iniVersion = "1.8";
			}

			#endregion

			if (iniVersion == "1.8")
			{
				// Fix invalid configuration created by version 1.9.0
				var showBalloonInterval = source.ReadInteger("Settings", "ShowBalloonInterval", 10000);
				if (showBalloonInterval == 0)
					source.Write("Settings", "ShowBalloonInterval", 10000);

				iniVersion = "1.9";
			}

			if (iniVersion != IniFileVersion)
				throw new ApplicationException("INI-file migration code is incomplete");

			source.Write("Settings", "Version", IniFileVersion);
		}


		public static bool IsTortoiseVersion_1_5_orHigher()
		{
			return GetTortoiseVersion() >= new Version("1.5");
		}


		private static Version GetTortoiseVersion()
		{
			try
			{
				string s = (string)Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\TortoiseSVN").GetValue("CurrentVersion");
				return new Version(s.Replace(',', '.'));
			}
			catch
			{
				return new Version("0.0.0");
			}
		}


		public static BindingList<SvnItem> ReadSvnFolders() => ReadSvnFolders(iniFile);

		private static BindingList<SvnItem> ReadSvnFolders(IniFile source)
		{
			var folders = new BindingList<SvnItem>();
			for (int i = 1; ; i++)
			{
				string s = source.ReadString("Folders", "Folder" + i);
				if (s.Length == 0)
					break;
				folders.Add(SvnItem.Deserialize(s));
			}
			return folders;
		}


		public static void SaveSvnFolders(BindingList<SvnItem> folders)
		{
			// Unveränderte Listen dürfen beim Beenden keine Repository-Änderungen einer anderen Instanz zurücksetzen.
			var requested = folders.Select(folder => folder.Serialize()).OrderBy(value => value, StringComparer.Ordinal);
			if (ReadSvnFolders().Select(folder => folder.Serialize()).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(requested)) return;
			iniFile.Update(_ =>
			{
				iniFile.DeleteSection("Folders");

				int i = 1;
				foreach (SvnItem folder in folders)
				{
					iniFile.Write("Folders", "Folder" + i, folder.Serialize());
					i++;
				}
			});
		}

		public static void SaveMainFormSize(int width, int height)
		{
			if ((formWidth != width) || (formHeight != height))
			{
				formWidth = width;
				formHeight = height;
				iniFile.Update(file =>
				{
					file.Write("Settings", "MainFormWidth", width);
					file.Write("Settings", "MainFormHeight", height);
				});
			}
		}


		public static int[] ReadMainFormSize()
		{
			formWidth = iniFile.ReadInteger("Settings", "MainFormWidth", 0);
			formHeight = iniFile.ReadInteger("Settings", "MainFormHeight", 0);
			return new[] { formWidth, formHeight };
		}


		/// <summary>
		/// Write text logs to log-file
		/// </summary>
		/// <param name="section"></param>
		/// <param name="arguments"></param>
		public static void WriteLog(string section, string arguments)
		{
			lock (logSync)
				File.AppendAllText(logFileName, string.Format("{0}\t{1}\t{2}\r\n", DateTime.Now, section, arguments));
		}

		private static readonly object logSync = new();


		/// <summary>
		/// Path to SVN_Notifier.ini
		/// </summary>
		internal static string iniFileName;

		/// <summary>
		/// Path to log file
		/// </summary>
		internal static string logFileName;

		#region Private fields

		private const string defaultSVNpath = @"C:\Program Files\TortoiseSVN\bin\svn.exe";
		private const string defaultTortoiseSVN_path = @"C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe";

		private static IniFile iniFile;
		private static Dictionary<string, object?>? loadedSettings;

		private static int formWidth;
		private static int formHeight;

		/// <summary>
		/// Current version of ini-file format
		/// </summary>
		private const string IniFileVersion = "1.9";

		#endregion
	}
}
