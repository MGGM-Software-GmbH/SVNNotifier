# PRP: WinUI 3 und native Windows-App-Benachrichtigungen

## Geplante Umsetzung in einfachen Worten

> **Lesehilfe für den Entwickler:** Dieser Block dient ausschließlich der Prüfung der geplanten Umsetzung. Er ist kein zusätzlicher Ausführungsinhalt für den KI-Agenten. Anforderungen, Aufgaben und Entscheidungen sind ausschließlich den folgenden PRP-Abschnitten zu entnehmen.

Der SVN Notifier erhält eine mit WinUI 3 gestaltete Windows-Oberfläche und moderne Windows-App-Benachrichtigungen. Repository-Liste, Tray-Bedienung, Einstellungen, Protokolle, SVN-Aufträge und Programmupdates behalten ihre Funktionen. Die bestehende SVN-Koordination und Konfiguration werden weiterverwendet. Eine Funktionsmatrix und Prüfungen gegen den Ausgangsstand verhindern, dass beim Umbau Bedienwege oder Schutzmechanismen entfallen. Die Entwicklung erfolgt auf einem eigenen Branch; eine spätere Zusammenführung und Veröffentlichung entscheidet der Benutzer.

## Ziel

Die vorhandene C#-Anwendung mit .NET 10 von Windows Forms auf WinUI 3 umstellen und die drei bisherigen Tray-Benachrichtigungsauslöser an Windows-App-Benachrichtigungen anbinden. Gestaltung, Funktionsumfang, portable Verteilung und Betriebssicherheit sind gleichrangige Anforderungen.

**Auftragsgrenze dieses Dokuments:** Die Implementierung auf `feature/winui-native-notifications` ist vom Benutzer beauftragt. Funktionserhalt, Gestaltung und die beschriebenen Prüfungen gehören zur Umsetzung. Merge, Release, Installation beim Benutzer und Veröffentlichung von Anwendungspaketen benötigen weiterhin einen ausdrücklichen Auftrag.

### Dokumente und Lesereihenfolge

1. Dieses PRP: Ziel, Architektur, Gestaltung, Reihenfolge und Freigabebedingungen.
2. [Funktionsparität](01-funktionsparitaet.md): verbindlicher Funktionsbestand mit Quellen und Prüfzielen.
3. [Validierung](02-validierung.md): Ausgangsmessungen, Regressionen, Gegenproben, Sichtprüfung und Merge-Nachweise.
4. [Windows-App-SDK-Integrationshinweise](../ai_docs/windows-app-sdk-integration.md): geprüfte API- und Auslieferungspunkte sowie offene Laufzeitnachweise.

### Branch und Ausgangsstand

- Hauptbranch bei Erstellung: `master`; nicht zusätzlich `main` anlegen oder Branches umbenennen.
- Ausgangscommit: `3df587de4a2a0bf1396b3d85ed5d1da95c48cf3a`.
- Eigener Arbeitsbranch: `feature/winui-native-notifications`, vom genannten Ausgangscommit abgezweigt.
- Programmversion im Ausgangsprojekt: `3.0.0.13`.
- Vorhandene Benutzeränderungen bleiben erhalten und gehören nicht in den PRP-Commit. Insbesondere ist `SVN_Notifier/Common/WindowsPathComparer.cs` im Arbeitsbaum geändert; diese Änderung weder zurücksetzen noch ungeprüft überschreiben. Der Commit allein beschreibt deshalb nicht den gesamten aktuellen Arbeitsbaum.
- Vor einer späteren Umsetzung den tatsächlichen Arbeitsstand erneut prüfen und eine reproduzierbare Baseline sichern. Keine fremden Änderungen pauschal stagen oder in Testsicherungskopien verstecken.
- Umstellungsänderungen ausschließlich auf diesem Featurebranch. Kleine lokale Checkpoints; kein SVN-Check-in, Push, Merge, Tag oder Release ohne ausdrücklichen Auftrag.
- Eine spätere Zusammenführung zielt auf den dann tatsächlich verwendeten Hauptbranch. Änderungen des Hauptbranches vor dem Merge berücksichtigen und betroffene Paritätsprüfungen erneut ausführen.

### Geprüfter Git-Push aus WSL

Für dieses Repository wurde der Push aus WSL über das vorhandene Windows-Git erfolgreich ausgeführt. Bei einem ausdrücklich beauftragten Push diesen geprüften Weg verwenden:

```bash
GIT_TERMINAL_PROMPT=0 '/mnt/c/Program Files/Git/cmd/git.exe' -c credential.interactive=false push --set-upstream mggm feature/winui-native-notifications
```

Anschließend den veröffentlichten Commit prüfen:

```bash
GIT_TERMINAL_PROMPT=0 '/mnt/c/Program Files/Git/cmd/git.exe' -c credential.interactive=false ls-remote --heads mggm refs/heads/feature/winui-native-notifications
```

Nachweis vom 12. November 2026: Push mit Exit-Code 0, neuer Remote-Branch und eingerichteter Upstream-Verknüpfung. Die anschließende Remote-Abfrage lieferte den PRP-Commit `11a1e804ce4fdd75fb8338e69fedc5a833776503`.

Die in dieser Sitzung geprüften GitHub-App-Schreibaufrufe wurden mit HTTP 403 und `Resource not accessible by integration` abgewiesen. Dieser Befund erlaubt keine pauschale Aussage über die Schreibrechte anderer Sitzungen oder des Benutzers. Der erfolgreiche Windows-Git-Push ist für die hier vorhandene Umgebung nachgewiesen; auf anderen Rechnern muss der Zugriff erneut geprüft werden. Keine Zugangsdaten, Tokens oder sonstigen Authentifizierungsdaten in diesem PRP oder in Befehlen hinterlegen.

## Warum

