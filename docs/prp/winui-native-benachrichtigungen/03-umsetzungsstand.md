# Umsetzungsstand und Nachweise

## Auftrag und Arbeitsplan

Die Implementierung ist auf `feature/winui-native-notifications` beauftragt. Kein Merge und kein Release. Benutzeränderungen außerhalb des Umbaus bleiben erhalten.

1. Ausgangsstand und Regressionen sichern.
2. SDK, Einzel-EXE, Startmodi und Benachrichtigungskompatibilität prüfen.
3. UI-freien Kern mit vorhandener SVN-Koordination anbinden.
4. Hauptfenster, Einstellungen und übrige Ansichten funktional und visuell prüfen.
5. Tray, Aktivierung, Lebenszyklus, native Meldungen und Programmupdate prüfen.
6. Paritätsmatrix und Dokumentation anhand der tatsächlichen Nachweise abgleichen.

## Testsitzungen

Routinemäßige GUI-Prüfungen laufen über `Invoke-QuietWindows.vbs` und `Invoke-IsolatedDesktop.ps1` auf einem getrennten, inaktiven Windows-Desktop ohne sichtbare Konsole. Der sichtbare Starter besitzt eine vor jeder Eingabe wirksame Datumsprüfung.

Sichtbare Testläufe benötigen ein ausdrücklich erlaubtes Zeitfenster. Für den 12.11.2026 war 13:00 bis 14:00 Uhr Europe/Berlin erlaubt. Nach jedem Testlauf sind die dabei gestarteten eigenen Instanzen zu beenden und ihr Prozessende zu prüfen. Außerhalb ausdrücklich erlaubter Läufe keine sichtbaren Wiederholungen. `native-window-guard-1253.log` belegt die Ablehnung eines nicht freigegebenen Zeitpunkts vor Testverzeichnis und Prozessstart, Exit-Code 0.

Zusätzlich wurde nach dem Entsperren ausdrücklich ein kurzer Desktoplauf erlaubt. Dieser lief am 12.11.2026 von 14:20:10 bis 14:21:36 Uhr und beendete seine eigenen Instanzen regulär. Der Modus `-Quick` lässt systemweite Benachrichtigungs-, DPI- und Kontraständerungen aus. Diese konkrete Erlaubnis gilt nicht für spätere Wiederholungen.

Eine weitere Freigabe lautete ausdrücklich „Jetzt höchstens fünf Minuten“. Der Produktlauf endete um 14:46:20 Uhr, eine begrenzte Shell-Diagnose um 14:47:32 Uhr. Danach wurden keine weiteren Desktop-Eingaben ausgeführt. Die Prozesskontrolle um 14:48:25 Uhr findet null eigene Testinstanzen. Diese Freigabe ist verbraucht.

Auf die erneute Freigabe „Prüf eben schnell“ folgte ein Kurzlauf von 14:55:40 bis 14:56:36 Uhr. Alle drei dabei gestarteten Produktinstanzen wurden regulär geschlossen; die Kontrolle um 14:57:29 Uhr findet null eigene Testinstanzen. Weitere sichtbare Läufe sind damit nicht freigegeben.

## Technischer und gestalterischer Stand

- WinUI 3, Windows App SDK 2.6.5, portable Einzel-EXE für x64/x86 und Produktversionsangaben 3.0.0.14. Dies ist keine Veröffentlichung.
- Vorhandene SVN-, INI-, Reservierungs- und Updaterlogik wird gemeinsam verwendet. Die Ablaufsteuerung und Benachrichtigungskontexte sind vom Fenster getrennt.
- Listenbereich mit erkennbarer Hauptaktion, kompakteren Spalten, semantischen Statuskennzeichnungen, abgestufter Typografie und inhaltsbezogenen Dialogbreiten. Einstellungen sind fachlich gruppiert. Schrift und Sprache bleiben konfigurierbar.
- Sieben Ansichten werden auf Deutsch und Englisch mit 8,5/12/24 Punkt sowie im Dunkelmodus ausgeführt. Tatsächliche Screenshots wurden angesehen, einschließlich Updateprotokoll im Dunkelmodus und mit 24 Punkt. Dies ersetzt keinen Windows-DPI-Wechsel.
- Die Portabilität wird anhand des .NET-Bundlemarkers geprüft. Die vollständige Extraktion für WinUI ist mit einem nicht leeren Assemblypfad vereinbar und darf den Programmupdate nicht sperren.
- Fenstermaße werden auf den verfügbaren Arbeitsbereich begrenzt. Eine bestätigte Sitzungsbeendigung schließt das eigene Fenster; eine abgebrochene Beendigung gibt die Prüfung wieder frei. Beim Schließen wird das Tray-Symbol freigegeben.
- Ein Import wird einschließlich Migration zunächst in einer separaten Datei validiert. Bei ungültigen Eingaben oder gesperrtem Ziel bleiben aktive Datei, nicht gespeicherte Optionen und INI-Cache erhalten. Der Export übernimmt die gespeicherten Daten bytegenau.
- Doppelklick und sichtbare Repository-Befehle verwenden dieselbe Statusfreigabe. Deaktivierte Aktionen starten auch per Doppelklick keinen SVN-Auftrag.
- **Benachrichtigungen anzeigen** steuert neue Windows-Meldungen aller drei Arten ohne Neustart. Der fehlende INI-Wert beginnt eingeschaltet; gespeichertes Aus bleibt erhalten. Prüfung, Auto-Update, Tray und manuelle Dialoge bleiben unabhängig.

