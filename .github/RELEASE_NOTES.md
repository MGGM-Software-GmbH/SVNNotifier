SVN Notifier 3.0.0.11

- **SVN Cleanup...** im Repository-Kontextmenü öffnet den TortoiseSVN-Auswahldialog für den ausgewählten Eintrag. Der Eintrag steht auch bei fehlerhaftem oder unbekanntem Status sowie ausgeschalteter Prüfung zur Verfügung.
- Der Cleanupdialog reserviert die Arbeitskopie bis zum tatsächlichen Prozessende und wird beim Programmupdate wie die anderen eigenen TortoiseSVN-Dialoge beendet. Die Bereinigung wird erst im Dialog bestätigt.
- Regressionstests prüfen Menüauswahl und Klick, konkurrierende Zugriffe, Auto-Update-Zustände sowie Pfade mit Leerzeichen, Umlauten und abschließendem Verzeichnistrenner. Lokale Tests mit echtem TortoiseSVN verwenden ausschließlich isolierte Arbeitskopien.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.11/docs/SVN-Koordination.md) beschreibt die interne Reservierung und ihre Grenzen. Es gibt keine automatischen Cleanups. Python wird nicht benötigt.

Die automatische Beendigung eigener SVN-Vorgänge beim Programmupdate gilt ab 3.0.0.10. Beim Update von einer älteren Version müssen deren laufende Notifier-SVN-Vorgänge einmal vorher beendet werden. Alternativ die neue portable Ausgabe manuell installieren.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
