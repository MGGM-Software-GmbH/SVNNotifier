# Umsetzungsstand und Nachweise

## Auftrag und Arbeitsplan

Die Implementierung ist auf `feature/winui-native-notifications` beauftragt. Kein Merge und kein Release. Benutzeränderungen außerhalb des Umbaus bleiben erhalten. GUI-Prüfungen dürfen den Benutzerdesktop weder überlagern noch dessen Fokus übernehmen.

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

## Fachliche Entscheidungen und offene Punkte

- K-01 ist vom Benutzer entschieden: Windows steuert die Banneranzeigedauer. Die WinUI-Einstellung verweist auf Windows; gespeicherte INI-Werte und Migrationen bleiben erhalten.
- K-02: App Notifications des Windows App SDK unterstützen keine erhöhten Prozesse. Ein Nachweis für funktionsgleichen erhöhten Betrieb steht aus. Kein stilles Abschalten und kein Rückfall auf Balloons.
- K-03/K-05: Einzel-EXE-Proben sind ein früher technischer Nachweis. Produktiver Installer-/Cleanup-Modus, COM-Aktivierung, verschobene Ordner, mehrere Konfigurationen und tatsächliches Alt-zu-Neu-Update stehen aus.
- Die breite WinUI-Oberfläche, Tray-Integration und native Meldungen sind noch nicht übertragen. Die Anwendung verwendet derzeit weiter ihren vorhandenen WinForms-Startpfad. Der Kernumbau allein ist kein Nachweis für die beauftragte Migration.
- Sichtprüfung der sieben neuen WinUI-Ansichten, sauberes Windows ohne vorinstallierte Laufzeiten, DPI-/Theme-Matrix und reale Benachrichtigungszentrale stehen aus. Keine Paritätszeile gilt ohne eigenen Nachweis als erfüllt.
