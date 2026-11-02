SVN Notifier 3.0.0.8

- SVN-Statusprüfungen, Revisionsabfragen und stille Updates erhalten absolute lokale Ziele hinter dem Optionsabschluss und mit ausdrücklich leerer Peg-Revision. Dadurch bleiben `@` und relative Namen mit führendem Minus Bestandteile des Dateinamens.
- Eine Arbeitskopie `Projekt@` wird korrekt aktualisiert, auch wenn daneben `Projekt` existiert und reserviert ist. Reservierung und SVN-Aufruf beziehen sich auf dasselbe Ziel.
- Der optionale Python-Wrapper verwendet unter Windows für `TortoiseProc.exe` den benötigten Befehlszeilenparser. Leerzeichen, Umlaute und abschließende Verzeichnistrenner bleiben erhalten; mehrdeutige Argumente verhindern den Start und geben die eigene Reservierung frei.
- Regressionstests prüfen die literalen Ziele, unveränderte benachbarte Arbeitskopien und den Windows-Wrapper. Schreibende Tests verwenden ausschließlich isolierte Arbeitskopien und lokale Test-Repositories.

Die [SVN-Koordination](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.8/docs/SVN-Koordination.md) beschreibt auch die nötige Zielübergabe für externe SVN-Aufrufe. Sie erfasst weiterhin nur teilnehmende Aufrufe. Direkte externe SVN- und TortoiseSVN-Aufrufe werden nicht erfasst. Es gibt keine automatischen Cleanups oder Änderungen an Arbeitskopiedatenbanken.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