## Nachweise

Artefaktwurzel: `artifacts/winui-implementation-20261112/`. Die isolierten Testdaten werden nicht als Produktquellcode eingecheckt.

| Bereich | Tatsächlicher Nachweis |
|---|---|
| Ausgangsstand | Baseline-Checkout aus `3df587de4a2a0bf1396b3d85ed5d1da95c48cf3a`; x64-Build, GUI, echte SVN-/TortoiseSVN- und Updaterprüfungen unter `baseline-results/`. |
| Solution-Build | `reviewed-solution.log`: Release-Build mit Exit-Code 0, null Fehler und 25 Nullable-Warnungen in gemeinsam verwendeten Klassen. Die Paketbuilds beider Architekturen sind in `reviewed-valid.log` enthalten. |
| Unsichtbarer Starter | `quiet-console-check.log`: Kindprozesse mit Exit-Code 0 und 9, erhaltene Argumente einschließlich Unicode, Leerzeichen und Shellzeichen. |
| GUI und Gegenproben | `reviewed-valid.log`, Exit-Code 0: beide Architekturen mit vier Varianten und jeweils 481 Prüfungen; je 15 erkannte Gegenproben. `shell-settings-matrix.log` und `shell-settings-matrix-x86.log`, beide Exit-Code 0: zusätzlich je viermal 497 Prüfungen und 17 erkannte Gegenproben auf dem Windows-Shell-Desktop. Die eigenen GUI-Prozesse enden nach jeder Variante und Gegenprobe; der begrenzte Job beendet etwaige verbliebene Kindprozesse. |
| Einstellungen und Eigenschaften | Alle sichtbaren Einstellungen speichern, INI neu laden, Dialog erneut öffnen, manipulierten gespeicherten Wert erkennen. Tatsächliche Eigenschaften- und Entfernen-Dialoge bestätigen/verwerfen; zwischenzeitlich entfernte Einträge verändern keine fremden Einträge. Eigenschaften verwerfen, ungültigen Pfad ablehnen, eigene Intervalle einschließlich Null und globale Vorgaben erneut öffnen. Beide Shell-Einstellungsmatrizen prüfen auch den tatsächlichen Einstellungsdialog, Abbrechen und angewendete Taskleistensichtbarkeit. |
| Programmupdate | `reviewed-valid.log`: je 34 Updaterprüfungen, gültiger Bundlemarker und ungültige Offsets, tatsächlicher EXE-Austausch mit Neustart sowie erkannte Manipulation und ungültige EXE. Beide GUI-Architekturen zusätzlich mit HTTP-Fehler, Downloadabbruch und bestätigter Übergabe geprüfter Paketbytes über den tatsächlichen Updatebutton. |
| Kern und Migrationen | `reviewed-valid.log`: x64 365 Prüfungen einschließlich realer SVN-Strecke, x86 297 Kernprüfungen. Sämtliche INI-Generationen, fehlerhafte Felder, unbekannte Version, Clientmigration mit/ohne gebündelte SVN-EXE, portable Kontexttrennung, natürliche Sortierung, Pfadkürzung, Datei-/Ordnertyp, Duplikatvermeidung sowie Import-/Exportfehler und Konfigurationserhalt. |
| Reale SVN-Strecke | `reviewed-valid.log`: 365 Prüfungen mit echtem SVN/TortoiseSVN auf isolierten Arbeitskopien für x64, Exit-Code 0, einschließlich Konkurrenzschutz und Freigabe bei langen Reservierungspfaden. |
| Startmodi | `reviewed-valid.log`: beide Architekturen starten mit richtiger INI trotz abweichendem Aufrufordner. Hilfsmodus lädt weder XAML noch Windows-App-Runtime und weist ungültige Übergabe mit Exit-Code 1 ab. WOW64-Module werden berücksichtigt; kurzzeitig instabile Modullisten werden gemäß Toolhelp-Vertrag erneut aufgenommen. |
| WinForms-zu-WinUI-Update | `legacy-transition.log`: beide Architekturen von 3.0.0.13 auf 3.0.0.14, richtige INI und Repository-Zuordnung, Stagingbereinigung und Einstellungsdialog, Exit-Code 0. |
| Windows-Shell | Lauf `validated-product/native-shell/e72d899ee2944bcab6ff5becc7e169dc/test.log`: Produktstart, Kontextarten, stilles Update, Aktivierungsweiterleitung, Leertaste, deutsche Menüs und Prüfintervalle beobachtet. Wiederholung ab 13 Uhr unter `portable-contract-clean/native-shell/3a17cfd67f7f42dc90bf2563923b0999/test.log`: API-Annahme und eigene Prozessbereinigung, aber gesperrte Sitzung. `input-session-current.txt` identifiziert `LockApp`. Banner, Historie, Tray-Menü und DPI-Wechsel sind damit nicht nachgewiesen. |
| Prozessbereinigung | `own-test-processes-after-notification-switch.txt`, 12.11.2026 um 14:42:17 Uhr: null eigene Notifier-, GUI-, SVN- oder Updater-Testprozesse nach den beendeten Matrizen. |
| Kurzer entsperrter Desktoplauf | `reviewed-valid/native-shell/5b695b542a484951af339f1cd49949c5/test.log`: 29 erfolgreiche Prüfungen, sieben fehlgeschlagene Szenarien, drei ausdrücklich ausgelassene Systemänderungen; Gesamt-Exit-Code 1. Leertaste, deutsche Menükürzel, gespeicherte Prüfintervalle, Kontextweiterleitung, Sitzungsbeendigung am eigenen Fenster und neue EXE-/INI-Zuordnung nach Ordnerwechsel beobachtet. Banner, Historienklick, Drag-and-drop, Tray-Erkennung und COM-Neustart bleiben ohne erfolgreichen Nachweis. Reguläre Bereinigung und null eigene Testprozesse anschließend nachgewiesen. |
| Eingabetaste | `keyboard-route.log` reproduziert den Fehler mit einer ausschließlich an das eigene Testfenster gesendeten Windows-Eingabenachricht: `ListViewItem` markiert Eingabe als verarbeitet; kein Prozessauftrag. Die Ereignisanbindung berücksichtigt deshalb verarbeitete Ereignisse. `keyboard-route-fixed.log`: 37 Prüfungen über die konfigurierten Aktionen, Exit-Code 0. Der weitere sichtbare Kurzlauf bestätigt Eingabe mit tatsächlichem Prüfauftrag, Entf mit Rückfrage und Einfg mit korrekt zugeordnetem Ordnerdialog. |
| Benachrichtigungsschalter | `notification-switch-transfer.log`: je 316 Kernprüfungen für x64/x86, Exit-Code 0; fehlender INI-Wert, ausgeschaltet speichern/neu laden, Import/Export samt unverändertem Schalter bei Importfehler, alle drei Meldungsarten und weiterlaufende SVN-Prüfung. `notification-counterexample.log`: entfernte Sperre in separat kompiliertem Fixture erkannt; regulärer Quelltext unverändert, Exit-Code 0. |
| GUI mit Benachrichtigungsschalter und Eingabekorrektur | `notification-switch-product.log`, Exit-Code 0: beide Pakete gebaut; je vier GUI-Varianten mit 487 Prüfungen, je 16 erkannte Gegenproben einschließlich verlorenem Schalter, Startmodi beider Architekturen sowie je 37 Prüfungen der Eingabetastenaktionen. Die allgemeine Einstellungsseite mit neuem Schalter wurde in Deutsch und Englisch gerendert und angesehen. |
| Weiterer sichtbarer Kurzlauf | `notification-switch-product/native-shell/9aa197aa36c24f0b9727d928ad0ff87b/test.log`: 37 erfolgreiche Prüfungen, sechs fehlgeschlagene Szenarien, drei ausgelassene Systemänderungen; Gesamt-Exit-Code 1. Banner, Historie, gemischter OLE-Drop, Tray-Erkennung, Aktivierung nach Prozessende und neue Banner nach Ordnerwechsel besitzen keinen erfolgreichen Nachweis. Alle eigenen Instanzen wurden regulär geschlossen. |
| Begrenzte Shell-Diagnose | `notification-switch-product/native-shell/69103d7cb3f146fe931abbaae20ecc40/test.log`, Exit-Code 0: Die UI-Automation findet vor und nach dem Öffnungsversuch keinen sichtbaren Benachrichtigungsbereich. Der eigene Pfadmarker erscheint nur im eigenen Hauptfenster. `SetForegroundWindow` liefert in den Diagnoseversuchen false. Das belegt weder einen Meldungsklick noch einen Zustellungsfehler im Produkt. |
| Shell-Teststarter nach dem Kurzlauf | `native-script-after-short-run.log`, Exit-Code 0: PowerShell-Syntax und beide C#-Helfer kompiliert, unbekannte Tray-Zuordnung abgewiesen und Aufruf außerhalb des Zeitfensters vor Verzeichnis- und Prozessstart abgelehnt. Keine Desktop-Eingaben oder Systemeinstellungen verändert. Die Änderung am Fokusziel benötigt noch den tatsächlichen Aktivierungslauf. |
| Aktuelle Prozesskontrolle | `own-process-check-after-short-run.log`, Exit-Code 0, und `own-test-processes-current.txt`, 12.11.2026 um 14:54:38 Uhr: null eigene Testinstanzen. Die Kontrolle verwendet ausschließlich ausführbare Dateien unter der eigenen Artefaktwurzel. |
| Erneuter Kurzlauf | `notification-switch-product/native-shell/8a00e8817d4441e4b311a03f89014300/test.log`: 33 erfolgreiche Prüfungen, sechs fehlgeschlagene Szenarien, drei ausgelassene Systemänderungen; Exit-Code 1. Eingabe, Entf und Einfg bestehen erneut. Drag-and-drop und Aktivierung nach Prozessende erreichen wegen fehlendem Vordergrund des eigenen Fensters bereits die nächste Testhandlung nicht. Banner, Historie und Tray-Erkennung schlagen weiterhin fehl. `own-process-check-1457.log`, Exit-Code 0: null eigene Testinstanzen um 14:57:29 Uhr. |

