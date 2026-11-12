# Funktionsparität

Diese Matrix beschreibt den verbindlich zu erhaltenden Funktionsbestand. Quellen sind relativ zum Repository angegeben; Zeilen beziehen sich auf die Recherche zum Ausgangsstand. Vor Umsetzung auch indirekte Aufrufer und den dann aktuellen Code prüfen. Eine fehlende Testabdeckung hebt keine Funktion auf. Unbeabsichtigte Fehler nicht als zu konservierende Fachregel übernehmen, aber eine notwendige Korrektur ausdrücklich von einem Funktionsverlust unterscheiden.

**Nachweisstand:** Dokumentationsrecherche. Implementierungs- und Laufzeitnachweise für WinUI stehen aus. Keine Zeile gilt allein durch Aufnahme in diese Tabelle als bestanden.

Prüfgruppen T-01 bis T-13 sind in [02-validierung.md](02-validierung.md) definiert. Bei Umsetzung je Zeile aktuellen Prüflauf, Ergebnis und relevante Artefakte ergänzen; keine geschätzten Erfüllungsgrade verwenden.

## Repository-Liste und Befehle

| ID | Erhaltenes Verhalten | Quelle | Nachweisziel |
|---|---|---|---|
| F-01 | Ordner und Dateien über die vorhandenen Auswahlwege hinzufügen; Pfadtyp und Validierung erhalten. | `SVN_Notifier/MainForm.cs:254`, `:290`; `Common/SvnItem.cs` | T-02: beide Typen, gültig/ungültig, mehrere Einträge. |
| F-02 | Einträge entfernen und die bisherige Auswahl-/Speicherwirkung erhalten. | `MainForm.cs:305` | T-02: selektierter Eintrag, keine Auswahl, Neustart. |
| F-03 | Explorer-nahe natürliche Pfadsortierung einschließlich vorhandener Benutzeranpassungen. | `MainForm.cs:100`; `Common/WindowsPathComparer.cs` | T-02: numerische Pfadteile, Unicode, Groß-/Kleinschreibung. |
| F-04 | Gemeinsame Pfadteile kürzen, unterscheidbare sichtbare Pfade und vollständige Tooltips erhalten. | `MainForm.Tools.cs:17`; `MainForm.cs:1376` | T-02: gemeinsame und getrennte Wurzeln, lange Pfade. |
| F-05 | Prüfung je Eintrag per Checkbox, Maus und Tastatur schalten und speichern. | `MainForm.cs:1344`; `MainForm.Appearance.cs` | T-02/T-03: Klick, Leertaste, Neustart. |
| F-06 | Auto-Update je Eintrag per Checkbox schalten und speichern; neue/alte feldlose Einträge beginnen aus. | `MainForm.cs:1358`; `Common/SvnItem.cs`; `docs/SVN-Koordination.md` | T-02/T-07: beide Zustände, ältere INI, Neustart. |
| F-07 | Unbekannt, synchronisiert, geändert, lokal bearbeitet, kombiniert und Fehler durch Text und Symbol unterscheiden. | `MainForm.Appearance.cs:137`; `MainForm.cs:1318` | T-02/T-12: jeder Zustand, auch deaktivierte Prüfung. |
| F-08 | Je nach Auswahl und Status dieselben Aktionen aktivieren/deaktivieren. | `MainForm.cs:540` | T-02: alle Status, keine Auswahl, fehlende Programme. |
| F-09 | Symbolleiste, Hauptmenüs und Repository-Kontextmenü behalten alle Befehle und Tastaturzugänge. | `MainForm.Designer.cs:190`, `:331`; `MainForm.Appearance.cs` | T-02/T-12: vollständige Befehlsinventur, Überlauf. |
| F-10 | Entf entfernt, Einfg fügt Ordner hinzu, Eingabe führt konfigurierte Doppelklickaktion aus. | `MainForm.cs:574` | T-02: echte Tastatureingaben, korrekte Auswahl. |
| F-11 | Doppelklick: Öffnen, Protokoll, Aktualisieren, Übertragen/Commit oder Jetzt prüfen. Protokoll bevorzugt Änderungsprotokoll, soweit verfügbar. | `MainForm.cs:593`; `Common/Config.cs:83` | T-02: alle fünf Einstellungen und Statusabhängigkeit. |
| F-12 | Drag-and-drop mehrerer Dateien und Ordner mit bisherigen Annahmebedingungen. | `MainForm.cs:628`, `:1303` | T-02: gemischte Ziele, Ablehnung ungeeigneter Inhalte. |
| F-13 | Eigenschaften, Öffnen, vollständiges Protokoll und Änderungsprotokoll öffnen das richtige Ziel. | `MainForm.Tools.cs`; `MainForm.cs:418` | T-02/T-04/T-07: Pfad-/Eintragszuordnung. |
| F-14 | Escape blendet das Hauptfenster aus; Leertaste schaltet die fokussierte Repository-Checkbox ohne unbeabsichtigten Doppelklick. | `MainForm.cs:675`; `MainForm.Appearance.cs:35` | T-02/T-06. |
| F-15 | Manuelles Aktualisieren funktioniert auch bei ausgeschaltetem Auto-Update und ändert dessen Checkbox nicht. | `MainForm.Tools.cs:42`; `docs/SVN-Koordination.md` | T-07: echte eingehende Änderung für beide Auto-Update-Werte. |
| F-16 | Alle aktualisieren, stille/angezeigte Verarbeitung und Statusanzeige erhalten. | `MainForm.cs:472`; `Common/SvnTools.cs`; `Common/Config.cs:118` | T-07: mehrere Ziele, Fehler und belegtes Ziel. |
| F-17 | Commit und TortoiseSVN-Protokoll-/Updatedialoge erhalten Argumente und Lebensdauer. | `Common/SvnTools.cs:109`; `MainForm.Tools.cs` | T-07/T-08: Leerzeichen, Umlaute, abschließender Separator. |
| F-18 | Änderungsprotokoll-vor-Update respektieren; Aktualisieren nicht ohne die bisher erforderliche Sichtung freigeben. | `MainForm.Tools.cs:42`; `Common/Config.cs:177` | T-07: ungesehene/gesehene Revision, neue HEAD-Revision. |
| F-19 | Cleanup bei jedem ausgewählten Eintrag, auch Fehler/unbekannt/Prüfung aus; ohne Auswahl deaktiviert. Nur Auswahldialog öffnen, keine automatische Bereinigung. | `MainForm.cs:437`; `docs/SVN-Koordination.md` | T-07: echter Dialog öffnen/abbrechen, Gegenprobe. |
| F-20 | Jetzt prüfen erzwingt die Prüfung des gewählten Eintrags unter denselben Koordinationsregeln. | `MainForm.cs:593`; Kontextmenü | T-07: angehaltene/aktive Prüfung und belegte Arbeitskopie. |

