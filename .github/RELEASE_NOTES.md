SVN Notifier 3.0.0.12

- Der Repository-Kontextmenüeintrag heißt in Deutsch und Englisch **Cleanup**. Er öffnet den TortoiseSVN-Auswahldialog für den ausgewählten Eintrag.
- GUI-Regressionstests prüfen die genaue Beschriftung in beiden Sprachen sowie den Menüaufruf und die Reservierung der Arbeitskopie.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.12/docs/SVN-Koordination.md) beschreibt die interne Reservierung und ihre Grenzen. Es gibt keine automatischen Cleanups. Python wird nicht benötigt.

Die automatische Beendigung eigener SVN-Vorgänge beim Programmupdate gilt ab 3.0.0.10. Beim Update von einer älteren Version müssen deren laufende Notifier-SVN-Vorgänge einmal vorher beendet werden. Alternativ die neue portable Ausgabe manuell installieren.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
