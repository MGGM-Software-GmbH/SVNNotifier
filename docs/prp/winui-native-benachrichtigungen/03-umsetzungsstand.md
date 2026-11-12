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

Weitere sichtbare Testläufe sind vom Benutzer ausschließlich für den 12.11.2026 zwischen 13:00 und 14:00 Uhr Europe/Berlin erlaubt. Nach jedem Testlauf sind die dabei gestarteten eigenen Instanzen zu beenden und ihr Prozessende zu prüfen. Außerhalb dieses Zeitfensters keine sichtbaren Wiederholungen. `native-window-guard-1253.log` belegt die Ablehnung eines nicht freigegebenen Zeitpunkts vor Testverzeichnis und Prozessstart, Exit-Code 0.

## Technischer und gestalterischer Stand

- WinUI 3, Windows App SDK 2.6.5, portable Einzel-EXE für x64/x86 und Produktversionsangaben 3.0.0.14. Dies ist keine Veröffentlichung.
- Vorhandene SVN-, INI-, Reservierungs- und Updaterlogik wird gemeinsam verwendet. Die Ablaufsteuerung und Benachrichtigungskontexte sind vom Fenster getrennt.
- Listenbereich mit erkennbarer Hauptaktion, kompakteren Spalten, semantischen Statuskennzeichnungen, abgestufter Typografie und inhaltsbezogenen Dialogbreiten. Einstellungen sind fachlich gruppiert. Schrift und Sprache bleiben konfigurierbar.
- Sieben Ansichten werden auf Deutsch und Englisch mit 8,5/12/24 Punkt sowie im Dunkelmodus ausgeführt. Tatsächliche Screenshots wurden angesehen, einschließlich Updateprotokoll im Dunkelmodus und mit 24 Punkt. Dies ersetzt keinen Windows-DPI-Wechsel.
- Die Portabilität wird anhand des .NET-Bundlemarkers geprüft. Die vollständige Extraktion für WinUI ist mit einem nicht leeren Assemblypfad vereinbar und darf den Programmupdate nicht sperren.
- Fenstermaße werden auf den verfügbaren Arbeitsbereich begrenzt. Eine bestätigte Sitzungsbeendigung schließt das eigene Fenster; eine abgebrochene Beendigung gibt die Prüfung wieder frei. Beim Schließen wird das Tray-Symbol freigegeben.
- Ein Import wird einschließlich Migration zunächst in einer separaten Datei validiert. Bei ungültigen Eingaben oder gesperrtem Ziel bleiben aktive Datei, nicht gespeicherte Optionen und INI-Cache erhalten. Der Export übernimmt die gespeicherten Daten bytegenau.
- Doppelklick und sichtbare Repository-Befehle verwenden dieselbe Statusfreigabe. Deaktivierte Aktionen starten auch per Doppelklick keinen SVN-Auftrag.

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
| Prozessbereinigung | `own-test-processes-current.txt`, 12.11.2026 um 13:48:20 Uhr: null eigene Notifier-, GUI-, SVN- oder Updater-Testprozesse nach den beendeten Matrizen. |

## Benutzerentscheidungen und offene Nachweise

- K-01: Windows steuert die Banneranzeigedauer. Die Einstellung verweist auf Windows; gespeicherte INI-Werte und Migrationen bleiben erhalten.
- K-02: Normaler Betrieb ohne Administratorrechte genügt. Ein erhöhter Produktstart wird mit konkreter Fehlermeldung abgelehnt. Kein zusätzlicher Benachrichtigungsprozess.
- K-04: Mindestversion Windows 10 1809, Projekt-Minimum Build 17763.
- K-05: Alte Meldungen dürfen nach Verschieben des portablen Ordners ungültig werden. Neue Meldungen müssen nach regulärem Start den neuen EXE-Pfad und die richtige Konfiguration verwenden. Kein installierter Starthelfer.
- Offen sind reale Banner, Benachrichtigungszentrale, COM-Aktivierung nach Prozessende, Windows-Meldungsschalter, DPI-Wechsel, Tray und neue Shellzustellung nach Ordnerwechsel.
- Ein sauberer Windows-Rechner ohne vorinstallierte Laufzeiten steht nicht zur Verfügung. Die Prüfung eigener Laufzeitmodule ersetzt diesen Nachweis nicht.
- Checkbox-Aufnahmen über `RenderTargetBitmap` zeigen Striche. `checkbox-states-counterprobe.log` bestätigt für 14 sichtbare Checkboxen den passenden booleschen Wert und SDK-Zustand `NormalOn`/`NormalOff`, 18 Prüfungen sowie eine erkannte Gegenprobe mit `NormalIndeterminate`, Exit-Code 0. Die tatsächlichen animierten Haken müssen zusätzlich am entsperrten Bildschirm geprüft werden; aus dem SDK-Zustand wird keine erfolgreiche Bildschirmdarstellung abgeleitet.
- Native Import-/Exportauswahl und Rückfrage, tatsächliches Öffnen im Explorer sowie weitere Eingabewege sind gemäß Paritätsmatrix zu ergänzen. Import-/Exportkern und zwischenzeitlich entfernte Einträge besitzen eigene Regressionstests. Keine Zeile gilt ohne eigenen Nachweis als erfüllt.
