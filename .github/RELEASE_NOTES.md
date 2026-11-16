SVN Notifier 3.1.0.5

- Protokollierungsfehler nach dem Start eines SVN-Prozesses verhindern weder Timeout noch Benutzerabbruch oder Stoppen. Die Diagnose wird als Fehler gemeldet, die Reservierung erst nach dem tatsächlichen Prozessende freigegeben.
- Einstellungen und Repository-Optionen werden vor Beginn des Shutdowns gespeichert. Scheitert das Speichern, bleiben die Anwendung bedienbar und ein erneuter Beenden-Versuch möglich.
- Start und Konfigurationsimport weisen ungültige Zeitintervalle zurück. Globale Intervalle müssen zwischen 0 und 359999 Sekunden liegen; Repository-Intervalle erlauben zusätzlich -1 für den jeweiligen Standardwert. Ein abgewiesener Import verändert die aktive Konfiguration nicht.
- Fehlgeschlagene Commit-, Cleanup- und Protokolldialoge erhalten dieselbe Fehleranzeige wie fehlgeschlagene Updates.

Die automatische Bereinigung temporärer Dateien und alter .NET-Entpackverzeichnisse läuft weiterhin beim Start und alle 15 Minuten und schützt laufende Instanzen.

Gezielte Regressionstests prüfen Diagnosefehler während echter Prozessläufe, Intervallgrenzen, unveränderte Konfiguration bei ungültigen Importen, fehlende TortoiseSVN-Programme und das erneute Beenden eines echten WinUI-Fensters nach einem INI-Schreibfehler.

Voraussetzungen: Windows 10 Version 1809 oder neuer, normaler Start ohne Administratorrechte sowie separat installiertes `svn.exe` und TortoiseSVN. Beide portablen ZIP-Pakete enthalten die .NET-Laufzeit. Für automatische Programmupdates ist Schreibzugriff auf den Programmordner erforderlich. Die EXE-Dateien sind nicht digital signiert; `SHA256SUMS.txt` enthält die Paketprüfsummen.
