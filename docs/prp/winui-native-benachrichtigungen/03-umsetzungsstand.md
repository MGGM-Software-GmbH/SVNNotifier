# Umsetzungsstand und Nachweise

## Auftrag und Arbeitsplan

Die Implementierung ist auf `feature/winui-native-notifications` beauftragt. Kein Merge und kein Release. Benutzeränderungen außerhalb des Umbaus bleiben erhalten. Routinemäßige GUI-Prüfungen laufen auf einem getrennten, inaktiven Windows-Desktop ohne sichtbare Konsole. Der Benutzer hat einmalig einen zusammenhängenden Testlauf auf seinem Desktop für die erforderlichen Windows-Integrationsprüfungen erlaubt. Diese Freigabe ist noch nicht verwendet; dabei geänderte Systemeinstellungen sind wiederherzustellen.

1. Ausgangsstand und Regressionen sichern; isolierten Windows-Teststart nachweisen.
2. SDK, Einzel-EXE, Startmodi und Benachrichtigungskompatibilität prüfen.
3. UI-freien Kern und Ablaufsteuerung mit vorhandener SVN-Koordination anbinden.
4. Hauptfenster und Einstellungen in WinUI umsetzen, funktional und visuell prüfen.
5. Übrige Ansichten, Tray, Aktivierung und Lebenszyklus übertragen.
6. Native Benachrichtigungen, portable Pakete und Programmupdate integrieren.
7. Paritätsmatrix, Gegenproben, Sichtprüfung und Dokumentation anhand aktueller Prüfläufe abgleichen.

## Nachweise vom 12. November 2026

Artefaktwurzel: `artifacts/winui-implementation-20261112/`. Sie enthält isolierte Testdaten und wird nicht als Produktquellcode eingecheckt.

