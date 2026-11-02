# SVN-Arbeitskopien koordinieren

## Verhalten der Anwendung

Automatische SVN-Updates benötigen eine aktivierte Checkbox in der Spalte **Auto-Update**. Neue Einträge und ältere INI-Einträge ohne gespeichertes Auto-Update-Feld beginnen ausgeschaltet. Bereits gespeicherte Werte bleiben erhalten. Ein gespeicherter eingeschalteter Wert bleibt deshalb auch nach einem Programmupdate eingeschaltet. Die Versionsprüfung der Anwendung ist eine getrennte Einstellung.

Manuelles **Aktualisieren** bleibt bei ausgeschaltetem Auto-Update verfügbar und verändert die Checkbox nicht. TortoiseSVN erhält seine Schalter im Format `/path:"Wert"`, da sein eigener Befehlszeilenparser andere Anführungszeichen erwartet als der SVN-Kommandozeilenclient. Pfade mit Leerzeichen und abschließendem Verzeichnistrenner bleiben erhalten.

Status, Info, stille Updates und vom Notifier gestartete TortoiseSVN-Dialoge für Update, Commit und Protokoll reservieren die Arbeitskopie vor dem Prozessstart. Die Reservierung gilt bis zum tatsächlichen Prozessende, unabhängig vom eingestellten Prüfintervall und von später entfernten Listeneinträgen. Eine belegte Arbeitskopie startet keinen weiteren teilnehmenden Vorgang. Der vorhandene Status und ausstehende Änderungsbenachrichtigungen bleiben erhalten. Hintergrundprüfungen warten vor einem erneuten Versuch mindestens 15 Sekunden. Übersprungene manuelle Aktionen werden nicht automatisch als Commit oder Dialog nachgeholt; sie können nach Freigabe erneut ausgelöst werden.

Die Identität ist die nächste übergeordnete Arbeitskopiewurzel mit `.svn/wc.db`. Die Datenbank wird dabei nicht geöffnet. Windows-Verzeichnishandles vereinheitlichen auch Junctions, Laufwerksaliase und UNC-Pfade; das Dateisystem berücksichtigt unterschiedliche Groß-/Kleinschreibung. Eingebettete Arbeitskopien reservieren zusätzlich alle äußeren Arbeitskopiewurzeln. Dadurch teilen Unterordner derselben Arbeitskopie und darin liegende Externals die äußere Reservierung. Arbeitskopien im alten SVN-Format ohne zentrale `wc.db` werden mit einem Koordinationsfehler abgewiesen.

Beim normalen Beenden stoppt die Anwendung neue Aufträge, bricht laufende Leseprüfungen ab und wartet auf bereits gestartete Schreibvorgänge und Dialoge. Schreibende Prozesse werden nicht wegen eines Programmendes oder Status-Zeitlimits gewaltsam beendet. Auch bei Timeout wird erst nach dem nachgewiesenen Ende des Leseprozesses freigegeben. Ein voller Ausgabe- oder Fehlerstream blockiert das Zeitlimit nicht.

## Gemeinsamer Aufrufvertrag

Der Notifier verwendet keine Windows-Mutex-Sperre für diesen Zweck. Er legt in jeder betroffenen Wurzel exklusiv die Datei `.svn/svnnotifier-operation-v1.lock` an. Windows verwendet `FileMode.CreateNew`, der mitgelieferte Python-Wrapper `O_CREAT | O_EXCL`. Eine vorhandene Datei ist immer belegt, unabhängig von Inhalt, Alter oder PID. Andere Fehler beim Ermitteln oder Anlegen der Reservierung erlauben ebenfalls keinen Prozessstart. Es gibt keinen unkoordinierten Ersatzpfad.

Die UTF-8-Datei enthält JSON mit `protocol: 1`, einem zufälligen `token`, `ownerPid`, `action` und `startedUtc`. Die PID dient ausschließlich der Diagnose. Sie wird niemals plattformübergreifend zur Freigabe ausgewertet. Es gibt keine Ablaufzeit und keine automatische Übernahme vermeintlich veralteter Reservierungen. Ein Teilnehmer entfernt nur seine eigene unveränderte Datei, nachdem sein Prozess beendet ist. Die Datei ist unabhängig von den Sperren und Journaldateien, die SVN und SQLite selbst verwenden.