## Einstellungen, Persistenz und Repository-Eigenschaften

| ID | Erhaltenes Verhalten | Quelle | Nachweisziel |
|---|---|---|---|
| S-01 | SVN- und TortoiseProc-Pfade anzeigen, auswählen, prüfen und speichern; ungültige Konfiguration führt zur Einstellungsauswahl. | `Common/Config.cs:155`; `SettingsForm.cs:105`; `MainForm.cs:1262` | T-03/T-06: fehlende/gültige EXE, Erststart. |
| S-02 | Standardprüfintervalle für aktives und inaktives Fenster getrennt speichern und anwenden. | `Common/Config.cs:155`; `MainForm.cs:724` | T-03/T-07: Fenster aktivieren/deaktivieren, Zeitablauf. |
| S-03 | Pause nach Programmstart und nach Windows-Fortsetzung mit Aktivierung und Dauer erhalten. | `Common/Config.cs:162`; `MainForm.cs:76` | T-03/T-06: beide Schalter und Timerübergänge. |
| S-04 | SVN-Status-Zeitlimit einschließlich 0 = unbegrenzt; keine Übertragung auf Schreibvorgänge. | `Common/Config.cs:180`; `Common/SvnTools.cs` | T-03/T-08: Timeout und unbegrenzter Test mit kontrolliertem Abbruch. |
| S-05 | Alle fünf Doppelklickoptionen in der INI erhalten und anwenden. | `Common/Config.cs:83`, `:168` | T-02/T-03. |
| S-06 | Benachrichtigungsdauer einschließlich Sekundenwert und INI-Roundtrip erhalten; tatsächliches Verhalten gemäß K-01 klären. | `Common/Config.cs:169`; `SettingsForm.cs:141`, `:197` | T-01/T-03/T-10; keine wirkungslose Ersatzoption. |
| S-07 | Ausgeblendeter Start und Taskleistenanzeige als getrennte Optionen erhalten. | `Common/Config.cs:170`; `MainForm.cs:51` | T-03/T-06: alle Kombinationen. |
| S-08 | Automatische Programmupdate-Prüfung, Änderungsprotokoll-vor-Update, stilles Update-all und Tortoise-Fensterabschlussaktion erhalten. | `Common/Config.cs:172`; `SettingsForm.cs` | T-03/T-07/T-09: Werte, Abhängigkeiten, Neustart. |
| S-09 | Deutsch/Englisch sowie gespeicherte Schriftfamilie und Punktgröße erhalten. Bestehenden Hinweis auf notwendigen Neustart abbilden, falls Änderungen weiterhin erst dann wirken. | `Common/Config.cs:179`; `SettingsForm.cs:168`; `Loc.cs` | T-03/T-12: Speichern, Verwerfen, Neustart. |
| S-10 | Speichern übernimmt alle Optionen; Verwerfen ändert weder laufenden noch gespeicherten Zustand. | `SettingsForm.cs:168`; `tests/Gui.Tests/Program.cs:174` | T-03: vollständiger Vorher-/Nachher-Vergleich. |
| S-11 | Konfiguration importieren/exportieren einschließlich Nachfrage vor Ersetzen und Fehlerbehandlung erhalten. | `MainForm.cs:133`; `:154` | T-03: Dateiinhalt, vorhandene Liste, Abbruch, ungültige Eingabe. |
| S-12 | Ältere INI-Formate, Trennzeichen-/Feldmigration, Schreibfehlerkorrektur und vorhandene Werte erhalten; Speicherort und Logdateizuordnung bleiben eindeutig. | `Common/Config.cs:222`; `MainForm.cs:1239`; `Common/IniFile.cs` | T-03: bestehende Migrationszweige, portable INI und AppData. |
| R-01 | Repository-Pfad, Datei-/Ordnertyp, Aktivierung und Auto-Update unverändert serialisieren. | `Common/Config.cs:335`; `Common/SvnItem.cs` | T-03/T-04: Roundtrip auch mit Pipe-/Unicode-Prüffällen entsprechend bestehendem Vertrag. |
| R-02 | Eigene aktive/inaktive Intervalle je Repository, Vererbung der Standardwerte und deaktivierte Eingaben erhalten. | `PropertiesForm.cs:33`, `:87`; `Common/SvnItem.cs:156` | T-04: Standard/eigen/Prüfung aus. |
| R-03 | Eigenschaften übernehmen oder verwerfen und betroffenen Listeneintrag korrekt aktualisieren. | `MainForm.cs:418`; `PropertiesForm.cs:87` | T-04: Modell, UI und Neustart. |

