SVN Notifier 3.1.0.4

- Automatische Bereinigung beim Start und alle 15 Minuten: verwaiste Programmupdate-Verzeichnisse, atomare INI-Schreibdateien, ältere Importreste, alte Startfehlerprotokolle und abgelaufene Benachrichtigungskontexte.
- Alte .NET-Entpackverzeichnisse der portablen Anwendung werden ebenfalls aufgeräumt. Das aktuelle Verzeichnis und aktive Dateien bleiben geschützt. Alle Dateien werden vor der ersten Löschung auf exklusive Verfügbarkeit geprüft; Verknüpfungen und unerwartete Inhalte in Update-Verzeichnissen verhindern deren Bereinigung.
- Downloads und Installer reservieren ihre Übergabedateien über eine gemeinsame Sperrdatei. Ein Prozessabsturz gibt diese Sperre frei. Mehrere aktuelle Notifier-Instanzen schützen ihre verwendeten Entpackverzeichnisse ebenfalls über Sperrdateien.
- Nicht markierte Verzeichnisse älterer Ausgaben bleiben erhalten, solange eine weitere Notifier- oder Installer-Instanz läuft. Installer-Caches benötigen einen Herkunftsnachweis des Notifiers. Dauerhafte INI-Dateien, Fehlerprotokolle neben der INI und Benutzer-Repositories werden nicht bereinigt.

Startfehlerprotokolle und Benachrichtigungskontexte bleiben sieben Tage erhalten; Startprotokolle noch laufender Prozesse werden weiter geschützt. Ältere Importreste ohne Zielzuordnung werden nach einem Tag berücksichtigt. Fehler beim Bereinigen werden protokolliert und verhindern nicht die Wartung anderer Bereiche.

Regressionstests prüfen aktive und abgestürzte Prozesse, geladene native Bibliotheken, fremde Dateien, Verzeichnisverknüpfungen, mehrere Instanzen und verwaiste Schreibdateien. Absichtlich defekte Varianten prüfen die Schutzregeln und die tatsächliche Bereinigung.

Weitere Befunde der erneuten allgemeinen Fehlerprüfung sind im [Umsetzungsstand](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.4/docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md) dokumentiert. Dieses Release setzt die beauftragte temporäre Dateibereinigung um.

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
