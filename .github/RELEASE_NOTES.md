SVN Notifier 3.1.0.2

- Manuelle SVN-Aktionen haben Vorrang vor automatischen Prüfungen und neuen Auto-Updates. Dies gilt für **Jetzt prüfen**, **Update**, **Commit**, **Cleanup**, beide Protokollaufrufe und **Alle aktualisieren**, auch über Symbolleiste, Doppelklick und Tray.
- Eine laufende automatische Status- oder Revisionsprüfung wird dafür unterbrochen, auch bei ausgeschaltetem SVN-Zeitlimit. Die automatische Verarbeitung wird anschließend fortgesetzt. Die Vorbereitung eines Auto-Updates blockiert den Oberflächenthread nicht.
- Wiederholte Prüfklicks werden zusammengefasst. Wegen einer belegten Arbeitskopie zurückgestellte Prüfaufträge bleiben erhalten, auch bei ausgeschalteter regulärer Prüfung.
- Bereits laufende Schreibvorgänge und TortoiseSVN-Dialoge werden nicht beendet. Eine weitere manuelle Aktion derselben Arbeitskopie wartet auf das Ende eigener Vorgänge; die gemeinsame Reservierung bleibt erhalten.
- Regressionstests verwenden echte, kontrolliert angehaltene Testprozesse für Statusprüfung, Revisionsprüfung, manuelle Dialoge, stille Updates und gemeinsame Arbeitskopien.

Das eingestellte SVN-Zeitlimit bleibt unverändert, standardmäßig 60 Sekunden; `0` bedeutet unbegrenzt. Schreibende SVN-Aufträge erhalten kein zusätzliches Zeitlimit. Gespeicherte Fehler werden weiterhin manuell geleert und nicht durch eine spätere erfolgreiche Prüfung entfernt.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.2/docs/SVN-Koordination.md) beschreibt Priorität und Reservierungen. Das mehrzeilige Fehlerprotokoll und die getrennten Timeout-, Abbruch- und Prozessfehlermeldungen bleiben erhalten. Ein gezielter Abbruch für einen manuellen Auftrag erzeugt keinen Repository-Fehler.

Die weiterhin offenen Integrationsnachweise für Windows-Banner, native Meldungsklicks, Tray, Drag-and-drop und Systemdarstellung stehen im [Umsetzungsstand](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.2/docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md).

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
