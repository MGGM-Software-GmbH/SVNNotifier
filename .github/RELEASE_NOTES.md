SVN Notifier 3.0.0.10

- Der Programmupdater sperrt vor der Installation neue SVN-Aufträge und beendet alle vom Notifier gestarteten SVN-Prozesse samt Unterprozessen. Auch laufende stille Updates und TortoiseSVN-Dialoge werden beendet.
- Der Installer startet erst nach dem Ende der eigenen Prozesse und der Hintergrundprüfung. Bei nicht bestätigtem Prozessende wird die Installation abgebrochen. Nach einem Installationsfehler wird die SVN-Verarbeitung wieder freigegeben.
- Extern gestartete SVN- und TortoiseSVN-Prozesse bleiben unberührt. Das normale Beenden der Anwendung wartet weiterhin auf Schreibvorgänge.
- Regressionstests prüfen laufende Lese-, Schreib- und Dialogprozesse, Unterprozesse, verzögerte Starts, Fehler und den tatsächlichen WinForms-Installationsübergang. Schreibende SVN-Tests verwenden ausschließlich isolierte Arbeitskopien und lokale Test-Repositories.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.10/docs/SVN-Koordination.md) beschreibt die interne Reservierung und ihre Grenzen. Der Notifier verändert weder den TortoiseSVN-Statuscache noch Arbeitskopiedatenbanken und führt keine automatischen Cleanups aus. Python wird nicht benötigt.

Die automatische Beendigung eigener SVN-Vorgänge gilt ab dieser Ausgabe. Beim Update von einer älteren Version müssen deren laufende Notifier-SVN-Vorgänge einmal vorher beendet werden. Alternativ die neue portable Ausgabe manuell installieren.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