- Neue SVN-Änderungen sollen auch nach dem Verschwinden eines Banners gezielt in der Windows-Benachrichtigungszentrale erreichbar sein.
- Hauptfenster und Dialoge sollen eine gemeinsame Fluent-Gestaltung mit Systemfarben, Hell-/Dunkelmodus, Kontrastdarstellung und konsistenten Interaktionszuständen erhalten.
- Die Anwendung bleibt ein kompaktes Werkzeug für Repository-Überwachung und SVN-Aktionen. Die Repository-Liste und häufige Aktionen stehen im Vordergrund.
- Die bereits implementierten Regeln zu Auto-Update, Arbeitskopiereservierung, Prozessende und sicherem Programmupdate müssen erhalten bleiben.

## Was

### Verbindlicher Umfang

- Alle Funktionen der [Funktionsparitätsmatrix](01-funktionsparitaet.md), auch solche ohne bisherigen automatisierten Test, erhalten.
- Sieben vorhandene Fenster bzw. Dialoge fachlich abbilden: Hauptfenster, Einstellungen, Repository-Eigenschaften, Fehlerprotokoll, Updateprotokoll, Info und Programmupdate.
- Die drei Benachrichtigungsarten erhalten: eingehende Änderungen, erfolgreiches stilles Auto-Update und Fehler der automatischen Programmupdate-Prüfung.
- Deutsch und Englisch, Schriftfamilie und Schriftgröße, Tastatur, Drag-and-drop, Kontextmenüs, Tray-Bedienung und alle vorhandenen Einstellungen erhalten.
- Portable Pakete für `win-x64` und `win-x86` mit enthaltenen Laufzeitabhängigkeiten und unverändertem EXE-Namen `SVN_Notifier.exe` liefern.
- Bestehende Benutzerkonfigurationen einschließlich INI-Migrationen, Import/Export und Repository-Feldern weiterverwenden.
- Aus allen neuen Bedienwegen dieselbe SVN-Koordination verwenden. Kein zusätzlicher unkoordinierter Prozessstart aus XAML, Tray oder Benachrichtigungsaktivierung.
- Bestehende Tests mit gleicher fachlicher Aussagekraft migrieren und erkannte Abdeckungslücken schließen. Frameworkabhängige Assertions ersetzen, keine Szenarien entfernen, um grüne Tests zu erhalten.

### Nicht beauftragt

Keine Delphi-Umsetzung, kein Webfrontend, kein zusätzliches Fachmodul, keine Datenbankänderung, kein Wechsel auf MSIX oder einen verpflichtenden Installer, keine neuen globalen SVN-/Container-Sperren und keine automatische SVN-Bereinigung. Vorhandene unbeteiligte Dateien und Entwicklungsversuche nicht bereinigen.

### Erfolgskriterien

- [ ] Jede Zeile der Paritätsmatrix ist durch einen aktuellen Nachweis erfüllt; kein ungeklärter Funktionsverlust.
- [ ] Alle sieben Ansichten sind in WinUI 3 nutzbar und anhand des UI-Zielbilds visuell geprüft.
- [ ] Die drei Meldungsarten werden als native Windows-App-Benachrichtigungen versendet und ihr Klickziel bleibt eindeutig.
- [ ] Mehrere laufende Instanzen, Windows-Sitzungsende und normale Programmbeendigung behalten ihre definierten Abläufe.
- [ ] x64 und x86 laufen aus dem entpackten Release-Paket auch ohne vorinstallierte .NET-/Windows-App-SDK-Laufzeit.
- [ ] Ein tatsächliches Update vom WinForms-Ausgangspaket auf das WinUI-Paket tauscht die EXE aus, startet neu und erhält die verwendete Konfiguration.
- [ ] SVN-Koordination, Prozessabbruch, Wiederherstellung verwaister Reservierungen und sichere Freigabe sind durch Regressionen belegt.
- [ ] Die Gestaltung ist bei den festgelegten Sprach-, Schrift-, Theme- und Größenvarianten geprüft; gefundene Mängel sind korrigiert.
- [ ] Änderungen an Anforderungen oder Funktionsumfang sind nicht durch eine Implementierungsannahme ersetzt worden.

### Kompatibilitätspunkte vor dem breiten Umbau

Diese Punkte sind erforderliche Untersuchungen, keine bereits bestätigten Eigenschaften einer WinUI-Implementierung:

| ID | Frage und verbindlicher Umgang |
|---|---|
| K-01 | Benutzerentscheidung: Windows steuert die Banneranzeigedauer. Die bisherige Sekunden-Einstellung wird in der WinUI-Oberfläche durch einen Zugang zu den Windows-Benachrichtigungseinstellungen ersetzt. Vorhandene INI-Werte einschließlich Migrationsregeln bleiben erhalten; keine Umdeutung als Ablaufzeit der Benachrichtigungszentrale. Die Wirkung der Windows-Einstellungen getrennt von der Historie prüfen. |
| K-02 | Benutzerentscheidung: Normaler Betrieb ohne Administratorrechte ist ausreichend. Die WinUI-Anwendung benötigt keinen erhöhten Benachrichtigungsprozess. Einen erhöhten Start mit einer konkreten Meldung ablehnen, statt Benachrichtigungen still abzuschalten oder auf Balloons auszuweichen. |
| K-03 | Einzel-EXE-Auslieferung ist in den geprüften SDK-Buildregeln vorgesehen. Tatsächlichen Start, COM-Aktivierung, Ressourcenauflösung und EXE-Austausch mit der konkret ausgewählten SDK-Version und beiden Architekturen nachweisen. Kein Paketformatwechsel als verdeckte Umgehung. |
| K-04 | Benutzerentscheidung: Windows 10 Version 1809 oder neuer ist als Mindestversion des WinUI-Branches freigegeben. Der Projektvertrag setzt mindestens Build 17763 voraus. Tatsächliche Laufzeitnachweise getrennt von der dokumentierten Plattformunterstützung führen. |
| K-05 | Benutzerentscheidung: Vor einem Verschieben des portablen Ordners erzeugte Meldungen dürfen ungültig werden, weil Windows auf den bisherigen EXE-Pfad verweist. Nach dem regulären Start am neuen Ort müssen neue Meldungen zum neuen Pfad und richtigen Konfigurationskontext gehören. Kein dauerhaft installierter Starthelfer. Mehrere Instanzen, Installerkopien und Arbeitsverzeichnisse dürfen weder eine falsche Konfiguration öffnen noch parallele Hintergrundprüfer erzeugen. |

