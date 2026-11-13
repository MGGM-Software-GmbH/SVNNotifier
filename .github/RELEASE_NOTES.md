SVN Notifier 3.1.0.1

- Das Fehlerprotokoll zeigt mehrzeilige Einträge mit Zeitstempel, Arbeitskopiepfad und Fehlerursache an.
- Zeitüberschreitungen nennen das tatsächlich für den Auftrag verwendete Zeitlimit. Abbruch, Zeitüberschreitung und Prozessfehler besitzen unterschiedliche Meldungen und Diagnosegründe.
- Start- und Ausführungsfehler ohne Exit-Code erhalten eine aussagekräftige Meldung.
- Regressionstests prüfen mehrere Fehler auf Deutsch und Englisch, das Leeren und das gezielte Abschneiden nach der ersten Zeile. Prozessprüfungen decken Timeout, Abbruch, Prozessfehler und die anschließende Reservierungsfreigabe ab.

Das eingestellte SVN-Zeitlimit bleibt unverändert, standardmäßig 60 Sekunden; `0` bedeutet unbegrenzt. Schreibende SVN-Aufträge erhalten kein zusätzliches Zeitlimit. Gespeicherte Fehler werden weiterhin manuell geleert und nicht durch eine spätere erfolgreiche Prüfung entfernt.

Die [Fehlerdiagnose](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.1/docs/Fehlerdiagnose.md) beschreibt das Verhalten. Der [Umsetzungsstand](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.1.0.1/docs/prp/winui-native-benachrichtigungen/03-umsetzungsstand.md) enthält Prüfnachweise und die weiterhin offenen Integrationsnachweise für Windows-Banner, native Meldungsklicks, Tray, Drag-and-drop und Systemdarstellung.

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