## Benutzerentscheidungen und offene Nachweise

- K-01: Windows steuert die Banneranzeigedauer. Die Einstellung verweist auf Windows; gespeicherte INI-Werte und Migrationen bleiben erhalten.
- K-02: Normaler Betrieb ohne Administratorrechte genügt. Ein erhöhter Produktstart wird mit konkreter Fehlermeldung abgelehnt. Kein zusätzlicher Benachrichtigungsprozess.
- K-04: Mindestversion Windows 10 1809, Projekt-Minimum Build 17763.
- K-05: Alte Meldungen dürfen nach Verschieben des portablen Ordners ungültig werden. Neue Meldungen müssen nach regulärem Start den neuen EXE-Pfad und die richtige Konfiguration verwenden. Kein installierter Starthelfer.
- Offen sind reale Banner, Benachrichtigungszentrale, COM-Aktivierung nach Prozessende, Windows-Meldungsschalter, DPI-Wechsel, Tray und neue Shellzustellung nach Ordnerwechsel.
- Die Tray-Suche verwendet die native Zuordnung über eigenes HWND und Symbol-ID. Der hintere Pfadmarker kann bei langen Tooltiptexten durch die Windows-Begrenzung fehlen. Kompilierung und Ablehnung eines unbekannten Symbols sind geprüft; im weiteren sichtbaren Lauf schlägt auch die positive Zuordnung fehl. Kein fremdes Tray-Symbol darf ersatzweise angeklickt werden.
- Das Aktivierungsszenario nach Prozessende scheitert bereits beim Fokussieren der Listenwurzel. Der Test verwendet dafür nun denselben ausgewählten Listeneintrag wie die bestandene Eingabetastenprüfung. Die Fortsetzung dieses Szenarios einschließlich tatsächlichem Meldungsklick ist noch ungeprüft. Für den OLE-Drop ist noch nicht belegt, ob die Testquelle tatsächlich den Ziehvorgang begonnen hat; daher keine unbelegte Änderung am Produkt-Drophandler.
- Ein sauberer Windows-Rechner ohne vorinstallierte Laufzeiten steht nicht zur Verfügung. Die Prüfung eigener Laufzeitmodule ersetzt diesen Nachweis nicht.
- Checkbox-Aufnahmen über `RenderTargetBitmap` zeigen Striche. `checkbox-states-counterprobe.log` bestätigt den passenden booleschen Wert und SDK-Zustand sowie eine erkannte Gegenprobe mit `NormalIndeterminate`. Die tatsächliche Bildschirmaufnahme `reviewed-valid/native-shell/5b695b542a484951af339f1cd49949c5/keyboard-start.png` zeigt korrekten Haken und leere Checkbox; sie wurde angesehen.
- Native Import-/Exportauswahl und Rückfrage, tatsächliches Öffnen im Explorer sowie weitere Eingabewege sind gemäß Paritätsmatrix zu ergänzen. Import-/Exportkern und zwischenzeitlich entfernte Einträge besitzen eigene Regressionstests. Keine Zeile gilt ohne eigenen Nachweis als erfüllt.