Wenn ein Punkt nicht erfüllbar ist, die übrigen unabhängigen Arbeiten fortführen und die konkrete verbleibende Entscheidung benennen. Anforderungen nicht nachträglich für optional erklären. Eine nicht auflösbare Abweichung blockiert den Merge.

## Vollständiger benötigter Kontext

Zusätzlicher Benutzerauftrag: Die Einstellung **Benachrichtigungen anzeigen** ist standardmäßig eingeschaltet. `ShowNotifications` wird in der INI gespeichert und steuert neue native Meldungen für eingehende Änderungen, abgeschlossene Auto-Updates und Fehler automatischer Programmupdate-Prüfungen. Fehlende Werte bleiben eingeschaltet. SVN-Verarbeitung, Tray, manuelle Dialoge und aufbewahrte Meldungen bleiben unabhängig. Nachweis gemäß S-13 der Paritätsmatrix.

### Erforderliche MGGM-Skills und Regeln

- Für die PRP-Erstellung verwendet: `/home/andreasht/.claude/skills/cmd-prp-base-create/SKILL.md` und dessen `references/prp_base.md`.
- Betroffener Stack: C#, .NET 10, Windows Forms als Ausgang, WinUI 3, Windows App SDK, Windows-Shell-/COM-Integration und PowerShell-Prüfungen.
- Unter den für diese Aufgabe verfügbaren Skills gibt es keinen speziellen MGGM-Skill für C#/WinUI. Deshalb die unten genannten Microsoft-Primärquellen und die vorhandenen C#-Patterns verwenden; keine ExtJS-, Delphi-, DWS-, SQL- oder React-Konventionen übertragen.
- Die Anwendung ist eine native Desktopanwendung. Die bestehende Windows-UI-Automation ist der Ausgangspunkt für GUI-Prüfungen. Playwright ist kein Ersatz für das Rendern und Bedienen der nativen Fenster.
- Globale und lokale `CLAUDE.md`/`AGENTS.md` sowie gegebenenfalls hinzukommende `.claude/rules/` vor der Umsetzung laden. Bei Erstellung waren keine zusätzlichen projektspezifischen Regeldateien oder `DESIGN.md` im betroffenen Bereich vorhanden.
- Änderungen gezielt mit `apply_patch`; Kommentare zu weiterverwendetem Fachcode erhalten. Deutsche Dokumentation, Kommentare und UI-Texte mit echten Umlauten, technische Bezeichner Englisch.
- Benutzeränderungen erhalten; kein ungefragter Fallback, keine Platzhalter, keine unabhängigen Umgestaltungen.
- Sichtprüfung ist Pflicht. Ein Build, das Vorhandensein von Controls oder erfolgreiche Commandtests belegen keine angemessene Gestaltung.

### Dokumentation und Referenzen

| Quelle | Wofür vor Umsetzung lesen |
|---|---|
| `README.md`, vom Benutzer bereitgestellte Projektanweisungen | Bestehende Bedienung, Versionen, Pakete und Releasevertrag. |
| `docs/SVN-Koordination.md` | Arbeitskopieidentität, Reservierungsjournal, Wiederherstellung, Prozessende, Cleanup und Updateübergang. Verbindlicher Verhaltensvertrag. |
| `SVN_Notifier/MainForm.cs`, `MainForm.Tools.cs`, `MainForm.ApplicationUpdates.cs` | UI-gebundene Ablaufsteuerung, Einstellungen, Prüfintervalle, Befehle, Lebenszyklus und Updates. |
| `SVN_Notifier/Common/Config.cs`, `IniFile.cs`, `SvnItem.cs`, `Loc.cs` | Persistenz, Migration, Sprachtexte, Änderungsereignisse und bestehendes Modell. |
| `SVN_Notifier/Common/SvnTools.cs`, `SvnOperationRunner.cs`, `WorkingCopyLease.cs`, `SvnXml.cs` | Wiederzuverwendende SVN-Einstiegspunkte und Schutzmechanismen. |
| `SVN_Notifier/Common/ApplicationUpdater.cs`, `ApplicationUpdateInstaller.cs` | Paketprüfung, Versionsquelle, Hilfsprozess, atomarer Austausch und Neustart. |
| `tests/Gui.Tests`, `tests/Svn.Tests`, `tests/Updater.Tests`, `.github/scripts`, `.github/workflows/windows-build.yml` | Bestehende Prüfungen, Gegenproben und Releasebedingungen. |
| [WinForms-Patterns und WinUI-Entsprechungen](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/migrate-winforms-patterns) | Tabellen, Dialoge, Binding, UI-Thread und Tray sind keine automatische Control-Konvertierung. |
| [Benachrichtigungen für .NET](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet) | Registrierung, Aktivierung, UI-Thread, COM und Einschränkung erhöhter Prozesse. |
| [Benachrichtigungen verwalten](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/manage-app-notifications) | Tag/Gruppe, Entfernen, Ablauf und Historie. |
| [Unverpackte WinUI-Auslieferung](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe) | Einzel-EXE, Self-contained, Entpackung und Initialisierung. Mit konkretem NuGet-Paket gegenprüfen. |
| [Projektproperties und Auto-Initialisierung](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties) | Initialisierung im normalen Modus und in Installer-/Cleanup-Modi. |
| [Kompakte Steuerelemente](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing) | Informationsdichte für Maus und Tastatur. |
| [XAML-Theme-Ressourcen](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources) | Hell, Dunkel, Kontrast, Systemfarben und dynamische Themewechsel. |
| [Shell-Benachrichtigungen](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw) | Bestehendes Tray-/Balloon-Verhalten, insbesondere Windows 11. |