Für teilnehmende externe Aufrufe steht [tools/svn-coordinated.py](../tools/svn-coordinated.py) zur Verfügung. Voraussetzung ist Python 3. Im Container beispielsweise:

```bash
python3 /pfad/zu/svn-coordinated.py \
  --working-copy /workspaces/klimm --action commit -- \
  svn commit /workspaces/klimm -m "Commit-Nachricht"
```

Unter Windows, wenn Python 3 installiert ist:

```powershell
python .\tools\svn-coordinated.py --working-copy D:\Entwicklung\Anwendungen\klimm --action update -- svn.exe update D:\Entwicklung\Anwendungen\klimm
python .\tools\svn-coordinated.py --working-copy D:\Entwicklung\Anwendungen\klimm --action commit -- 'C:\Program Files\TortoiseSVN\bin\TortoiseProc.exe' /command:commit /path:D:\Entwicklung\Anwendungen\klimm
```

Dies sind Aufrufbeispiele, keine im KLIMM-Projekt vorgenommenen Änderungen. Für den Container-Aufrufweg müssten alle beteiligten Werkzeuge den Wrapper vor dem eigentlichen SVN-Prozess verwenden. Das Arbeitsverzeichnis des Aufrufers bleibt erhalten. Das gestartete Programm muss im Vordergrund bleiben, bis sein Zugriff beendet ist. Kein `&`, kein `Start-Process` ohne Warten und keine abgekoppelten Hintergrundprozesse innerhalb des Wrappers. Bei mehreren betroffenen Arbeitskopien muss `--working-copy` für jede Arbeitskopie angegeben werden. Der Aufrufer muss sämtliche Ziele seiner Befehlszeile und gegebenenfalls zusätzlich erreichte Arbeitskopien deklarieren. Der Wrapper interpretiert keine beliebigen SVN- oder TortoiseSVN-Argumente.

Alle Teilnehmer müssen dieselbe physische `.svn`-Ablage sehen und dürfen die Koordinationsdatei weder ausblenden noch kopieren, zurücksetzen oder entfernen. Exit-Code `75` bedeutet belegte Koordination, `74` einen Koordinations- oder Startfehler. Der eigentliche Befehl läuft in diesen Fällen nicht. Bei einem gestarteten Befehl wird dessen Exit-Code weitergegeben. Der Wrapper wiederholt keine schreibenden Aktionen automatisch. Für zeitgesteuerte Aufrufer ist ein erneuter Versuch frühestens nach 15 Sekunden vorzusehen.

## Grenzen und Fehlerfälle

Direkte Aufrufe von `svn`, TortoiseSVN aus dem Explorer, IDE-Integrationen, ältere Notifier-Ausgaben und andere Werkzeuge ohne dieses Protokoll werden nicht erfasst. Auch separat gestartete oder abgekoppelte Vorgänge aus einem TortoiseSVN-Dialog können außerhalb der beobachteten Prozesslebensdauer liegen. Der Notifier kann keine beliebigen externen SVN-Aufrufe gegenseitig ausschließen. Das Protokoll ersetzt nicht die internen SVN-/SQLite-Sperren und repariert keine Dateisystem- oder Datenbankfehler.

Bei Absturz, erzwungenem Prozessende, Stromausfall oder fehlgeschlagener Freigabe kann die Datei bestehen bleiben. Das verhindert weitere teilnehmende Aufrufe. Es gibt absichtlich weder automatisches Cleanup noch automatisches Löschen fremder Dateien. Eine manuelle Wiederherstellung erfordert vorher die Prüfung auf allen beteiligten Systemen, dass kein zugehöriger SVN-Prozess und kein abgekoppelter Vorgang mehr läuft. Eine lokale PID-Prüfung oder das Alter der Datei genügt dafür nicht. SVN-interne Sperren und `wc.db` dürfen dabei nicht verändert werden.

