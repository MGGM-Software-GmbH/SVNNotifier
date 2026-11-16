SVN Notifier 3.1.0.3

- Repository-Pfadwechsel verwerfen Ergebnisse laufender Prüfungen sowie alte Status-, URL- und Revisionsdaten. Ein Auto-Update des neuen Pfads setzt dessen eigene Prüfung voraus. Laufende manuelle Aktionen, Schreibvorgänge und Dialoge verhindern den Pfadwechsel.
- Manuelle Aktionen behalten Vorrang. Während der Wiederholfrist einer belegten Arbeitskopie dürfen unabhängige automatische Aufträge laufen. Fehlende Arbeitskopien und Koordinationsfehler beenden den manuellen Prüfauftrag mit Fehleranzeige.
- Der Konfigurationsimport sperrt neue SVN-Aufträge und wartet auf angenommene manuelle Aktionen sowie laufende eigene Prüfungen, Updates und Dialoge. Die neue Konfiguration wird getrennt validiert und erst anschließend übernommen.
- INI-Änderungen werden zwischen Notifier-Instanzen koordiniert und atomar gespeichert. Änderungen unabhängiger Schlüssel bleiben erhalten; Repository-Listen und zusammengehörige Einstellungen werden gemeinsam geschrieben.
- Beschädigte Benachrichtigungskontexte werden mit Diagnose isoliert, ohne gültige Meldungen zu blockieren. Kontextdateien einschließlich isolierter Defekte werden nach sieben Tagen entfernt. Windows-Meldungen laufen nach drei Tagen ab; ihre Klickziele bleiben darüber hinaus erhalten.

Regressionstests prüfen Pfadwechsel mit angehaltenen Prozessen, manuelle Importsperren, konkurrierende INI-Schreiber und beschädigte sowie abgelaufene Benachrichtigungskontexte. Alle SVN-Tests verwenden eigene Arbeitskopien und Konfigurationen.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.3/docs/SVN-Koordination.md) beschreibt Priorität und Reservierungen. Bestehende Fehlerprotokolle bleiben erhalten. Laufende Schreibvorgänge und TortoiseSVN-Dialoge werden für manuelle Aktionen oder einen Konfigurationsimport nicht beendet.

Die offenen Integrationsnachweise für Windows-Banner, native Meldungsklicks, Tray, Drag-and-drop und Systemdarstellung stehen im [Umsetzungsstand](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.3/docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md).

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