### WinForms-Ausgangsstruktur

```text
SVN_Notifier.sln
SVN_Notifier/
  SVN_Notifier.csproj                 # WinForms, net10.0-windows, EXE-Version
  MainForm*.cs                       # Liste, Commands, Polling, Lebenszyklus
  SettingsForm*.cs                   # Drei Einstellungsseiten
  PropertiesForm*.cs                 # Repository-Eigenschaften
  ErrorLogForm*.cs, UpdateLogForm*.cs
  AboutForm*.cs, ApplicationUpdateForm.cs
  Common/                            # Modell, SVN, INI, Updates, Darstellung
tests/{Gui.Tests,Svn.Tests,Updater.Tests}/
.github/scripts/{Build-Windows,Test-Gui,Test-SvnOperations,Test-ApplicationUpdater}.ps1
.github/workflows/windows-build.yml
docs/SVN-Koordination.md
```

### Codebasis und Verantwortung

Die Implementierung bündelt die Aktivierungszuordnung im Benachrichtigungsdienst. Die Ablaufsteuerung bleibt im UI-freien Kern. Keine zusätzliche Schichtenhierarchie für einzelne Getter einführen.

```text
SVN_Notifier.Core/
  SVN_Notifier.Core.csproj            # UI-freie Klassenbibliothek für geteilten Code
  ApplicationCoordinator.cs          # Polling und Übergang zu SVN/Programmupdates
  NotificationContext.cs             # Stabile Meldungsziele und Konfigurationen
SVN_Notifier/
  SVN_Notifier.csproj                 # WinUI-EXE, bisheriger EXE-Name/Versionsvertrag
  Program.cs, App.xaml, App.xaml.cs   # Moduserkennung, Initialisierung, Lebenszyklus
  MainWindow.xaml, MainWindow.xaml.cs
  ViewResources.xaml                 # Gemeinsame Theme-Ressourcen
  Views/SettingsView.xaml            # Einstellungen mit Code-behind
  Views/RepositoryPropertiesView.xaml # Repository-Eigenschaften mit Code-behind
  Views/RepositoryStatusView.xaml    # Statusanzeige mit Theme-Ressourcen
  Views/{ErrorLog,UpdateLog,About,ApplicationUpdate}View.cs
  Services/NotificationService.cs    # Meldungen und Klick-/Instanzzuordnung
  Services/TrayService.cs            # Shell-Tray, native Menüs, Wiederherstellung
  Services/WindowService.cs          # Fenster, Dialogbesitzer, Aktivierung
  Common/                            # Bestehender Quellcode bleibt maßgeblich
tests/{Gui.Tests,Svn.Tests,Updater.Tests}/
```

- Kernprojekt bindet die tatsächlich benötigten vorhandenen Dateien aus `Common` explizit als gemeinsame Quelldateien ein. Diese im UI-Projekt nicht ein zweites Mal kompilieren. Keine abweichenden Kopien der Prozess-/INI-Implementierung erzeugen.
- Geeignete Kernkandidaten: `Config`, `IniFile`, `Loc`, `SvnItem`, `SvnStatus`, `SvnXml`, `SvnTools`, `SvnOperationRunner`, `WorkingCopyLease`, `WindowsPathComparer` und ihre tatsächlichen Hilfstypen. Abhängigkeiten durch Build und Tests verifizieren.
- Bestehende `BindingList`-Verträge nicht für die UI ungefragt umbauen. Eine UI-Collection bzw. Adapter darf dasselbe Modell mit `INotifyPropertyChanged` darstellen; Objektidentität und Änderungen dürfen nicht auseinanderlaufen.
- Updater und Installer zunächst beim bisherigen Versions-/EXE-Vertrag belassen. Ein Verschieben in eine Bibliothek darf `CurrentVersion`, `typeof(...).Assembly` oder die im Test verwendeten Assemblies nicht auf eine falsche Version umstellen.
- Den produktiven Startpfad im vorhandenen Projekt umstellen. Keine dauerhaft parallele WinForms-Ersatzanwendung und kein Laufzeit-Fallback auf die alte UI vorsehen. Falls ein temporäres Vergleichsbuild benötigt wird, über den gesicherten Baseline-Checkout bereitstellen.
- Alte `Form`-/Designer-Einheiten kontrolliert aus der produktiven Kompilierung ablösen, sobald ihre Funktionen übertragen sind. Verbleibende Referenzen, Ressourcen und Tests mit umstellen; fachliche Kommentare an die neue verantwortliche Stelle übernehmen. Keine unbeteiligten Assets oder Quelldateien pauschal löschen.

### Bekannte Fallstricke unserer Codebasis und Bibliotheks-Eigenheiten

1. Die zentrale Tabelle heißt `ItemListView`, ist aber ein `DataGridView`. Nicht aus dem Namen auf eine vorhandene WinUI-kompatible Liste schließen.
2. `SvnItem` implementiert bereits `INotifyPropertyChanged` (`Common/SvnItem.cs:27`). Das ist wiederverwendbar, ersetzt aber keine Prüfung der vorhandenen Binding-/Statusänderungen.
3. `MainForm` enthält Polling, Aktiv-/Inaktivintervalle, Pause nach Start/Fortsetzung, Thread-Marshalling, Fehlerzustände und Installationssperren. Nur die Controls zu ersetzen würde diese Abläufe verlieren.
4. `--apply-update` und `--update-cleanup` sind produktive Startmodi (`MainForm.cs:1217`). Ein Installerhilfsprozess darf keine normalen Fenster, Benachrichtigungsregistrierungen oder SVN-Prüfer starten. SDK-Auto-Initialisierung kann vor dem regulären Einstiegspunkt wirken und ist ausdrücklich zu prüfen.
5. Relative portable INI-Dateien werden anhand des Arbeitsverzeichnisses gewählt. Eine durch Windows ausgelöste Benachrichtigungsaktivierung hat nicht automatisch dasselbe Arbeitsverzeichnis. Keine falsche AppData-Konfiguration öffnen.
6. Mehrere Instanzen sind zulässig und koordinieren ihre SVN-Vorgänge. Kein globaler Single-Instance-Zwang als bequeme Lösung für Benachrichtigungsklicks.
7. `ApplicationUpdater.PrepareAsync` akzeptiert genau EXE, README und Lizenz (`Common/ApplicationUpdater.cs:134`); der Installer tauscht genau die EXE aus (`ApplicationUpdateInstaller.cs:107`). Zusätzliche lose SDK-Dateien brechen diesen Vertrag.
8. Die bisherigen GUI-Gegenproben erwarten konkrete fachliche Fehler. Beim Austausch der UI nicht nur die Reflection-Zugriffe entfernen und diese Gegenproben damit unwirksam machen.
9. Auch `Svn.Tests` startet eine echte `MainForm` (`tests/Svn.Tests/Program.cs:449`). Diese Integration muss für die neue Anwendung erhalten bleiben.
10. Keine experimentelle Tabellenkomponente als Voraussetzung für den Funktionserhalt einführen. Für die vorhandene überschaubare Tabelle eine WinUI-Liste mit abgestimmten Spalten, Headern und vollständiger Bedienung bauen.