## Protokolle, Info und Fensterbetrieb

| ID | Erhaltenes Verhalten | Quelle | Nachweisziel |
|---|---|---|---|
| L-01 | Updateprotokoll zeigt hinzugefügt, gelöscht, aktualisiert, Konflikt, zusammengeführt, Abschluss/Revision und Fehler mit Zählern. | `UpdateLogForm.cs:89`, `:177` | T-05: repräsentative Ausgaben und lange Pfade. |
| L-02 | Konfliktwarnung, weiterführendes Protokoll und vorhandener TortoiseSVN-Fehlerklang erhalten. | `UpdateLogForm.cs:154`, `:190`, `:200` | T-05: Konflikt, konfigurierte/fehlende Klangquelle, Zielaufruf. |
| L-03 | Fehlerpuffer pro Ziel, Anzeige über Warnungszugang und Leeren einschließlich der bisherigen Übernahme beim Dialogschließen erhalten. | `MainForm.cs:1171`, `:1185`; `ErrorLogForm.cs:49` | T-05: Anzeigen, Leeren, Schließen, erneuter Fehler. |
| L-04 | Infofenster zeigt Produkt-/Versionsdaten und vorhandene Links; externe Links behalten Fehlerbehandlung. | `AboutForm.cs:39`; `AboutForm.Appearance.cs`; `ApplicationUpdateForm.cs:73` | T-05/T-09: richtige Version und Linkziel. |
| W-01 | Tray-Symbol mit Status, Tooltip und Menü Programm anzeigen / Alle aktualisieren / Beenden. | `MainForm.cs:1095`; `MainForm.Designer.cs:134` | T-06: alle Status, Menü und Shell-Neustart. |
| W-02 | Tray-Klick zeigt/aktiviert das Hauptfenster; Schließen blendet aus, Beenden beendet kontrolliert. | `MainForm.cs:651`, `:685`, `:738` | T-06: normal/minimiert/ausgeblendet. |
| W-03 | Gespeicherte Fenstergröße, Größenänderung im Normalzustand und Bildschirmgrenzen erhalten. | `MainForm.cs:112`, `:713`; `Common/Config.cs:363` | T-06/T-12: Neustart, DPI-/Monitorwechsel. |
| W-04 | Standby/Fortsetzung, Aktiv-/Inaktivzustand und Abbruch einer Windows-Abmeldung behalten ihre unterschiedlichen Übergänge. | `MainForm.cs:76`, `:724`, `:1203` | T-06/T-08: echte Übergänge oder abgegrenzte Simulation plus Windows-Nachweis. |
| W-05 | Mehrere Notifier-Instanzen bleiben zulässig; Beenden und Update einer Instanz beeinflussen keine fremden Prozesse. | `docs/SVN-Koordination.md`; `Common/SvnOperationRunner.cs` | T-06/T-08/T-10: zwei Instanzen und externer Prozess. |
| W-06 | Produktive Startmodi `start`, `--apply-update` und `--update-cleanup` bleiben mit ihren bisherigen Wirkungen nutzbar. | `MainForm.cs:1217` | T-06/T-09: echte EXE mit Argumenten, kein unbeabsichtigter UI-/Pollingstart. |

