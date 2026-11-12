SVN Notifier 3.0.0.14 (Entwicklungsbranch)

- WinUI-3-Oberfläche für Repository-Liste, Einstellungen, Eigenschaften, Protokolle, Info und Programmupdate.
- Kompakte Repository-Zeilen mit dezenten Trennlinien und integrierter Menüleiste. Die Zeilen wachsen bei größerer Schrift mit; eine zusätzliche Listenüberschrift entfällt.
- Fenster-, Taskleisten- und Tray-Symbol verwenden dieselben vorhandenen Statussymbole: Grau für unbekannt, Grün für synchronisiert, Gelb für eingehende Änderungen und Rot für Fehler. Die bisherige Priorität der Zustände bleibt erhalten.
- Native Windows-App-Benachrichtigungen für eingehende Änderungen, erfolgreiche stille SVN-Updates und Fehler der automatischen Programmprüfung. Windows steuert die Banneranzeigedauer; vorhandene INI-Werte bleiben erhalten.
- Benachrichtigungen lassen sich in den allgemeinen Einstellungen abschalten und sind standardmäßig eingeschaltet. SVN-Prüfung und Auto-Update bleiben unabhängig davon.
- Gemeinsamer UI-freier Kern für Konfiguration, SVN-Koordination und Ablaufsteuerung. Deutsch, Englisch sowie Schriftfamilie und Schriftgröße bleiben konfigurierbar.

Prüfstand und offene Integrationsnachweise stehen im [PRP-Umsetzungsstand](../docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md). Diese Hinweise beschreiben den Entwicklungsbranch, keine veröffentlichte Ausgabe.

Der geplante [Merge- und Releaseablauf](../docs/prp/winui-native-benachrichtigungen/04-merge-und-release.md) nennt Voraussetzungen, Windows-Prüfungen, Versionsangaben und Tag-Auslösung. Voraussetzung für WinUI sind Windows 10 Version 1809 oder neuer und ein normaler Start ohne Administratorrechte. Nach einem portablen Ordnerwechsel einmal am neuen Ort starten; alte Meldungen dürfen ungültig werden. Die Vorwärtsaktualisierung von WinForms 3.0.0.13 ist für x64 und x86 mit EXE-Austausch, Neustart und erhaltener Konfiguration geprüft.

Beibehaltene SVN-Koordination:

- Verwaiste Notifier-Reservierungen werden beim Start und vor späteren SVN-Aufträgen automatisch freigegeben. Auch das alte Reservierungsformat wird berücksichtigt.
- Aktive Instanzen und nach einem Absturz weiterlaufende SVN-Prozesse bleiben geschützt. Anschließend erfolgt die Freigabe bei einer späteren Prüfung. Rechnerkennung und Prozessstartzeiten unterscheiden neue Reservierungen auch bei wiederverwendeten Prozess-IDs.
- Beim Beenden erfolgt die Freigabe nach dem tatsächlichen Prozessende auch über den Fehlerpfad des Nachrichtenloops. Die Prüfung und Freigabe verwenden dasselbe Windows-Dateihandle und vermeiden ein Rennen mit einem Dateiaustausch.
- Regressionstests erzeugen echte Besitzerabbrüche und überlebende SVN-Testprozesse. Weitere Gegenproben prüfen alte Dateien, gleichzeitige Übernahmen, Prozessstartfenster und Freigabefehler.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.13/docs/SVN-Koordination.md) beschreibt die Wiederherstellung und ihre Grenzen. Extern gestartete SVN-, TortoiseSVN- und Container-Aufrufe werden weiterhin nicht koordiniert. Es gibt keine automatischen SVN-Cleanups oder Änderungen an Arbeitskopiedatenbanken. Python wird nicht benötigt.

Die automatische Beendigung eigener SVN-Vorgänge beim Programmupdate gilt ab 3.0.0.10. Beim Update von einer älteren Version müssen deren laufende Notifier-SVN-Vorgänge einmal vorher beendet werden. Alternativ die neue portable Ausgabe manuell installieren.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