### UI-Zielbild und Designquellen

**UI-Variante:** native Windows-Desktopanwendung mit WinUI 3, C# und XAML. Vorrang haben Benutzerauftrag, Funktionsmatrix und aktueller Code. Microsofts Fluent-/Theme-Vorgaben bestimmen die technische Gestaltung. Die vorhandenen Aufnahmen unter `artifacts/svn-ui-reviewed-captures/` zeigen Version 3.0.0.5 und sind eine ergänzende Layoutreferenz, kein Nachweis des aktuellen Laufzeitstands und keine dauerhaft verfügbare Quelldatei.

**Gestaltungsabsicht:** kompakte Repository-Liste ohne zusätzliche Überschrift, gleichgewichtete Symbolleistenaktionen, ruhige Systemfarben und einheitliche Steuerelemente. Menü und Symbolleiste sitzen gemeinsam im Listenrahmen. „Ordner hinzufügen“ erhält keine dauerhafte Akzentfläche. Die Zeilen beginnen bei 32 DIPs und wachsen bei größerer Schrift mit; Checkboxen behalten mindestens 28 DIPs Höhe. Dezente Trennlinien werden innerhalb der Zeilen gezeichnet und erhöhen diese nicht. Fenster, Taskleiste und Tray verwenden die vorhandenen vier Statussymbole einschließlich bisheriger Priorität. Zeilen, Spalten und Aktionsgruppen folgen der tatsächlichen Aufgabe. Keine zusätzlichen Dashboardseiten, dekorativen Kennzahlen oder Kartenraster.

Der spätere Merge, die Releasevoraussetzungen und der Updateweg aus WinForms sind im [Merge- und Releaseablauf](04-merge-und-release.md) beschrieben. Diese Dokumentation ist kein Auftrag zur Veröffentlichung.

- Hauptfenster: Befehlsbereich oben, Liste im Hauptbereich, Status und Fehlerzugang unten. Alle bisherigen Menü-, Kontext- und Tray-Befehle erhalten; seltene Befehle dürfen im Menü liegen, müssen aber auffindbar und per Tastatur erreichbar sein.
- Liste: Prüfung, Statussymbol, Statustext, Pfad und Auto-Update. Pfad erhält den verbleibenden Raum; die übrigen Spalten bleiben lesbar. Auswahl, Hover, Fokus und deaktivierte Zustände unterscheiden. Status nie allein über Farbe vermitteln. Gemeinsame Pfadkürzung und vollständige Tooltips erhalten.
- Aktionen: bestehende Beschriftungen aus `Loc` und fachliche Aktivierungsbedingungen übernehmen. `Cleanup` bleibt so benannt. Überlauf bei schmalem Fenster statt abgeschnittener Befehle.
- Einstellungen: vorhandene Bereiche Allgemein, Status und Aktualisieren fachlich erhalten. Felder nach Aufgabe gruppieren; direkte Zuordnung zwischen Label und Eingabe; Speichern und Verwerfen sichtbar. Keine zusätzliche Hauptnavigation nur wegen der technischen View-Aufteilung.
- Repository-Eigenschaften: Pfad, Aktivierung und eigene Intervalle zusammen darstellen. Abhängige Felder entsprechend dem bisherigen Zustand aktivieren/deaktivieren.
- Protokolle: lange Pfade und Fehlermeldungen lesbar und scrollbar. Dateiaktionen, Ergebniszähler, Konflikte und weiterführender Protokollaufruf bleiben zugänglich.
- Programmupdate: Version, Release-Hinweise, Herunterladen/Neustart, Später, Fortschritt, Abbruch und Fehlerzustände fachlich erhalten. Keine neue SVN-Aktion aus einem Programmupdate-Dialog ableiten.
- Typografie: bestehende Benutzerschrift und Punktgröße erhalten und korrekt auf WinUI-Größen abbilden. Schriftgrößen 8,5, 12 und 24 Punkt prüfen. Neue Systemtypografie darf gespeicherte Benutzerwünsche nicht überschreiben.
- Dichte: kompakte WinUI-Dichte für den Arbeitsbereich; Abstände im Raster 4/8/12/16 logischen Pixeln. Zeilen und Buttons müssen mit Schriftgröße wachsen; keine feste Höhe, die bei 24 Punkt Text abschneidet.
- Farben: Theme-Ressourcen für Hell, Dunkel und Kontrast verwenden. Systemakzent gezielt für Auswahl und primäre Aktion. Kontrastfarben nicht durch starre Statusfarben überschreiben.
- Fenster: Inhalte bleiben bei wenig Platz über Scrollbereiche und Befehlsüberlauf erreichbar. Fenster nicht außerhalb des nutzbaren Bildschirms öffnen. Gespeicherte Größe, Taskleistenanzeige und Verbergen beim Schließen erhalten.
- Überschriften: präzise Titel ohne Eyebrow, Overline, Subtitle oder dekorative Erklärungstexte. Notwendige Eingabebedingungen, Status und Fehler bleiben am betroffenen Element sichtbar; ergänzende Tooltips müssen per Tastatur erreichbar sein.
- Zustände: leere Liste, keine Auswahl, Prüfung läuft, Arbeitskopie belegt, Änderungen vorhanden, lokale Änderungen, Konflikt, Fehler, Update läuft, Installerübergang und fehlende Programme gezielt gestalten und prüfen.

