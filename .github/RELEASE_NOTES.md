SVN Notifier 3.0.0.9

- Der optionale Python-Wrapper samt zugehörigen Tests und CI-Schritten ist entfernt. Python wird nicht benötigt.
- Die interne SVN-Absicherung bleibt erhalten: Vom Notifier gestartete Prüfungen und Aktionen reservieren die Arbeitskopiewurzel bis zum Prozessende, auch zwischen mehreren aktuellen Notifier-Instanzen.
- Manuelles Aktualisieren bei ausgeschaltetem Auto-Update, Änderungsbenachrichtigungen und die korrigierte Übergabe von Pfaden mit Leerzeichen, Umlauten und `@` bleiben erhalten.
- Die Windows-Regressionstests für Konfiguration, Prozesssteuerung und SVN-Ziele bleiben Bestandteil der Veröffentlichung. Schreibende lokale Tests verwenden ausschließlich isolierte Arbeitskopien und lokale Test-Repositories.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.9/docs/SVN-Koordination.md) beschreibt die interne Reservierung und ihre Grenzen. Extern gestartete SVN-, TortoiseSVN- und Container-Vorgänge werden nicht koordiniert. Der Notifier verändert weder den TortoiseSVN-Statuscache noch Arbeitskopiedatenbanken und führt keine automatischen Cleanups aus.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
