# SVN Notifier

Small and portable application which notifies you about other people's commits to Subversion repositories.

## Version 3.x (MGGM-Software GmbH)

**SVN Notifier 3.1.0.0** wird von [MGGM-Software GmbH](https://github.com/MGGM-Software-GmbH) gepflegt und erweitert.

### Neu in v3.1.0.0

Version `3.1.0.0` verwendet WinUI 3 und native Windows-App-Benachrichtigungen. Aktuelle Prüfergebnisse und offene Integrationsnachweise stehen im [Umsetzungsstand](docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md). Der [Merge- und Releaseablauf](docs/prp/winui-native-benachrichtigungen/04-merge-und-release.md) beschreibt Prüfungen und die Aktualisierung aus WinForms.

Hauptfenster, Einstellungen, Repository-Eigenschaften, Protokolle, Info und Programmupdate verwenden WinUI. Die Repository-Liste behält Prüfung und Auto-Update als getrennte Schalter. Systemfarben, Hell-/Dunkelmodus sowie die konfigurierbare Sprache und Schrift werden übernommen. Die Anzeigedauer nativer Banner wird in Windows eingestellt; die bisherigen INI-Werte bleiben erhalten. Der normale Programmstart erfolgt ohne Administratorrechte.

Die Anwendung setzt Windows 10 Version 1809 oder neuer voraus. Nach einem Verschieben des portablen Programmordners die Anwendung einmal regulär am neuen Ort starten. Neue Meldungen werden diesem Pfad zugeordnet; zuvor erzeugte Meldungen können auf den nicht mehr vorhandenen alten EXE-Pfad verweisen.

Unter **Einstellungen > Allgemein > Benachrichtigungen anzeigen** lassen sich neue Windows-Meldungen abschalten. Die Option ist standardmäßig eingeschaltet und wird als `ShowNotifications` in der INI gespeichert. SVN-Prüfung, Auto-Update, Tray-Symbol und manuelle Fehlerdialoge bleiben davon unabhängig. Bereits vorhandene Meldungen werden durch den Schalter nicht pauschal gelöscht.

### Neu in v3.0.0.13

- Beim Start und vor späteren SVN-Aufträgen werden verwaiste eigene Reservierungen automatisch freigegeben. Auch Reservierungen im alten Notifier-Format werden geprüft.
- Laufende Notifier- und SVN-Prozesse bleiben geschützt. Nach einem Absturz wird bei noch laufendem SVN-Prozess zunächst gewartet und später erneut geprüft. Prozessstartzeiten verhindern Verwechslungen bei wiederverwendeten Prozess-IDs.
- Beim Beenden werden eigene Reservierungen nach dem tatsächlichen Prozessende freigegeben. Die Freigabe bleibt auch bei Fehlern im Nachrichtenloop aktiv und verwendet ein gegen Dateiaustausch geschütztes Windows-Handle.
- Keine automatischen SVN-Cleanups, keine Änderungen an `wc.db` und keine Koordination externer SVN- oder Container-Aufrufe. Einzelheiten stehen in der SVN-Koordinationsdokumentation.

### Neu in v3.0.0.12

- Der Repository-Kontextmenüeintrag heißt in Deutsch und Englisch **Cleanup**.

### Neu in v3.0.0.11

- **Cleanup** im Repository-Kontextmenü öffnet den TortoiseSVN-Auswahldialog für den ausgewählten Eintrag, auch bei fehlerhaftem oder unbekanntem Status.
- Die Arbeitskopie bleibt bis zum Ende des Dialogprozesses reserviert. Die Bereinigung wird erst im TortoiseSVN-Dialog bestätigt; es gibt keine automatische Bereinigung.

### Neu in v3.0.0.10

- Vor der Installation eines bestätigten Programmupdates werden neue SVN-Aufträge gesperrt und die vom Notifier gestarteten SVN-Prozesse samt Unterprozessen beendet. Dies umfasst auch stille Updates und TortoiseSVN-Dialoge.
- Der Installer startet erst nach dem Ende der eigenen Prozesse und der Hintergrundprüfung. Ein fehlgeschlagener Installationsversuch gibt die SVN-Verarbeitung wieder frei. Extern gestartete SVN-Prozesse bleiben unberührt.

### Neu in v3.0.0.9

- Der optionale Python-Wrapper samt zugehörigen Tests und CI-Schritten ist entfernt. Python wird nicht benötigt.
- Die arbeitskopieweite Reservierung aller vom Notifier gestarteten SVN-Vorgänge bleibt erhalten, auch zwischen mehreren aktuellen Notifier-Instanzen. Extern gestartete SVN-, TortoiseSVN- und Container-Vorgänge werden nicht koordiniert.
- Manuelles Aktualisieren bei ausgeschaltetem Auto-Update, Benachrichtigungen und die korrigierte Pfadübergabe bleiben erhalten.

### Neu in v3.0.0.8

- SVN-Kommandozeilenaufrufe behandeln `@` im Arbeitskopiepfad als Dateinamenbestandteil. Auch relative Pfade mit führendem Minus werden als lokale Ziele übergeben.
- Eine Arbeitskopie mit abschließendem `@` wird unabhängig von einer danebenliegenden ähnlich benannten Arbeitskopie ohne `@` reserviert und aktualisiert.
- Regressionstests mit isolierten Arbeitskopien prüfen insbesondere, dass eine andere reservierte Arbeitskopie unverändert bleibt. Verhalten und Grenzen stehen in der SVN-Koordinationsdokumentation.

### Neu in v3.0.0.7

- Korrigierte Pfadübergabe an TortoiseSVN für manuelles Aktualisieren sowie Commit- und Protokolldialoge. Pfade mit Leerzeichen, Umlauten und abschließendem Verzeichnistrenner werden berücksichtigt.
- Manuelles Aktualisieren bleibt bei ausgeschaltetem Auto-Update nutzbar und verändert die Checkbox nicht. Die Reservierung der Arbeitskopie bleibt bis zum Prozessende bestehen.
- Regressionstests für beide Auto-Update-Zustände, verschiedene Pfadformen und ungültige Argumente. Die lokalen Prüfungen mit echtem TortoiseSVN verwenden ausschließlich isolierte Test-Arbeitskopien.

### Neu in v3.0.0.6

- Automatische SVN-Updates benötigen eine aktivierte Checkbox. Neue Einträge und ältere Einstellungen ohne Auto-Update-Feld beginnen ausgeschaltet. Ausdrücklich gespeicherte Werte bleiben erhalten.
- Alle vom Notifier gestarteten SVN-Prüfungen, Updates, Commits und Protokolldialoge reservieren die Arbeitskopiewurzel vor dem Prozessstart. Mehrere konfigurierte Unterordner teilen dieselbe Reservierung.
- Belegte Arbeitskopien werden ohne konkurrierenden Prozessstart übersprungen und frühestens nach 15 Sekunden erneut geprüft. Benachrichtigungen bleiben bei ausgeschaltetem Auto-Update nutzbar.
- Eine gemeinsame Reservierungsdatei verhindert überlappende Zugriffe mehrerer aktueller Notifier-Instanzen. Externe SVN- oder TortoiseSVN-Aufrufe werden nicht erfasst.
- Diagnose mit Arbeitskopie, Aktion, Prozess-ID, Beginn, Ende und Exit-Code ohne Befehlsargumente oder Zugangsdaten. Keine automatischen Cleanups und keine Änderungen an SVN-Datenbanken oder SQLite-Einstellungen.

Verhalten, Tests und Grenzen stehen in [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/master/docs/SVN-Koordination.md). Bereits gespeichert aktivierte Auto-Updates bleiben aktiv und sollten bei gemeinsam genutzten Arbeitskopien bewusst geprüft werden. Der Notifier verändert den TortoiseSVN-Statuscache nicht.

### Neu in v3.0.0.5

- Modernisierte Windows-Forms-Oberfläche mit einheitlichen Symbolen, beschrifteten Listenspalten und flachen Symbolleisten ohne zusätzliches UI-Toolkit.
- Kompakte Dialogschaltflächen und an Inhalt, Schriftgröße und Bildschirmauflösung angepasste Dialoge. Einstellungsseiten können bei Platzmangel gescrollt werden.
- Vorhandene Repository-Befehle, Kontextmenüs, Tray-Menüs und Programmupdates bleiben erhalten. Symbolleistenbefehle sind auch bei schmalen Fenstern über das Überlaufmenü erreichbar.
- GUI-Prüfungen für Deutsch und Englisch sowie 8,5, 12 und 24 Punkt große Schrift in den Windows-Builds für x64 und x86.

### Benutzeroberfläche

Die C#-Anwendung verwendet WinUI 3 mit Windows App SDK 2.6.5. Die Repository-Liste zeigt beschriftete Spalten für Prüfung, Status, Pfad und Auto-Update. Kompakte Zeilen wachsen bei größerer Schrift mit; dezente Trennlinien beanspruchen keine zusätzliche Zeilenhöhe. Menü und Symbolleiste sind in den Listenrahmen integriert, die Symbolleistenaktionen sind einheitlich neutral gestaltet. Fenster-, Taskleisten- und Tray-Symbol zeigen den gemeinsamen Gesamtstatus in Grau, Grün, Gelb oder Rot. Auch in schmalen Fenstern bleiben die Symbolleistenbefehle über das Überlaufmenü erreichbar.

Einstellungen, Repository-Eigenschaften und Protokolle verwenden dieselben vorhandenen Aktionen in neu angeordneten Dialogen. Schriftfamilie, Schriftgröße und Sprache bleiben konfigurierbar. Die Einstellungstabellen richten sich nach ihrem Inhalt; bei knappem Platz können die Einstellungsseiten gescrollt werden.

### Neu in v3.0.0.4

- Versionsprüfung für die Anwendung beim Start, anschließend alle zwölf Stunden und manuell unter **Hilfe > Nach Updates suchen**.
- Bestätigter Download der passenden x64- oder x86-Ausgabe mit SHA-256-Prüfung, Prüfung von ZIP-Inhalt, EXE-Version und Architektur sowie anschließendem Austausch und Neustart.
- Die automatische Prüfung lässt sich unter **Einstellungen > Allgemein > Automatisch nach Programmupdates suchen** abschalten. Die manuelle Prüfung bleibt verfügbar.
- Repository-Liste und INI-Einstellungen bleiben beim Programmupdate erhalten. SVN-Updates und Programmupdates sind getrennte Funktionen.

### Programmupdates

Der Updater verwendet die öffentliche GitHub-Adresse des neuesten stabilen Releases und dessen versionierte Download-Dateien. Er benötigt keine GitHub-Zugangsdaten. Bei einer neueren Version bietet er **Herunterladen und neu starten** sowie **Später** an. Während des Downloads kann abgebrochen werden. Netzwerk-, Integritäts- und Schreibfehler werden gemeldet; die Anwendung wird bei fehlgeschlagener Paketprüfung nicht ersetzt. Downgrades und Vorabversionen werden nicht installiert.

Die automatische Installation ist für die portablen ZIP-Ausgaben vorgesehen und benötigt Schreibzugriff auf den Programmordner. Mehrdatei-Entwicklungsbuilds werden nicht automatisch überschrieben. Nach erfolgreicher Downloadprüfung sperrt der Notifier neue SVN-Aufträge und beendet seine eigenen laufenden SVN-Prozesse samt Unterprozessen, einschließlich Schreibvorgängen und TortoiseSVN-Dialogen. Extern gestartete Vorgänge werden nicht beendet. Erst danach startet der Installer, wartet auf das Ende der Anwendung, prüft beide EXE-Dateien erneut und ersetzt die Anwendung atomar. Der Neustart behält das Arbeitsverzeichnis bei, damit auch portable INI-Dateien weiterverwendet werden. Nach dem Neustart werden die temporären Installationsdateien entfernt.

Die automatische Beendigung eigener SVN-Vorgänge steht ab 3.0.0.10 zur Verfügung. Beim Wechsel von einer älteren Ausgabe gilt noch deren Update-Verhalten: Laufende Notifier-SVN-Vorgänge vorher beenden oder die neue portable Ausgabe einmal manuell installieren.

Versionen bis einschließlich 3.0.0.3 enthalten diesen Programmupdater noch nicht. Für den Einstieg muss eine Ausgabe ab 3.0.0.4 einmal manuell heruntergeladen und entpackt werden.

### Neu in v3.0.0.3

- GitHub Actions baut die C#-Anwendung unter Windows bei Pushes auf `master`, Pull Requests und manuellen Workflow-Starts.
- Portable ZIP-Pakete für Windows x64 und x86 enthalten die .NET-10-Laufzeit, diese README und die Lizenz. Eine separate .NET-Installation ist nicht erforderlich.
- Versions-Tags erzeugen ein GitHub Release mit beiden Paketen und SHA-256-Prüfsummen. Die Anwendung selbst bleibt funktional unverändert.

### Download und Verwendung

Das zur Windows-Architektur passende ZIP-Paket unter [Releases](https://github.com/MGGM-Software-GmbH/SVNNotifier/releases) herunterladen, entpacken und `SVN_Notifier.exe` starten. Subversion-Kommandozeilenclient (`svn.exe`) und TortoiseSVN werden weiterhin separat benötigt und in den Einstellungen ausgewählt. Die erzeugten EXE-Dateien sind nicht digital signiert.

Die Prüfsumme des Downloads kann in PowerShell mit `Get-FileHash .\SVNNotifier-3.1.0.0-win-x64.zip -Algorithm SHA256` ermittelt und mit `SHA256SUMS.txt` im Release verglichen werden.

### Windows-Build und Veröffentlichung

Fehler vor dem Laden der Konfiguration werden unter `%TEMP%` als `SVNNotifier-startup-<Prozess-ID>-<Prozessstartticks>.log` protokolliert. Die Datei enthält Exceptiontyp, Fehlermeldung und Aufrufstapel. Nach geladener Konfiguration verwendet die Anwendung das zugehörige Protokoll neben der INI-Datei. INI-Inhalte und Befehlsargumente werden dafür nicht ausgegeben.

Voraussetzung für einen lokalen Build ist das .NET-10-SDK unter Windows. Im Repository-Hauptverzeichnis:

```powershell
dotnet build SVN_Notifier.sln --configuration Release
pwsh -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier win-x64
pwsh -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier win-x86
pwsh -File .github/scripts/Test-Gui.ps1 -RuntimeIdentifier win-x64
pwsh -File .github/scripts/Test-Gui.ps1 -RuntimeIdentifier win-x86
powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier win-x64
powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier win-x86
powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier win-x64
powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier win-x86
powershell -File .github/scripts/Test-StartModes.ps1 -RuntimeIdentifier win-x64 -ArtifactsDirectory artifacts
powershell -File .github/scripts/Test-StartModes.ps1 -RuntimeIdentifier win-x86 -ArtifactsDirectory artifacts
```

Die ZIP-Pakete und einzelnen Prüfsummendateien liegen unter `artifacts/packages/`. Mit `-ArtifactsDirectory` kann ein eigener Ausgabeordner gewählt werden. Für jeden Prüflauf muss dieser neue Installations-Testverzeichnisse erlauben. Der Workflow stellt die Pakete auch bei normalen Builds als herunterladbare Actions-Artefakte zur Verfügung und prüft den Anwendungsstart beider Architekturen. Die Updater-Prüfungen verwenden isolierte HTTP-Testdaten und separate Installationsordner. Sie prüfen auch den tatsächlichen EXE-Austausch mit Neustart und eine Manipulation zwischen Downloadprüfung und Installation.

Die gewöhnlichen GUI-Prüfungen können über `Invoke-QuietWindows.vbs` und `Invoke-IsolatedDesktop.ps1` ohne sichtbare Konsole auf einem inaktiven Windows-Desktop laufen. `Test-Gui.ps1 -IncludeShellSettings` ergänzt die tatsächliche Anwendung der Taskleisteneinstellung und benötigt einen Desktop mit Windows-Shell. Native Banner, Benachrichtigungszentrale und Eingabewege benötigen zusätzlich eine ausdrücklich erlaubte, entsperrte Windows-Testsitzung. Voraussetzungen und Aufrufparameter stehen in [Validierung](docs/prp/winui-native-benachrichtigungen/02-validierung.md).

Für ein Release müssen `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` in `SVN_Notifier/SVN_Notifier.csproj` übereinstimmen. Die Release-Hinweise stehen in `.github/RELEASE_NOTES.md`. Nach dem Merge auf `master` und erfolgreichen Prüfungen löst ein Tag im Format `v3.1.0.0` die Veröffentlichung aus:

```powershell
git tag v3.1.0.0
git push mggm v3.1.0.0
```

Der Release-Job läuft erst nach beiden erfolgreichen Windows-Builds, Startprüfungen, Updater-Tests, GUI-Prüfungen und den SVN-Koordinationsprüfungen unter Windows. Die GUI-Prüfungen öffnen die echten Dialoge auf Deutsch und Englisch mit 8,5, 12 und 24 Punkt. Sie prüfen Befehle, Repository-Schalter, Speichern und Verwerfen von Einstellungen, die Erreichbarkeit bei wenig Platz sowie Button- und Fensterhöhen. Gegenproben erkennen entfernte Befehle, nicht übernommene Optionen und übergroße Buttons. Alle Tests verwenden eigene Konfigurationsdateien und verändern keine Benutzer-Repositories.

Der Release-Job überprüft die Prüfsummen, lädt die Pakete zunächst in einen Release-Entwurf hoch und veröffentlicht diesen anschließend. Dafür genügt das automatische `GITHUB_TOKEN`; zusätzliche Repository-Secrets sind nicht erforderlich.

### What's new in v3.0.0.2

- **Auto-Update per repository** — new column in the repository list with a green ✓ indicator; when enabled, the repository is automatically updated silently in the background as soon as incoming changes are detected (no popup window)
- **Toast notification after auto-update** — a system tray balloon tip confirms each completed silent update, so you always know what happened
- Auto-Update wird je Eintrag in der INI-Konfiguration gespeichert und beim nächsten Start wiederhergestellt. Neue Einträge und ältere Einstellungen ohne Auto-Update-Feld beginnen ausgeschaltet.

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