## SVN-Ausführung und Koordination

| ID | Erhaltenes Verhalten | Quelle | Nachweisziel |
|---|---|---|---|
| P-01 | Status/Info, stille Updates, Commit-, Update-, Protokoll- und Cleanupdialoge reservieren vor dem Start bis zum tatsächlichen Prozessende. | `Common/SvnOperationRunner.cs:60`; `docs/SVN-Koordination.md` | T-07/T-08: alle Einstiegspunkte. |
| P-02 | Unterordner, physische Windows-Pfadidentität, Junctions/Aliase/UNC und eingebettete Arbeitskopien teilen die vorgesehenen Wurzeln. | `Common/WorkingCopyLease.cs`; Koordinationsdokumentation | T-08: vorhandene Pfad-/Wurzeltests erhalten. |
| P-03 | Belegte Arbeitskopie startet keinen konkurrierenden Auftrag; Status bleibt erhalten; Wiederholung frühestens nach 15 Sekunden. Manuelle Dialoge nicht ungefragt nachholen. | `Common/SvnItem.cs`; `SvnOperationRunner.cs`; Koordinationsdokumentation | T-07/T-08: Zeitgrenze, Status und keine automatische Schreibaktion. |
| P-04 | SVN-CLI und Tortoise-Pfadquotierung erhalten; `@name`, `@123`, abschließendes `@`, relatives `-r2`, Umlaute und Leerzeichen korrekt behandeln. | `Common/SvnTools.cs`; `tests/Svn.Tests/Program.cs`; Koordinationsdokumentation | T-07/T-08: tatsächlicher Zielpfad und Kollisionsgegenprobe. |
| P-05 | Normales Beenden stoppt neue Aufträge, beendet Leseprüfungen kontrolliert und wartet auf Schreibvorgänge/Dialoge. | `SvnOperationRunner.cs`; `MainForm.cs:1279` | T-08: laufende und verzögert startende Prozesse. |
| P-06 | Reservierungsjournal, Besitzer-/Kindprozessidentität, Rechnerkennung, Prozessstartzeiten und Wiederherstellung alter/eigener verwaister Dateien erhalten. | `Common/WorkingCopyLease.cs`; Koordinationsdokumentation | T-08: vorhandene echte Prozess-/Recoverytests. |
| P-07 | Freigabe über geschütztes Windows-Handle nach Prozessende, auch bei Stream-, Diagnose-, Timeout- und Nachrichtenloopfehlern. | `WorkingCopyLease.cs`; `SvnOperationRunner.cs`; `MainForm.cs:1279` | T-08: Lösch-/Austausch-/Fehlergegenproben. |
| P-08 | Diagnose nennt Vorgang, Wurzel, Zeit, Phase, PID, Exit-Code und Grund; keine Argumente/Zugangsdaten. | `SvnOperationRunner.cs`; `Config.cs:390`; Koordinationsdokumentation | T-08: aktuelle Ereignisse und sensible Felder. |
| P-09 | Keine automatischen Cleanups, keine `wc.db`-Änderungen, keine behauptete Koordination externer SVN-/Container-Aufrufe. | `docs/SVN-Koordination.md` | T-07/T-08: externe Prozesse bleiben unberührt. |

