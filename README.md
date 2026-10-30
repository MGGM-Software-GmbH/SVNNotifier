# SVN Notifier

Small and portable application which notifies you about other people's commits to Subversion repositories.

## Version 3.x (MGGM-Software GmbH)

**SVN Notifier 3.0.0.4** wird von [MGGM-Software GmbH](https://github.com/MGGM-Software-GmbH) gepflegt und erweitert.

### Neu in v3.0.0.4

- Versionsprüfung für die Anwendung beim Start, anschließend alle zwölf Stunden und manuell unter **Hilfe > Nach Updates suchen**.
- Bestätigter Download der passenden x64- oder x86-Ausgabe mit SHA-256-Prüfung, Prüfung von ZIP-Inhalt, EXE-Version und Architektur sowie anschließendem Austausch und Neustart.
- Die automatische Prüfung lässt sich unter **Einstellungen > Allgemein > Automatisch nach Programmupdates suchen** abschalten. Die manuelle Prüfung bleibt verfügbar.
- Repository-Liste und INI-Einstellungen bleiben beim Programmupdate erhalten. SVN-Updates und Programmupdates sind getrennte Funktionen.

### Programmupdates

Der Updater verwendet die öffentliche GitHub-Adresse des neuesten stabilen Releases und dessen versionierte Download-Dateien. Er benötigt keine GitHub-Zugangsdaten. Bei einer neueren Version bietet er **Herunterladen und neu starten** sowie **Später** an. Während des Downloads kann abgebrochen werden. Netzwerk-, Integritäts- und Schreibfehler werden gemeldet; die Anwendung wird bei fehlgeschlagener Paketprüfung nicht ersetzt. Downgrades und Vorabversionen werden nicht installiert.

Die automatische Installation ist für die portablen ZIP-Ausgaben vorgesehen und benötigt Schreibzugriff auf den Programmordner. Mehrdatei-Entwicklungsbuilds werden nicht automatisch überschrieben. Laufende SVN-Vorgänge müssen vor der Installation beendet sein. Der Installer wartet auf das Ende der Anwendung, prüft beide EXE-Dateien erneut und ersetzt die Anwendung atomar. Der Neustart behält das Arbeitsverzeichnis bei, damit auch portable INI-Dateien weiterverwendet werden. Nach dem Neustart werden die temporären Installationsdateien entfernt.

Versionen bis einschließlich 3.0.0.3 enthalten diesen Programmupdater noch nicht. Für den Einstieg muss eine Ausgabe ab 3.0.0.4 einmal manuell heruntergeladen und entpackt werden.

### Neu in v3.0.0.3

- GitHub Actions baut die C#-Anwendung unter Windows bei Pushes auf `master`, Pull Requests und manuellen Workflow-Starts.
- Portable ZIP-Pakete für Windows x64 und x86 enthalten die .NET-10-Laufzeit, diese README und die Lizenz. Eine separate .NET-Installation ist nicht erforderlich.
- Versions-Tags erzeugen ein GitHub Release mit beiden Paketen und SHA-256-Prüfsummen. Die Anwendung selbst bleibt funktional unverändert.

### Download und Verwendung

Das zur Windows-Architektur passende ZIP-Paket unter [Releases](https://github.com/MGGM-Software-GmbH/SVNNotifier/releases) herunterladen, entpacken und `SVN_Notifier.exe` starten. Subversion-Kommandozeilenclient (`svn.exe`) und TortoiseSVN werden weiterhin separat benötigt und in den Einstellungen ausgewählt. Die erzeugten EXE-Dateien sind nicht digital signiert.

Die Prüfsumme des Downloads kann in PowerShell mit `Get-FileHash .\SVNNotifier-3.0.0.4-win-x64.zip -Algorithm SHA256` ermittelt und mit `SHA256SUMS.txt` im Release verglichen werden.

### Windows-Build und Veröffentlichung

Voraussetzung für einen lokalen Build ist das .NET-10-SDK unter Windows. Im Repository-Hauptverzeichnis:

```powershell
dotnet build SVN_Notifier.sln --configuration Release
pwsh -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier win-x64
pwsh -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier win-x86
powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier win-x64
powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier win-x86
```

Die ZIP-Pakete und einzelnen Prüfsummendateien liegen unter `artifacts/packages/`. Mit `-ArtifactsDirectory` kann ein eigener Ausgabeordner gewählt werden. Für jeden Prüflauf muss dieser neue Installations-Testverzeichnisse erlauben. Der Workflow stellt die Pakete auch bei normalen Builds als herunterladbare Actions-Artefakte zur Verfügung und prüft den Anwendungsstart beider Architekturen. Die Updater-Prüfungen verwenden isolierte HTTP-Testdaten und separate Installationsordner. Sie prüfen auch den tatsächlichen EXE-Austausch mit Neustart und eine Manipulation zwischen Downloadprüfung und Installation.

Für ein Release müssen `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` in `SVN_Notifier/SVN_Notifier.csproj` übereinstimmen. Die Release-Hinweise stehen in `.github/RELEASE_NOTES.md`. Nach einem erfolgreichen Branch-Build löst ein Tag im Format `v3.0.0.4` die Veröffentlichung aus:

```powershell
git tag v3.0.0.4
git push mggm v3.0.0.4
```

Der Release-Job läuft erst nach beiden erfolgreichen Windows-Builds, Startprüfungen und Updater-Tests. Er überprüft die Prüfsummen, lädt die Pakete zunächst in einen Release-Entwurf hoch und veröffentlicht diesen anschließend. Dafür genügt das automatische `GITHUB_TOKEN`; zusätzliche Repository-Secrets sind nicht erforderlich.

### What's new in v3.0.0.2

- **Auto-Update per repository** — new column in the repository list with a green ✓ indicator; when enabled, the repository is automatically updated silently in the background as soon as incoming changes are detected (no popup window)
- **Toast notification after auto-update** — a system tray balloon tip confirms each completed silent update, so you always know what happened
- Auto-Update state is **persisted** in the INI configuration and restored on next launch (default: enabled)

### What's new in v3.0.0.1

- **Migrated to .NET 10** (net10.0-windows) with SDK-style project format
- **Multilingual UI** — complete English and German localisation; switchable at runtime via Settings
- **Configurable font** — font family and size selectable in Settings (default: Segoe UI 8.5 pt)
- **Windows Explorer sort order** for repository list (uses `StrCmpLogicalW`)
- **Configurable status check timeout** — prevents hanging on unreachable servers during status polling (default: 60 s, 0 = unlimited; does not affect Update/Commit)
- Removed all Win32 P/Invoke from `IniFile.cs` — replaced with fully managed INI reader/writer
- Replaced deprecated `XmlTextReader` + `Hashtable` with `XDocument` / LINQ to XML
- Replaced non-generic collections (`Queue`, `Hashtable`) with `ConcurrentQueue<T>`, `Dictionary<K,V>`
- Removed obsolete `Thread.Abort()` usage (not supported in .NET 5+)
- Fixed `Process.Start()` calls for .NET 8+ (`UseShellExecute = true`)

### Source

Fork of [JocysCom/SVNNotifier](https://github.com/JocysCom/SVNNotifier):
https://github.com/MGGM-Software-GmbH/SVNNotifier

---

## Version 2.x (JocysCom)

**SVN Notifier 2.1.7.0 (2019-08-31)**

Improved old interface and refactored the code.

## Original Source and Developers (v1)

  http://svnnotifier.tigris.org

  Developers: Vlad Rudenko, Aleksej Vaschenko, Nikolaj Nahimov.

## Description

SVN Notifier is a simple and useful tool to monitor your Subversion project repositories for changes. SVN Notifier notifies you about other people's recent commits to repository and helps you keep your local copy up to date. You can review all the changes and update your local copy right from the application. This tool greatly helps agile teams with continuous integration.

SVN Notifier is tightly integrated with TortoiseSVN. It implements "monitoring/notification" feature only (that is missing in TortoiseSVN) and thus has very simple user interface. All TortoiseSVN features are easy accessible by double clicking on monitored item.

This software has been developed for internal use at CHD Ltd (www.chd.lv). But as it is not related to company's main business (retail solutions, cash registers, etc.) the company gave permission to original developers to release SVN Notifier under GNU License to support the open source community.

SVN Notifier is free software for both personal and commercial use.

## Screenshots

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Files.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Files.png)

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Tray.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Tray.png) ![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Tray_Notification.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Tray_Notification.png)


![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier.png)

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Menu.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Menu.png)

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_General.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_General.png)

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_Status.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_Status.png)

![https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_Update.png](https://raw.githubusercontent.com/JocysCom/SVNNotifier/master/SVN_Notifier/Documents/Screenshots/SVN_Notifier_Settings_Update.png)
