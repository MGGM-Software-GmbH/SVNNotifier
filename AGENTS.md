# SVN Notifier: Projektregeln

Projektbeschreibung, Buildbefehle und Releaseablauf stehen in [README.md](README.md).

## Artefakte immer aufräumen

Am Ende jeder Aufgabe müssen die erzeugten Artefakte dieses Projekts selbstständig aufgeräumt werden. Das gilt für Build-, Test-, Publish- und Paketdateien in `artifacts/`, `bin/` und `obj/` sowie für ausgelagerte temporäre Arbeitsverzeichnisse unter Windows und Linux. Keine vollständigen Build- oder Testbäume als Archiv zurücklassen.

- Vor umfangreichen Läufen den freien Speicher prüfen und vorhandene entbehrliche Projektartefakte berücksichtigen.
- Laufende Prüfungen und Veröffentlichungen abwarten; anschließend deren Zwischenstände und lokale Paketkopien entfernen. Benötigte Releasepakete erst nach erfolgreicher Veröffentlichung und Prüfung der öffentlichen Downloads löschen.
- Nur kompakte Prüflogs, Ergebnisdateien und erforderliche einzelne Nachweise behalten. Umfangreiche Screenshotsammlungen, Testarbeitskopien, Quellkopien für Gegenproben und Binärdateien entfernen.
- Löschziele vorab als konkrete erzeugte Projektverzeichnisse prüfen. Verknüpfungen nicht verfolgen. Quellen, Versionskontrollmetadaten, Benutzerkonfigurationen und Benutzer-Repositories erhalten.
- Die Bereinigung und verbleibende notwendige Nachweise im Ergebnis kurz benennen. Fehlgeschlagene oder blockierte Bereinigungen ausdrücklich melden.
