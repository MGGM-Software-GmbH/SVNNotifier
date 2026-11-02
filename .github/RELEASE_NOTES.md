SVN Notifier 3.0.0.7

- Manuelles Aktualisieren über TortoiseSVN erhält Pfade im vom TortoiseSVN-Parser benötigten Format. Damit werden Pfade mit Leerzeichen korrekt übergeben.
- Die korrigierte Übergabe gilt ebenfalls für Commit- und Protokolldialoge sowie Pfade mit Umlauten und abschließendem Verzeichnistrenner.
- Manuelles Aktualisieren funktioniert bei ein- und ausgeschaltetem Auto-Update und verändert die Checkbox nicht. Die arbeitskopieweite Reservierung bleibt bis zum tatsächlichen Prozessende bestehen.
- Regressionstests für Pfadformen, beide Auto-Update-Zustände sowie abgewiesene ungültige Argumente mit anschließender Freigabe. Lokale Windows-Prüfungen für x64 und x86 enthalten ein echtes manuelles TortoiseSVN-Update auf einer isolierten Arbeitskopie.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.7/docs/SVN-Koordination.md) erfasst weiterhin nur teilnehmende Aufrufe. Direkte externe SVN- und TortoiseSVN-Aufrufe werden nicht erfasst. Es gibt keine automatischen Cleanups oder Änderungen an Arbeitskopiedatenbanken.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