- Reproduzierbarer Baseline-Checkout aus `3df587de4a2a0bf1396b3d85ed5d1da95c48cf3a` unter `baseline-source/`.
- Baseline x64: Paketbuild, GUI-, reale SVN-/TortoiseSVN- und Updaterprüfungen mit Exit-Code 0. `baseline-results/` enthält die zugehörigen Logs. Der sichtbare Lauf wurde auf Benutzerwunsch gestoppt; weitere Tests laufen isoliert.
- Baseline x86: Paketbuild und Updaterprüfung mit Exit-Code 0; SVN-Treiber ohne reale TortoiseSVN-Dialoge sowie GUI für 8,5/12/24 Punkt mit Exit-Code 0 auf getrenntem Desktop. Logs unter `isolated-results/baseline-win-x86-*`.
- Windows `.NET SDK 10.0.114`, Windows `10.0.26200.0`, TortoiseSVN-Kommandozeilenprogramme vorhanden. Die Tests verwenden eigene Arbeitskopien.
- `Microsoft.WindowsAppSDK` Version `2.6.5` aus dem NuGet-Index und dessen tatsächlichem Paketinhalt geprüft. Isolierter WinUI-Prüfprozess als selbstständige Einzel-EXE für x64 und x86 gebaut, gestartet und mit Exit-Code 0 beendet. `AppNotificationManager.IsSupported()` liefert im nicht erhöhten Prüfprozess `true`. Logs: `sdk-probe-build-*.log` und `sdk-probe-isolated-*.log`. Diese technischen Proben belegen noch keine Funktionsparität der Anwendung oder einen Start auf sauberem Windows.
- Testdesktop-Gegenprobe: Fenster auf `SVNNotifierTests-…`, Eingabedesktop `Default`; normaler Fall Exit-Code 0, absichtlich fehlerhafter Fall Exit-Code 9 wird erkannt.
- Konsolen-Gegenprobe über `Invoke-QuietWindows.vbs`: `ConsoleVisible=False`, gültiger Fall Exit-Code 0, absichtlich fehlerhafter Fall Exit-Code 9. Keine Zugangsdaten im Startweg.
- Gemeinsame Quellen sind in `SVN_Notifier.Core` explizit eingebunden und aus der doppelten Kompilierung im UI-Projekt entfernt. Updater/Installer verbleiben wegen ihres Versionsvertrags im Anwendungsprojekt. Die unbeteiligte Benutzeränderung an `WindowsPathComparer.cs` bleibt am vorhandenen Ort erhalten.
- Der erste Pakettest der Kernauslagerung erkannte eine zusätzlich publizierte Core-PDB. Die Release-Konfiguration des Kernprojekts unterdrückt diese Datei, damit der bestehende Paketvertrag EXE/README/Lizenz erhalten bleibt. Erneute Prüfungen laufen unter `core-validation/`.
- Aktuelle x64-Kernregression: `current-core-regression-third.log`, Exit-Code 0, 287 erfolgreiche Prüfungen einschließlich echter TortoiseSVN-Arbeitskopien, produktivem Coordinator-Installationsübergang und stabilen Benachrichtigungskontexten. Spätere Änderungen an Fehlerprotokoll und Installerstart benötigen erneute betroffene Prüfungen.
- `console-isolation-verified.log`: gültiger Kindprozess mit Exit-Code 0 und absichtlich fehlerhafter Kindprozess mit Exit-Code 9; beide ohne sichtbares Konsolenfenster. Argumente mit Leerzeichen, Umlauten, Anführungszeichen, abschließendem Backslash und literalen Shellzeichen bleiben entsprechend dem dokumentierten PowerShell-Parser erhalten. Der Konsolenstarter setzt `CreateNoWindow` ausdrücklich für jedes gestartete Konsolenprogramm.
- `current-regressions.log`: SVN-Prüfungen mit Exit-Code 0 für x64 (287 Prüfungen einschließlich echtem TortoiseSVN) und x86 (219 Prüfungen mit isoliertem Prozesstreiber). Die 30 Updater-Regressionen für x64 laufen durch. Der anschließende Produktneustart schlägt fehl und ist ausdrücklich kein bestandener Installationsnachweis.
- Der WinUI-Testhost verwendet die produktive Anwendungsassembly und eine eigene `ApplicationDefinition`. Keine kopierten UI-Implementierungen. Ein Konflikt der beiden `App.xbf`-Ressourcen wird durch den eigenen Testressourcennamen vermieden. Die SDK-Ressourcenauflösung wird einschließlich aktueller Prozess-ID initialisiert.
- `winui-gui-regression-x64.log`: drei Schriftvarianten in Deutsch und Englisch sowie fünf erkannte Gegenproben, Exit-Code 0. Die ergänzende Sichtprüfung hat eine unzureichende Schriftübernahme und zu breite Dialogaktionen erkannt. Diese Mängel wurden im Produktcode korrigiert; die Gegenprüfung umfasst nun gerenderte Statustexte und Eingabefelder sowie den geöffneten Befehlsüberlauf. `font-views-second.log`: 181 erfolgreiche Prüfungen mit 24 Punkt, beide Sprachen und alle sieben Ansichten, Exit-Code 0. Die übrigen Varianten und x86 müssen mit diesem Stand erneut geprüft werden.
- Der native Produktstartfehler `0xC000027B` wurde mit dem Windows-Debugger auf einen Zugriff auf globale XAML-Ressourcen im Anwendungskonstruktor eingegrenzt. Die Schriftübernahme erfolgt im geladenen Ansichtsbaum. `product-start-font-init.log` belegt danach den laufenden Produktprozess mit Hauptfenster. Die Tray-Registrierung scheitert auf dem inaktiven Desktop ohne Explorer; das ist kein Shell-Integrationsnachweis.
- `gui-matrix-current.log`: Je Architektur vier Varianten (8,5/12/24 Punkt und Dunkelmodus), jeweils Deutsch und Englisch, mit 213 erfolgreichen Prüfungen pro Variante sowie sechs erkannten Gegenproben. Die ergänzte Prüfung deaktivierter Statusanzeigen wird im aktuellen Lauf `validated-product.log` erneut ausgeführt.
- Der Entwicklungsbuild trägt in allen vier Produktversionsangaben `3.0.0.14`, damit der tatsächliche Versionswechsel vom Ausgangspaket `3.0.0.13` geprüft werden kann. Dies ist keine Veröffentlichung.

## Fachliche Entscheidungen und offene Punkte

- K-01 ist vom Benutzer entschieden: Windows steuert die Banneranzeigedauer. Die WinUI-Einstellung verweist auf Windows; gespeicherte INI-Werte und Migrationen bleiben erhalten.
- K-02 ist vom Benutzer entschieden: Normaler Betrieb ohne Administratorrechte ist ausreichend. Kein erhöhter Benachrichtigungsprozess. Ein erhöhter Produktstart erhält eine konkrete Fehlermeldung.
- K-03/K-05: Einzel-EXE-Proben sind ein früher technischer Nachweis. Produktiver Installer-/Cleanup-Modus, COM-Aktivierung, verschobene Ordner, mehrere Konfigurationen und tatsächliches Alt-zu-Neu-Update stehen aus.
- Die WinUI-Ansichten sind gebaut und im Testhost ausgeführt. Aktuelle portable x64- und x86-Produktpakete werden unter `validated-product/` geprüft. Der Produktprozess erreicht das Hauptfenster; Tray, Benachrichtigungsaktivierung und Installerneustart benötigen noch die zusammenhängende Shell-Prüfung.
- Sichtprüfung der sieben neuen WinUI-Ansichten, sauberes Windows ohne vorinstallierte Laufzeiten, DPI-/Theme-Matrix und reale Benachrichtigungszentrale stehen aus. Keine Paritätszeile gilt ohne eigenen Nachweis als erfüllt.