#### UI-Traceability-Matrix

| Quelle | UI-Detail | Geplante Umsetzung | Validierung |
|---|---|---|---|
| `MainForm.Appearance.cs:8`, `MainForm.Designer.cs:608` | Kompakte Liste mit fünf Spalten, Symbolen, Auswahl, Tooltips und Überlauf | `MainWindow`, Repository-Zeile und Befehlsbereich | F-01 bis F-13, Sichtprüfung breit/schmal |
| `MainForm.cs:574`, `MainForm.Appearance.cs:35` | Entf, Einfg, Eingabe, Leertaste, Escape, Doppelklick | Commands und Input-Routing | F-10 bis F-14, T-02 |
| `SettingsForm.cs:105`, `SettingsForm.Appearance.cs` | Alle Einstellungen, Speichern/Verwerfen, drei Bereiche | `SettingsView`, temporäres Einstellungsmodell | S-01 bis S-12, T-03 |
| `PropertiesForm.cs:33`, `PropertiesForm.Appearance.cs` | Repository-Aktivierung und eigene Intervalle | `RepositoryPropertiesView` | R-01 bis R-03, T-04 |
| `UpdateLogForm.cs:89`, `ErrorLogForm.cs:49` | Ergebniszeilen, Konflikte, Fehlerpuffer und Leeren | `UpdateLogView`, `ErrorLogView` | L-01 bis L-04, T-05 |
| `ApplicationUpdateForm.cs`, `MainForm.ApplicationUpdates.cs` | Download-/Installationsablauf einschließlich Abbruch | `ApplicationUpdateView`, Coordinator | U-01 bis U-09, T-09 |
| `MainForm.cs:651`, `MainForm.Designer.cs:134` | Tray-Klick, Tray-Menü, Fensterverbergen | `TrayService`, `WindowService` | W-01 bis W-05, T-06 |
| Drei `ShowBalloonTip`-Aufrufe | Drei Benachrichtigungstypen, eigene Ziele und Historie | `NotificationService`, `NotificationContextStore` | N-01 bis N-08, T-10 |
| Benutzerauftrag und WinUI-Theme-Dokumentation | Moderne Gestaltung, Hell/Dunkel/Kontrast, lesbare Dichte | XAML-Ressourcen und alle sieben Ansichten | T-12, aktuelle Screenshots tatsächlich ansehen |
| `Loc.cs`, `Config.FontFamily`, `Config.FontSize` | Deutsche/englische UI und konfigurierbare Schrift | Bestehende Texte und gemeinsame Ressourcen | S-09, T-12 |

## Implementierungsplan

### Datenmodelle und Struktur

```text
Startargumente erkennen
  Installer/Cleanup: ausschließlich den vorgesehenen Hilfsablauf ausführen
  normal/Benachrichtigung: Konfiguration und Instanzkontext eindeutig ermitteln
UI-freien Kern mit denselben SVN-/INI-Regeln aufbauen
ApplicationCoordinator übernimmt bisherige Abläufe aus MainForm
UI-Commands rufen Coordinator und vorhandene SvnTools-Einstiegspunkte auf
NotificationService empfängt fachliche Ereignisse und erzeugt native Meldungen
NotificationService ordnet einen Klick dem richtigen Eintrag und Prozess zu
Beenden/Installieren durchlaufen die vorhandenen unterschiedlichen Stoppregeln
```

- Einstellungen zunächst in einem editierbaren Entwurf halten; erst bei OK in `Config` übernehmen. Abbrechen darf keine sichtbaren oder gespeicherten Optionen verändern.
- Benachrichtigungen typisieren, etwa `IncomingChanges`, `AutoUpdateCompleted`, `ApplicationUpdateCheckFailed`. Diese Bezeichner sind neue interne Verträge und müssen implementiert und geprüft werden.
- Payloads enthalten einen validierbaren Meldungstyp und eine stabile Eintrags-/Konfigurationszuordnung. Keine Prozessbefehle, Zugangsdaten oder ungeprüfte Shell-Argumente aus Payloads übernehmen.
- Repository-Zuordnung darf nicht auf einem globalen veränderlichen `firstBalloonPath` beruhen. Entfernte Einträge führen zu einer verständlichen Anzeige ohne ungefragten neuen SVN-Auftrag.
- Bei Fehlern vorhandene Diagnosewege verwenden. Fehlende Voraussetzungen konkret melden; weder Meldungsfehler noch fehlgeschlagene Registrierung als erfolgreiche Benachrichtigung verbuchen.

### Aufgabenliste zur Erfüllung des PRP in der nötigen Reihenfolge