Exklusives Anlegen und Sichtbarkeit hängen vom tatsächlich eingesetzten Dateisystem und Mount ab. Eine erfolgreiche Probe beweist keine allgemeine Zuverlässigkeit von 9p/DrvFS oder von SQLite auf diesem Mount. Bei anderen Mounts, mehreren Linux-Clients, Cache-Einstellungen oder Container-Konfigurationen muss die gegenseitige Ausschließung erneut geprüft werden. Siehe auch die [SQLite-Hinweise zu Netzwerkdateisystemen](https://www.sqlite.org/useovernet.html).

## Diagnose

Die bestehende Logdatei neben der verwendeten INI-Datei enthält unter `SvnOperation` JSON-Ereignisse mit UTC-Zeit, Arbeitskopiewurzel, Aktion, Phase, Prozess-ID, Exit-Code und Grund. Phasen sind `reserve`, `begin`, `end`, `skip` und bei fehlgeschlagener Freigabe `release-error`. Vor einem tatsächlichen Start bleiben Prozess-ID und Exit-Code leer. Der Wrapper gibt dieselben Diagnosefelder auf stderr aus. Befehlszeilen, Umgebungsvariablen und SVN-Ausgaben werden nicht in diese Diagnose übernommen. Die normale Konsolenausgabe eines vom Wrapper gestarteten Werkzeugs bleibt dessen eigene Ausgabe.

## Prüfungen

Die Windows-Konsole `tests/Svn.Tests` verwendet dieselben Produktklassen wie die Anwendung. Sie prüft Opt-in und INI-Erhalt, Unterordner und normalisierte Pfade, eingebettete Arbeitskopien, konkurrierende und verzögerte Starts, externe Belegung, Start- und Prozessfehler, volle Streams, Timeout, Abbruch, Programmende, Diagnosefehler und Freigabe. TortoiseSVN-Aufrufe werden zusätzlich mit Leerzeichen, Umlauten und abschließendem Verzeichnistrenner sowie mit ein- und ausgeschaltetem Auto-Update geprüft. Ein optionaler echter SVN-Lauf verwendet ausschließlich ein eigenes lokales Repository und zwei isolierte Arbeitskopien:

```powershell
powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier win-x64 -ArtifactsDirectory artifacts/svn-check -SvnDirectory 'C:\Program Files\TortoiseSVN\bin'
```

Ohne `-SvnDirectory` laufen die Prozess- und Konfigurationstests mit synthetischen Arbeitskopiemarkierungen und eigenen Testprozessen. In GitHub Actions laufen diese für x64 und x86. Die GUI-Prüfungen testen zusätzlich sichtbare Checkboxzustände, Maus und Leertaste sowie Änderungsbenachrichtigungen bei ausgeschaltetem Auto-Update. Der Linux-Job prüft den Wrapper einschließlich echter SVN-Aktionen gegen ein lokales Test-Repository. Die Windows-Jobs prüfen den Python-Wrapper zusätzlich auf gegenseitigen Ausschluss und Fehlerverhalten.

Der lokale Plattformtest wurde unter Windows mit .NET 10 und im separaten Debian-Testcontainer mit Python 3.11 und SVN 1.14.2 auf derselben Windows-Ablage über `9p`/`v9fs` ausgeführt. Beide Belegungsrichtungen und zehn gleichzeitige Startversuche wurden geprüft. Unter WSL lief zusätzlich der Wrapper mit SVN 1.14.3. Windows-SVN 1.15.0 wurde gegen ein separates lokales Test-Repository geprüft. Echte TortoiseSVN-Protokoll-, Update- und Commitdialoge wurden auf dieser Test-Arbeitskopie geöffnet und geschlossen beziehungsweise abgebrochen; die Reservierung wurde jeweils erst nach Prozessende freigegeben. Das manuelle Update übernimmt bei ausgeschaltetem Auto-Update eine eingehende Änderung in einem Pfad mit Leerzeichen. Weder KLIMM-Quelltexte noch dessen Arbeitskopiedatenbank wurden dafür verwendet.

Nicht geprüft ist der konkrete KLIMM-DevContainer mit seinem tatsächlichen Aufrufweg. Dort sind nach Integration des Wrappers beide Belegungsrichtungen und gleichzeitige Starts auf einer isolierten Test-Arbeitskopie mit den tatsächlichen Mount- und Cache-Einstellungen zu wiederholen. Die Tests liefern keinen neuen Ursachennachweis für den gemeldeten SQLite-I/O- und Rollback-Fehler.