## Programmupdates und Auslieferung

| ID | Erhaltenes Verhalten | Quelle | Nachweisziel |
|---|---|---|---|
| U-01 | Automatische Prüfung beim Start und alle zwölf Stunden; getrennt abschaltbar, manuelle Prüfung bleibt verfügbar. | `MainForm.ApplicationUpdates.cs:8`, `:17` | T-09: an/aus, Timer, manuell. |
| U-02 | Nur neuere stabile Versionen der passenden Architektur; keine Downgrades oder Vorabversionen. | `Common/ApplicationUpdater.cs:47` | T-09: bestehende positive/negative Release-Fixtures. |
| U-03 | Bestätigter Download/Neustart, Später, Release-Link, Fortschritt und Abbruch erhalten. | `ApplicationUpdateForm.cs` | T-09/T-12: jede Aktion und Fehlerzustände. |
| U-04 | SHA-256, ZIP-Inhalt, PE-Architektur und vier Versionsangaben vor Installation prüfen. | `ApplicationUpdater.cs:94`; `.github/scripts/Build-Windows.ps1` | T-09: Manipulation, falsche Architektur/Version, unvollständiges ZIP. |
| U-05 | Nach bestätigtem Download neue SVN-Aufträge sperren, eigene Prozesse samt Unterprozessen beenden und Hintergrundprüfung abwarten. | `MainForm.ApplicationUpdates.cs:91`; Koordinationsdokumentation | T-08/T-09: Schreiben, Dialog, Kindprozess, verzögerter Start. |
| U-06 | Fehlgeschlagener Installerstart gibt Verarbeitung wieder frei; unbestätigtes Prozessende verhindert Installerstart. | `MainForm.ApplicationUpdates.cs:98`; `SvnOperationRunner.cs` | T-08/T-09: Fehler, Timeout und erneuter Versuch. |
| U-07 | Installer wartet auf die richtige ursprüngliche Prozessidentität, prüft erneut und tauscht EXE atomar. | `ApplicationUpdateInstaller.cs:94` | T-09: tatsächlicher Austausch und Manipulation zwischen den Prüfungen. |
| U-08 | Neustart erhält Arbeitsverzeichnis, INI und Repository-Liste; temporäre Installationsdateien werden entfernt. | `ApplicationUpdateInstaller.cs:109`, `:120` | T-09: unterschiedliche Programm-/Arbeitsordner, portable INI und AppData. |
| U-09 | Portable x64-/x86-Pakete mit EXE, README, Lizenz und Prüfsummen; Start ohne separat installierte Laufzeit. | `.github/scripts/Build-Windows.ps1`; `windows-build.yml` | T-09/T-13: saubere Windows-Umgebung und beide Architekturen. |

