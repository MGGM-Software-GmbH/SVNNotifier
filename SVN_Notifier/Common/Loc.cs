namespace CHD.SVN_Notifier
{
	/// <summary>
	/// Localization — all UI strings in English (default) and German.
	/// </summary>
	public static class Loc
	{
		private static bool IsDe => Config.Language == 1;

		private static string T(string en, string de) => IsDe ? de : en;

		// ----------------------------------------------------------------
		// MainForm — menus
		// ----------------------------------------------------------------
		public static string Menu_File           => T("&File",                         "&Datei");
		public static string Menu_AddFolder      => T("&Add Folder...",                "&Ordner hinzufügen...");
		public static string Menu_AddFile        => T("Add F&ile...",                  "Datei hinzufügen...");
		public static string Menu_Remove         => T("&Remove...",                    "&Entfernen...");
		public static string Menu_ImportConfig   => T("&Import Configuration...",      "Konfiguration &importieren...");
		public static string Menu_ExportConfig   => T("&Export Configuration...",      "Konfiguration &exportieren...");
		public static string Menu_Exit           => T("E&xit",                         "B&eenden");
		public static string Menu_Tools          => T("&Tools",                        "&Werkzeuge");
		public static string Menu_UpdateAll      => T("&Update All",                   "&Alle aktualisieren");
		public static string Menu_Settings       => T("&Settings...",                  "&Einstellungen...");
		public static string Menu_Help           => T("&Help",                         "&Hilfe");
		public static string Menu_CheckUpdates   => T("&Check for Updates",            "Nach &Updates suchen");

		// Programmupdates beziehen sich ausschließlich auf die Anwendung, nicht auf SVN-Repositories.
		public static string AppUpdate_Title => T("Application update", "Programmupdate");
		public static string AppUpdate_Checking => T("Checking for application updates...", "Programmversion wird geprüft...");
		public static string AppUpdate_Install => T("Download and restart", "Herunterladen und neu starten");
		public static string AppUpdate_Cancel => T("Cancel", "Abbrechen");
		public static string AppUpdate_Cancelled => T("Download canceled.", "Download abgebrochen.");
		public static string AppUpdate_NotInstalled => T("Update not installed.", "Update nicht installiert.");
		public static string AppUpdate_Later => T("Later", "Später");
		public static string AppUpdate_Notes => T("Release notes", "Versionshinweise");
		public static string AppUpdate_Downloading => T("Downloading update...", "Update wird heruntergeladen...");
		public static string AppUpdate_Verifying => T("Verifying update...", "Update wird geprüft...");
		public static string AppUpdate_Installing => T("Installing update...", "Update wird installiert...");
		public static string AppUpdate_PortableRequired => T("Automatic installation requires the portable ZIP release. Download and extract it from GitHub Releases.", "Die automatische Installation benötigt die portable ZIP-Ausgabe. Bitte diese aus den GitHub Releases herunterladen und entpacken.");
		public static string AppUpdate_RepositoryBusy => T("The application is already updating or shutting down.", "Ein Programmupdate oder das Beenden der Anwendung läuft bereits.");
		public static string AppUpdate_Current(string version) => T("Installed version: ", "Installierte Version: ") + version;
		public static string AppUpdate_Available(string version) => T("Available version: ", "Verfügbare Version: ") + version;
		public static string AppUpdate_Error(string message) => T("Application update failed.\n\n", "Das Programmupdate ist fehlgeschlagen.\n\n") + message;
		public static string AppUpdate_CheckError(string message) => T("Could not check for application updates.\n\n", "Programmupdates konnten nicht geprüft werden.\n\n") + message;
		public static string Menu_About          => T("&About SVN Notifier",           "&Über SVN Notifier");

		// Tray menu
		public static string Tray_ShowProgram    => T("Show Program",                  "Programm anzeigen");
		public static string Tray_UpdateAll      => T("Update All",                    "Alle aktualisieren");
		public static string Tray_Exit           => T("Exit",                          "Beenden");

		// Toolbar buttons
		public static string Btn_AddFolder       => T("Add Folder",                    "Ordner hinzufügen");
		public static string Btn_AddFile         => T("Add File",                      "Datei hinzufügen");
		public static string Btn_Remove          => T("Remove",                        "Entfernen");
		public static string Btn_Properties      => T("Properties",                    "Eigenschaften");
		public static string Btn_Update          => T("Update",                        "Aktualisieren");
		public static string Btn_ChangeLog       => T("Change Log",                    "Änderungsprotokoll");
		public static string Btn_Log             => T("Log",                           "Protokoll");
		public static string Btn_Open            => T("Open",                          "Öffnen");
		public static string Btn_Commit          => T("Commit",                        "Übertragen");

		// Grid column headers
		public static string Col_Status          => T("Status",                        "Status");
		public static string Col_Enabled         => T("Monitor",                       "Prüfen");
		public static string Col_VisiblePath     => T("Visible Path",                  "Angezeigter Pfad");
		public static string Col_Path            => T("Path",                          "Pfad");
		public static string Col_AutoUpdate      => T("Auto Update",                   "Auto-Update");
		public static string Tooltip_AutoUpdate  => T("Auto-update when changes are detected", "Automatisches Aktualisieren wenn Änderungen erkannt werden");
		public static string Tooltip_AutoUpdateState(bool enabled) => enabled
			? T("Automatic SVN updates enabled. Click to disable.", "Automatische SVN-Updates aktiv. Zum Ausschalten anklicken.")
			: T("Automatic SVN updates disabled. Click to enable.", "Automatische SVN-Updates ausgeschaltet. Zum Einschalten anklicken.");
		public static string Status_WaitingForOperations => T("Waiting for SVN operations to finish...", "Warte auf das Ende laufender SVN-Vorgänge...");
		public static string Svn_CoordinationError(string reason) => T("SVN coordination unavailable: ", "SVN-Koordination nicht möglich: ") + reason;
		public static string Svn_InvalidInfo => T("Invalid SVN info response.", "Ungültige SVN-Info-Antwort.");
		public static string Svn_Cancelled => T("SVN check cancelled or timed out.", "SVN-Prüfung abgebrochen oder Zeitlimit erreicht.");
		public static string Svn_ProcessError(int? exitCode) => T("SVN operation failed. Exit code: ", "SVN-Vorgang fehlgeschlagen. Exit-Code: ") + exitCode;
		public static string Toast_AutoUpdateCompleted(string path) => IsDe ? $"✓ Aktualisiert: {path}" : $"✓ Updated: {path}";

		// Context menu
		public static string Ctx_CheckNow        => T("Check Now...",                  "Jetzt prüfen...");
		public static string Ctx_ChangeLog       => T("Change Log...",                 "Änderungsprotokoll...");
		public static string Ctx_Update          => T("Update",                        "Aktualisieren");
		public static string Ctx_Commit          => T("Commit...",                     "Übertragen...");
		public static string Ctx_Cleanup         => T("SVN Cleanup...",                "SVN Cleanup...");
		public static string Ctx_Open            => T("Open...",                       "Öffnen...");
		public static string Ctx_Log             => T("Log...",                        "Protokoll...");
		public static string Ctx_Properties      => T("Properties",                    "Eigenschaften");

		// Dialogs
		public static string Dlg_SelectSvnFolder => T("Select folder controlled by Subversion",
		                                               "Ordner unter SVN-Kontrolle auswählen");
		public static string Dlg_SelectSvnFile   => T("Select file controlled by Subversion",
		                                               "Datei unter SVN-Kontrolle auswählen");

		// Status bar / status texts
		public static string Status_Updating(string path) => IsDe ? $"Aktualisiere '{path}'..." : $"Updating '{path}'...";
		public static string Status_UpdatingAll            => T("Updating all...",     "Alle aktualisieren...");
		public static string Status_Checking(string path) => IsDe ? $"Prüfe '{path}'..." : $"Checking '{path}'...";
		public static string Status_CheckingVersion        => T("Checking for new version...", "Nach neuer Version suchen...");

		// Balloon
		public static string Balloon_UpdateNeeded => T("Update needed",               "Update erforderlich");

		// MessageBoxes — MainForm
		public static string Msg_ChangeLogFirst  => T("You need to see ChangeLog first!",
		                                               "Zuerst das Änderungsprotokoll ansehen!");
		public static string Msg_FolderNotSvn    => T("This folder is not under SVN",
		                                               "Dieser Ordner steht nicht unter SVN-Kontrolle");
		public static string Msg_FileNotSvn      => T("This file is not under SVN",
		                                               "Diese Datei steht nicht unter SVN-Kontrolle");
		public static string Msg_ConfirmImport   => T("All current settings will be lost.\n\nDo you really want to change the settings?",
		                                               "Alle aktuellen Einstellungen gehen verloren.\n\nEinstellungen wirklich ändern?");
		public static string Msg_Attention       => T("Attention",                    "Hinweis");
		public static string Msg_NewVersion(string v) => IsDe
			? $"Neue stabile Version von SVN Notifier verfügbar - v{v}\nZur Projektwebseite wechseln?"
			: $"New stable version of SVN Notifier is available - v{v}\nDo you want to go to the project home page?";
		public static string Msg_LatestVersion   => T("You are using latest version of SVN Notifier.",
		                                               "Sie verwenden die neueste Version von SVN Notifier.");
		public static string Msg_CantCheckVersion => T("Can't check for new version!",
		                                               "Neue Version konnte nicht geprüft werden!");
		public static string Msg_ConfirmRemove(string path) => IsDe
			? $"Soll '{path}' wirklich aus der Liste entfernt werden?"
			: $"Are you sure to remove {path} from list?";
		public static string Msg_StatusThreadError(string e) => IsDe
			? $"Fehler im Status-Thread: {e}"
			: $"Error on status thread: {e}";
		public static string Msg_RestartRequired => T(
			"Some settings will take effect after restarting the application.",
			"Einige Einstellungen werden erst nach einem Neustart der Anwendung wirksam.");

		// ----------------------------------------------------------------
		// SVN Status strings
		// ----------------------------------------------------------------
		public static string SvnStatusText(SvnStatus status, bool enabled)
		{
			if (!enabled) return T("Disabled", "Deaktiviert");
			return status switch
			{
				SvnStatus.Unknown             => T("Unknown",           "Unbekannt"),
				SvnStatus.Error               => T("Error",             "Fehler"),
				SvnStatus.NeedUpdate          => T("Changed",           "Geändert"),
				SvnStatus.NeedUpdate_Modified => T("Changed, Modified", "Geändert, Bearbeitet"),
				SvnStatus.UpToDate            => T("Synchronized",      "Synchronisiert"),
				SvnStatus.UpToDate_Modified   => T("Modified",          "Bearbeitet"),
				_                             => T("Unknown",           "Unbekannt"),
			};
		}

		// ----------------------------------------------------------------
		// SettingsForm
		// ----------------------------------------------------------------
		public static string SF_Title            => T("Settings",                      "Einstellungen");
		public static string SF_Tab_General      => T("General",                       "Allgemein");
		public static string SF_Tab_Status       => T("Status",                        "Status");
		public static string SF_Tab_Update       => T("Update",                        "Aktualisieren");
		public static string SF_Group_Locations  => T("Locations",                     "Speicherorte");
		public static string SF_Label_SvnPath    => T("Path to svn.exe:",              "Pfad zu svn.exe:");
		public static string SF_Label_TortoisePath => T("Path to TortoiseProc.exe:",   "Pfad zu TortoiseProc.exe:");
		public static string SF_Btn_Browse       => T("Browse...",                     "Durchsuchen...");
		public static string SF_Group_Other      => T("Other Options",                 "Weitere Optionen");
		public static string SF_Label_DblClick   => T("On double click:",              "Bei Doppelklick:");
		public static string SF_DblClick_Open    => T("Open folder",                   "Ordner öffnen");
		public static string SF_DblClick_Log     => T("Show change/full log",          "Änderungs-/Protokoll anzeigen");
		public static string SF_DblClick_Update  => T("Update",                        "Aktualisieren");
		public static string SF_DblClick_Commit  => T("Commit",                        "Übertragen");
		public static string SF_DblClick_Check   => T("CheckNow",                      "Jetzt prüfen");
		public static string SF_Label_HideTray   => T("Hide tray notification after:", "Tray-Hinweis ausblenden nach:");
		public static string SF_Chk_HideOnStart  => T("Hide program to system tray on startup",
		                                               "Beim Start in die Taskleiste minimieren");
		public static string SF_Chk_ShowTaskbar  => T("Show in taskbar",               "In Taskleiste anzeigen");
		public static string SF_Chk_CheckVersion => T("Check for application updates automatically", "Automatisch nach Programmupdates suchen");
		public static string SF_Chk_ChangeLog    => T("Force to see \"Change Log\" before Update",
		                                               "\"Änderungsprotokoll\" vor Update erzwingen");
		public static string SF_Chk_SilentUpdate => T("\"Silent\" Update All",         "\"Stilles\" Alle aktualisieren");
		public static string SF_Label_Language   => T("Language:",                     "Sprache:");
		public static string SF_Label_Font       => T("Font:",                         "Schriftart:");
		public static string SF_Label_FontSize   => T("Font size:",                    "Schriftgröße:");
		public static string SF_Label_SvnTimeout => T("Status check timeout:",              "Statusprüfungs-Timeout:");
		public static string SF_Label_SvnTimeoutSec => T("sec.",                              "Sek.");
		public static string SF_Group_Status     => T("Default check intervals",      "Standard-Prüfintervalle");
		public static string SF_Label_Active     => T("Window active:",               "Fenster aktiv:");
		public static string SF_Label_Inactive   => T("Window inactive:",             "Fenster nicht aktiv:");
		public static string SF_Label_Hours      => T("Hours",                         "Stunden");
		public static string SF_Label_Minutes    => T("Minutes",                       "Minuten");
		public static string SF_Label_Seconds    => T("Seconds",                       "Sekunden");
		public static string SF_Group_Pause      => T("Pause checks",                 "Prüfung pausieren");
		public static string SF_Chk_Resume       => T("After Windows resumes",        "Nach Windows-Fortsetzung");
		public static string SF_Chk_Startup      => T("After application startup",    "Nach dem Programmstart");
		public static string SF_Label_DialogAction => T("TortoiseSVN dialog action after update:",
		                                                 "TortoiseSVN-Dialog-Aktion nach Update:");
		public static string SF_Label_RequiresTortoise => T("(Requires TortoiseSVN v1.5 or higher)",
		                                                     "(Benötigt TortoiseSVN v1.5 oder höher)");
		public static string SF_Btn_OK           => T("&OK",                           "&OK");
		public static string SF_Btn_Cancel       => T("Cancel",                        "Abbrechen");

		// ----------------------------------------------------------------
		// PropertiesForm
		// ----------------------------------------------------------------
		public static string PF_Title            => T("Properties",                    "Eigenschaften");
		public static string PF_Label_Path       => T("Path:",                         "Pfad:");
		public static string PF_Chk_Enable       => T("Enable checking",               "Prüfung aktivieren");
		public static string PF_Chk_CustomInterval => T("Custom check intervals",      "Eigene Prüfintervalle");
		public static string PF_Label_Active     => T("Window active:",               "Fenster aktiv:");
		public static string PF_Label_Inactive   => T("Window inactive:",             "Fenster nicht aktiv:");
		public static string PF_Label_Hours      => T("Hours",                         "Stunden");
		public static string PF_Label_Minutes    => T("Minutes",                       "Minuten");
		public static string PF_Label_Seconds    => T("Seconds",                       "Sekunden");
		public static string PF_Btn_OK           => T("OK",                            "OK");
		public static string PF_Btn_Cancel       => T("Cancel",                        "Abbrechen");
		public static string PF_FolderDialog     => T("Select folder controlled by Subversion",
		                                               "Ordner unter SVN-Kontrolle auswählen");

		// ----------------------------------------------------------------
		// ErrorLogForm
		// ----------------------------------------------------------------
		public static string ELF_Btn_Clear       => T("Clear",                         "Leeren");
		public static string ELF_Btn_Close       => T("Close",                         "Schließen");
		public static string ELF_Title           => T("Error log",                    "Fehlerprotokoll");

		// ----------------------------------------------------------------
		// UpdateLogForm
		// ----------------------------------------------------------------
		public static string ULF_Title           => T("Update Log",                    "Aktualisierungsprotokoll");
		public static string ULF_Btn_ShowLog     => T("Show log...",                   "Protokoll anzeigen...");
		public static string ULF_Btn_OK          => T("Ok",                            "OK");
		public static string ULF_Btn_Cancel      => T("Cancel",                        "Abbrechen");
		public static string ULF_Col_Action      => T("Action",                        "Aktion");
		public static string ULF_Col_Path        => T("Path",                          "Pfad");

		// ----------------------------------------------------------------
		// AboutForm
		public static string AF_Title            => T("About SVN Notifier",           "Über SVN Notifier");
		// ----------------------------------------------------------------
		public static string AF_Description      => T("SVN Notifier is Open Source Software released under the GNU General Public License v3.",
		                                               "SVN Notifier ist Open-Source-Software unter der GNU General Public License v3.");
	}
}