1. **BASELINE / BRANCH:** tatsächlichen Branch und Benutzeränderungen prüfen; Ausgangspaket und vorhandene Tests sichern. Funktionsmatrix gegen die dann aktuelle Anwendung vervollständigen. Baseline-Prüfungen gemäß T-01 ausführen.
2. **KOMPATIBILITÄT / SDK:** stabile SDK-Version prüfen und pinnen; K-01 bis K-05 untersuchen. Aus dem echten Projekt früh beide portable Pakete bauen, normalen Start, Aktivierung und Alt-zu-Neu-Update prüfen. Paket-/SDK-Version und Ergebnis dokumentieren, keine Runtime-Installation auf dem Entwicklerrechner als Ersatznachweis.
3. **KERN / TESTGRENZEN:** gemeinsame Quellen in `SVN_Notifier.Core` anbinden; Doppelkompilierung vermeiden. Bestehende SVN- und INI-Tests weiter ausführen. Nur für die UI notwendige Ablaufsteuerung aus `MainForm` lösen. Schutzmechanismen nicht neu erfinden.
4. **REPRÄSENTATIVE UI:** Hauptfenster und Einstellungen mit realistischen langen Pfaden, verschiedenen Status und dem Kernablauf hinzufügen, prüfen, auswählen, aktualisieren umsetzen. Auf das vorhandene Modell verdrahten. Hell-/Dunkelmodus, schmale Darstellung und große Schrift tatsächlich rendern und ansehen; Strukturmängel vor Übertragung auf weitere Ansichten beheben.
5. **RESTLICHE UI:** Repository-Eigenschaften, Update-/Fehlerprotokoll, Info und Programmupdate übertragen. Jeden vorhandenen Command und jede Einstellungsabhängigkeit aus der Matrix nachweisen. Datei-/Ordnerauswahl, Dialogbesitzer und externe Links prüfen.
6. **TRAY / LEBENSZYKLUS:** Tray-Symbol, Kontextmenü, Explorer-Neustart, Fensterverbergen, Taskleiste, Sitzung, Standby und Prozessende anbinden. Mehrere Notifier-Instanzen bleiben zulässig; ihre Reservierung koordiniert weiterhin nur eigene SVN-Vorgänge.
7. **BENACHRICHTIGUNGEN:** drei Ereignisse anbinden, eigenen Aktivierungskontext, Tag-/Gruppenverwaltung und kontrolliertes Entfernen implementieren. Registrierung nur für den richtigen Startmodus; UI-Thread und früher Klick vor Fenstererstellung berücksichtigen. Keine automatische Commit-/Cleanup-Aktion einführen.
8. **PAKET / UPDATER / CI:** vorhandene Skripte auf das produktive WinUI-Projekt und dessen Testtreiber ausrichten; Dateiname, Architektur, vier Versionsangaben, Prüfsummen, Paketinhalt und EXE-Austausch erhalten. Featurebranch-Builds zusätzlich zu `master` erlauben; Branch-Artefakte nicht als neuestes stabiles Release anbieten.
9. **REGRESSION / SICHTPRÜFUNG:** T-01 bis T-13 ausführen, Mängel korrigieren und nur betroffene Prüfungen erweitern/wiederholen. Alle Matrixzeilen mit aktuellen Artefakten und Ergebnissen verbinden. Kein Merge bei Lücken.
10. **DOKUMENTATION / ÜBERGABE:** README, SVN-Koordinationsdokumentation bei betroffenen Abläufen und Release-Hinweise auf den tatsächlichen Stand bringen. Offene Abweichungen benennen; lokalen Checkpoint erstellen. Merge/Release nur nach Benutzerauftrag.

Vor jedem größeren Bereich anwendbare Projektregeln und die dort referenzierten Primärquellen laden. Keine Stack-Skills ohne fachlichen Bezug hinzufügen. Die wiederkehrenden UI-Zustände zunächst an Hauptfenster und Einstellungen verifizieren.

### Pseudocode je Aufgabe, soweit nötig

Die folgenden Vorher-Ausschnitte stammen aus der Codebasis des Ausgangsstands. Die Nachher-Ausschnitte sind Entwurfsbeispiele für geplante interne APIs, keine Aussage über eine bereits existierende Implementierung.

**Beispiel A: erfolgreiche automatische Aktualisierung, `MainForm.cs:1006`.**

Vorher:

```csharp
TrayNotifyIcon.ShowBalloonTip(3000, Loc.Tooltip_AutoUpdate,
    Loc.Toast_AutoUpdateCompleted(folder.VisiblePath), ToolTipIcon.Info);
```

Nachher, im entsprechenden fachlichen Erfolgspfad:

```csharp
notificationService.ShowAutoUpdateCompleted(folder.Path, folder.VisiblePath);
```

Die neue Methode muss Meldungstyp, eindeutiges Ziel, lokalisierte Texte und native Zustellung umsetzen. Der Erfolgsauslöser und das Zurücksetzen von `_IsAutoUpdating` bleiben an den bisherigen Statusübergang gebunden. Ein bloß angenommener oder übersprungener SVN-Auftrag ist kein Erfolg.

**Beispiel B: eingehende Änderungen, `MainForm.cs:1151`.**

Vorher:

```csharp
firstBalloonPath = nonUpdatedFolders[0];
string balloonMessage = String.Join(Environment.NewLine, nonUpdatedFolders);
TrayNotifyIcon.ShowBalloonTip(Config.ShowBalloonInterval,
    Loc.Balloon_UpdateNeeded, balloonMessage, ToolTipIcon.Info);
```

Nachher, im übertragenen Benachrichtigungsablauf:

```csharp
notificationService.ShowIncomingChanges(newNonUpdatedFolders.ToArray());
```

Der Service muss die tatsächlichen Repository-Objekte bzw. stabilen Ziele vor späteren Änderungen sichern, die bisherige Bündelung berücksichtigen und K-01 erfüllen. Der dritte Aufruf in `MainForm.ApplicationUpdates.cs:66` wird entsprechend als eigener Meldungstyp angebunden.

### Integrationspunkte

- **Konfiguration:** bestehender Speicherort und Dateivertrag; alte Schlüssel nicht löschen. Eine neue Theme-Einstellung ist nicht zwingend: zunächst dem Systemtheme folgen. Zusätzliche Einstellungen nur bei tatsächlichem Bedienbedarf.
- **Polling:** aktive/inaktive Intervalle, erzwungene Prüfungen und Pausen mit einem testbaren Coordinator übernehmen; keine zusätzlichen Timer beim Wiederöffnen oder bei Toastaktivierung.
- **SVN:** öffentliche fachliche Einstiegspunkte in `SvnTools` und `SvnOperationRunner`; Pfadquotierung, Reservierung, Wiederherstellung und Logging unverändert wirksam halten.
- **Shell:** Tray mit nativer Windows-Integration, Benachrichtigungsaktivierung mit Windows App SDK. Keine reine WinForms-Abhängigkeit allein für die produktive Tray-Funktion fortführen.
- **Updates:** `--apply-update`, `--update-cleanup`, Warte-/Stoppregeln, Arbeitsverzeichnis und Stagingbereinigung erhalten. Testtreiber und Release-EXE dürfen unterschiedliche Versionsquellen nicht vermischen.
- **CI:** bestehende x64-/x86-Matrix und fachliche Gegenproben erhalten. Neue Desktopnachweise ergänzen; fehlende interaktive Shell als konkrete Prüflücke ausweisen, nicht als bestandenen Toasttest.

