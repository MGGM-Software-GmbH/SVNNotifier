SVN Notifier 3.0.0.13

- Verwaiste Notifier-Reservierungen werden beim Start und vor späteren SVN-Aufträgen automatisch freigegeben. Auch das alte Reservierungsformat wird berücksichtigt.
- Aktive Instanzen und nach einem Absturz weiterlaufende SVN-Prozesse bleiben geschützt. Anschließend erfolgt die Freigabe bei einer späteren Prüfung. Rechnerkennung und Prozessstartzeiten unterscheiden neue Reservierungen auch bei wiederverwendeten Prozess-IDs.
- Beim Beenden erfolgt die Freigabe nach dem tatsächlichen Prozessende auch über den Fehlerpfad des Nachrichtenloops. Die Prüfung und Freigabe verwenden dasselbe Windows-Dateihandle und vermeiden ein Rennen mit einem Dateiaustausch.
- Regressionstests erzeugen echte Besitzerabbrüche und überlebende SVN-Testprozesse. Weitere Gegenproben prüfen alte Dateien, gleichzeitige Übernahmen, Prozessstartfenster und Freigabefehler.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.13/docs/SVN-Koordination.md) beschreibt die Wiederherstellung und ihre Grenzen. Extern gestartete SVN-, TortoiseSVN- und Container-Aufrufe werden weiterhin nicht koordiniert. Es gibt keine automatischen SVN-Cleanups oder Änderungen an Arbeitskopiedatenbanken. Python wird nicht benötigt.

Die automatische Beendigung eigener SVN-Vorgänge beim Programmupdate gilt ab 3.0.0.10. Beim Update von einer älteren Version müssen deren laufende Notifier-SVN-Vorgänge einmal vorher beendet werden. Alternativ die neue portable Ausgabe manuell installieren.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
