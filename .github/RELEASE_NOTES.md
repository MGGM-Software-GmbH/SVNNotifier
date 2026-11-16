SVN Notifier 3.1.0.6

- Fehler von Commit-, Cleanup- und Protokollaufträgen gehen beim Konfigurationswechsel oder vor einem Programmupdate nicht verloren.
- Eine belegte Konfigurationssperre wird nach höchstens zwei Sekunden als Fehler gemeldet. Die Anwendung behält ungespeicherte Optionen für einen erneuten Versuch. Auch der Repository-Schalter zeigt Sperrfehler über den vorhandenen Fehlerdialog an.
- Das SVN-Zeitlimit wird beim Start und Import gegen dieselben Grenzen wie im Einstellungsdialog geprüft: 0 bis 86400 Sekunden, 0 ohne Zeitbegrenzung. Ungültige Importe erhalten die aktive Konfiguration.
- Repository-Pfade unterstützen mehrere und wiederholte Umgebungsvariablen. Beim Speichern bleibt die ursprüngliche Angabe erhalten.
- Das Abbrechen eines TortoiseSVN-Dialogs erzeugt keinen zusätzlichen Fehlerdialog mit lediglich dem Repository-Pfad. Startfehler und unerwartete Prozessabbrüche werden weiterhin mit einer Diagnose angezeigt.

Die automatische Bereinigung temporärer Dateien und alter .NET-Entpackverzeichnisse läuft weiterhin beim Start und alle 15 Minuten und schützt laufende Instanzen.

Gezielte Regressionstests prüfen Fehler beim Konfigurationswechsel, Sperren zwischen Instanzen, Wiederholung nach Sperrfreigabe, ungültige und gültige SVN-Zeitlimits sowie das Speichern und Laden variabler Repository-Pfade. Die GUI-Prüfungen verwenden echte WinUI-Fenster und eigene Konfigurationen.

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
