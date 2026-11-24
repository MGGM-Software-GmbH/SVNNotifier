SVN Notifier 3.1.0.8

- Deaktivierte Repositories beeinflussen das gemeinsame Fenster-, Taskleisten- und Tray-Symbol nicht mehr. Ein dort gespeicherter Updatebedarf oder Fehler überlagert den Status aktiver Repositories nicht.
- Sind alle aktiven Repositories aktuell, bleibt das Symbol grün. Ohne aktivierte Repositories erscheint es grau.
- Der letzte Repository-Status bleibt beim Deaktivieren gespeichert. Erneutes Aktivieren bezieht ihn wieder in die Statusanzeige ein.
- Die beiden Repository-Schalter verwenden statische Haken und leere Kästchen. Dadurch wird ihre Darstellung unabhängig vom animierten WinUI-Checkbox-Symbol beim Wiederverwenden gescrollter Zeilen. Systemfarben und native Checkbox-Bedienung bleiben erhalten.

Die GUI-Regressionstests prüfen gemischte aktive und deaktivierte Einträge, ausschließlich deaktivierte Repositories sowie Deaktivieren und erneutes Aktivieren am tatsächlichen Fenstersymbol und am gemeinsam verwendeten Tray-Symbol. Eine Liste mit 1.000 Einträgen prüft die Schalterwerte und sichtbaren Haken nach wiederholtem Scrollen; eine Gegenprobe erkennt ein falsches Minuszeichen. Die Prüfungen verwenden echte WinUI-Fenster und eigene Konfigurationen.

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