### UI-Implementierungsplan

1. Vorhandene Bedienabläufe und Matrixzeilen einem zentralen Command bzw. Viewzustand zuordnen; `AutomationId` und zugängliche Namen stabil vergeben.
2. Hauptfenster mit echter Bindung, Zustandsänderungen, Überlauf, Auswahl, Checkboxen und Leer-/Fehlerzuständen umsetzen.
3. Einstellungsentwurf und Repository-Eigenschaften mit Speichern/Verwerfen und abhängigen Eingaben umsetzen.
4. Nach Sichtprüfung des repräsentativen Ablaufs gemeinsame Ressourcen auf die übrigen fünf Ansichten übertragen.
5. Native Dateiauswahl, externe Dialoge, Tray, Aktivierung und Themewechsel mit echten Fenstern prüfen.
6. Die in T-12 festgelegten Ansichten rendern, Screenshots tatsächlich ansehen, Mängel korrigieren und betroffene Ansichten erneut aufnehmen und ansehen.

## Validierungsschleife

Die ausführlichen Szenarien, Befehle und Nachweisregeln stehen in [02-validierung.md](02-validierung.md). Den aktuellen Umsetzungs- und Prüfstand in [03-umsetzungsstand.md](03-umsetzungsstand.md) pflegen.

### Level 1: Syntax und Stil

Windows-Build mit bestehender Solution, gezielte Diff-Prüfung und Prüfung der projektspezifischen Sprach-/Kommentarregeln. Vorhandene Formatierungswerkzeuge aufgabenbezogen verwenden; kein zusätzliches Lint-/Testframework allein für den Umbau einführen.

### Level 2: Unit-Tests für jedes neue Feature, jede neue Datei und jede neue Funktion

Verhaltensbezogene Tests für neue Aktivierungs-, Konfigurations- und Benachrichtigungslogik in den vorhandenen Testprojekten ergänzen. Nicht jede Eigenschaft isoliert spiegeln. Positive und fehlerhafte Fälle gemäß T-03, T-10 und T-11 verwenden.

### Level 3: Integrationstest

Beide portablen Architekturen, echte isolierte SVN-/TortoiseSVN-Abläufe, Prozessende, Wiederherstellung, nativer Start, Benachrichtigungsaktivierung und tatsächliches Alt-zu-Neu-Programmupdate prüfen. Keine Benutzer-Arbeitskopien verwenden.

### Level 4: UI- und visuelle Validierung

Windows UI Automation plus sichtbare Bedienung der echten WinUI-Fenster. Screenshots sind ergänzende Nachweise für Gestaltung, keine Ersatzprüfung für Verhalten. Umfang und Größen gemäß T-12; alle wesentlichen Ansichten einschließen.

## Abschluss-Validierungscheckliste

- [ ] Ausgangsstand, konkrete SDK-/Windows-Versionen, Architekturen und Prüfläufe sind eindeutig dokumentiert.
- [ ] F-, S-, R-, L-, W-, P-, U- und N-Matrixzeilen sind mit aktuellen Nachweisen versehen.
- [ ] K-01 bis K-05 sind gelöst oder eine konkrete Abweichung ist vom Benutzer ausdrücklich entschieden; keine unterstellte Freigabe.
- [ ] Vorhandene Regressionen und Gegenproben behalten ihre Aussagekraft, zusätzliche Lücken sind geprüft.
- [ ] Alle sieben Ansichten und ihre relevanten Zustände sind visuell geprüft und gefundene Mängel korrigiert.
- [ ] Portable x64-/x86-Pakete, native Aktivierung, Installation und Konfigurationserhalt sind tatsächlich getestet.
- [ ] Keine neue ungeprüfte automatische SVN-Aktion, kein Fallback und kein unkoordinierter Prozessstart.
- [ ] README, Kommentare, relevante Fach-/Builddokumentation und Release-Hinweise passen zur Umsetzung.
- [ ] Keine fremden Änderungen eingecheckt; lokaler Checkpoint nur für den Aufgabenbereich.
- [ ] Benutzer entscheidet über Merge und Release. Ohne diesen Auftrag weder zusammenführen noch veröffentlichen.

## Zu vermeidende Fehlmuster

- Das Entfernen eines Tests oder einer Option ist kein Nachweis für Funktionsparität.
- Eine gespeicherte, aber wirkungslose Option ist kein Ersatz für die erhaltene Bedienfunktion.
- Die Umbenennung von WinForms-Controls in XAML-Namen ersetzt keine Prüfung der zugehörigen Abläufe.
- Ein erfolgreicher API-Aufruf belegt weder ein sichtbares Windows-Banner noch ein korrektes Klickziel.
- Ein bereits auf dem Entwicklerrechner installiertes SDK belegt keine eigenständige portable Auslieferung.
- Ein erfolgreicher Neustart in einer anderen INI-Datei belegt keinen Konfigurationserhalt.
- Die Wiederverwendung des Kerns erlaubt keine Abschwächung von Reservierungen oder Prozessendeprüfungen.
- Ein moderner Controlsatz ersetzt keine passende Hierarchie, Dichte und lesbare Dialoge.
- Keine überholten Entwurfsvarianten parallel pflegen. Dieses PRP und seine Matrizen bei beauftragten Entscheidungen gemeinsam aktualisieren.