## Native Benachrichtigungen

| ID | Erhaltenes Verhalten und beauftragte Erweiterung | Quelle | Nachweisziel |
|---|---|---|---|
| N-01 | Hinweise auf neue Änderungen auch bei Auto-Update aus; bisherige Bündelung und Zuordnung erhalten. | `MainForm.cs:1151`; `tests/Gui.Tests/Program.cs:109` | T-07/T-10. |
| N-02 | Erfolgreiches stilles Auto-Update meldet den richtigen Eintrag genau nach Erfolg. | `MainForm.cs:1001` | T-10: Erfolg, Fehler, belegtes Ziel und kein falscher Erfolg. |
| N-03 | Fehler der automatischen Programmupdate-Prüfung bleibt eine Meldung; manuelle Fehler-/Ergebnisdialoge erhalten. | `MainForm.ApplicationUpdates.cs:59` | T-09/T-10. |
| N-04 | Klick zeigt die Anwendung mit dem richtigen Eintrag; verschiedene aufbewahrte Meldungen behalten ihr eigenes Ziel. | `MainForm.cs:661`; Benutzerauftrag native Meldungen | T-10: A/B, Klick auf ältere Meldung, entfernter Eintrag. |
| N-05 | Meldungen sind in der Benachrichtigungszentrale verwaltbar; erledigte Änderungsmeldungen entfernen/aktualisieren, keine Erfolgshistorie ungefragt als ausstehend darstellen. | Benutzerauftrag; Microsoft-Verwaltungs-API | T-10: Bannerende, Historie, Statuswechsel, Neustart. |
| N-06 | Klick bei beendetem Prozess, frühe Aktivierung, mehrere Instanzen und verschobener portabler Ordner öffnen den passenden Kontext. | K-05; `MainForm.cs:1239` | T-10: kein zusätzlicher Prüfer, keine falsche INI. |
| N-07 | Betrieb bei deaktivierten Meldungen, Nicht stören, Registrierungsfehlern und erhöhter Anwendung wird ausdrücklich geprüft. Kein behaupteter Versand-/Anzeigenachweis bei Fehlschlag. | K-02; Microsoft-Dokumentation | T-10/T-11: reale Shellzustände und kontrollierte Fehler. |
| N-08 | Gespeicherte Benachrichtigungsdauer und normale Betriebsfunktionen bleiben bei Umstellung erhalten; Abweichungen nicht stillschweigend akzeptieren. | K-01/K-02; S-06 | T-01/T-10, gegebenenfalls ausdrückliche Benutzerentscheidung vor Änderung. |

## Regeln für die Paritätsentscheidung

- Der aktuelle Code und das beobachtete Ausgangsverhalten ergänzen diese Liste. Neu entdeckte bestehende Funktionen vor dem Umbau ergänzen, nicht aus dem Umfang ausschließen.
- Für jede bisher ungetestete Funktion einen passenden Regressionstest oder einen begründeten, konkreten manuellen Nachweis vorsehen.
- Vorhandene Testaussagen auf die neue Oberfläche übertragen; WinForms-Typnamen sind kein fachlicher Vertrag, ihre geprüfte Wirkung ist es.
- Ein API-/Frameworkwechsel rechtfertigt keine Funktionseinschränkung. Offene technische Grenzen als Blocker behandeln und dem Benutzer mit konkretem Unterschied vorlegen.
- Ein Merge ist erst nach den in [02-validierung.md](02-validierung.md) beschriebenen Nachweisen und ausdrücklichem Mergeauftrag zulässig.
