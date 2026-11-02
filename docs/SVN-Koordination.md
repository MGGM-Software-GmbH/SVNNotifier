# SVN-Arbeitskopien koordinieren

## Verhalten der Anwendung

Automatische SVN-Updates benötigen eine aktivierte Checkbox in der Spalte **Auto-Update**. Neue Einträge und ältere INI-Einträge ohne gespeichertes Auto-Update-Feld beginnen ausgeschaltet. Bereits gespeicherte Werte bleiben erhalten. Ein gespeicherter eingeschalteter Wert bleibt deshalb auch nach einem Programmupdate eingeschaltet. Die Versionsprüfung der Anwendung ist eine getrennte Einstellung.

Manuelles **Aktualisieren** bleibt bei ausgeschaltetem Auto-Update verfügbar und verändert die Checkbox nicht. TortoiseSVN erhält seine Schalter im Format `/path:"Wert"`, da sein eigener Befehlszeilenparser andere Anführungszeichen erwartet als der SVN-Kommandozeilenclient. Pfade mit Leerzeichen und abschließendem Verzeichnistrenner bleiben erhalten.

Für `svn.exe` übergibt der Notifier absolute lokale Ziele hinter dem Optionsabschluss `--` mit angehängtem `@` für eine leere Peg-Revision. Ein im Dateinamen enthaltenes `@` bleibt dadurch erhalten, und ein relativer Name wie `-r2` wird nicht als Option ausgewertet. Reservierung und SVN-Befehl beziehen sich auf dieselbe Arbeitskopie, auch wenn daneben eine ähnlich benannte Arbeitskopie ohne abschließendes `@` existiert.

Status, Info, stille Updates und vom Notifier gestartete TortoiseSVN-Dialoge für Update, Commit, Cleanup und Protokoll reservieren die Arbeitskopie vor dem Prozessstart. Die Reservierung gilt bis zum tatsächlichen Prozessende, unabhängig vom eingestellten Prüfintervall und von später entfernten Listeneinträgen. Eine belegte Arbeitskopie startet keinen weiteren teilnehmenden Vorgang. Der vorhandene Status und ausstehende Änderungsbenachrichtigungen bleiben erhalten. Hintergrundprüfungen warten vor einem erneuten Versuch mindestens 15 Sekunden. Übersprungene manuelle Aktionen werden nicht automatisch als Commit oder Dialog nachgeholt; sie können nach Freigabe erneut ausgelöst werden.

**SVN Cleanup...** im Repository-Kontextmenü öffnet mit `/command:cleanup` und `/path` den [TortoiseSVN-Auswahldialog](https://tortoisesvn.net/docs/release/TortoiseSVN_en/tsvn-automation.html). Der Eintrag ist auch bei fehlerhaftem oder unbekanntem Status und ausgeschalteter Prüfung verfügbar. Ohne ausgewählten Eintrag ist er deaktiviert. Bereinigungsoptionen werden im TortoiseSVN-Dialog ausgewählt und dort bestätigt; der Notifier übergibt keine automatischen Bereinigungs- oder Löschoptionen.

Die Identität ist die nächste übergeordnete Arbeitskopiewurzel mit `.svn/wc.db`. Die Datenbank wird dabei nicht geöffnet. Windows-Verzeichnishandles vereinheitlichen auch Junctions, Laufwerksaliase und UNC-Pfade; das Dateisystem berücksichtigt unterschiedliche Groß-/Kleinschreibung. Eingebettete Arbeitskopien reservieren zusätzlich alle äußeren Arbeitskopiewurzeln. Dadurch teilen Unterordner derselben Arbeitskopie und darin liegende Externals die äußere Reservierung. Arbeitskopien im alten SVN-Format ohne zentrale `wc.db` werden mit einem Koordinationsfehler abgewiesen.

Beim normalen Beenden stoppt die Anwendung neue Aufträge, bricht laufende Leseprüfungen ab und wartet auf bereits gestartete Schreibvorgänge und Dialoge. Schreibende Prozesse werden nicht wegen eines Programmendes oder Status-Zeitlimits gewaltsam beendet. Auch bei Timeout wird erst nach dem nachgewiesenen Ende des Leseprozesses freigegeben. Ein voller Ausgabe- oder Fehlerstream blockiert das Zeitlimit nicht.

Bei einem bestätigten Programmupdate beendet der Notifier nach erfolgreicher Paketprüfung dagegen alle von ihm gestarteten SVN-Prozesse samt Unterprozessen, auch laufende Schreibvorgänge und TortoiseSVN-Dialoge. Neue SVN-Aufträge bleiben während des Installationsübergangs gesperrt. Der Installer startet erst nach Prozessende, Freigabe der Reservierungen und Ende der Hintergrundprüfung. Auch ein bereits angenommener, aber verzögert gestarteter Prozess wird abgebrochen. Kann das Prozessende nicht innerhalb von 30 Sekunden bestätigt werden, startet der Installer nicht. Nach einem Installationsfehler sind neue SVN-Aufträge wieder zulässig; noch laufende Prozesse behalten ihre Reservierung. Es gibt keine automatischen Cleanups.

## Interne Reservierung

Der Notifier legt in jeder betroffenen Wurzel exklusiv die Datei `.svn/svnnotifier-operation-v1.lock` mit `FileMode.CreateNew` an. Mehrere aktuelle Notifier-Instanzen mit derselben physischen Arbeitskopie teilen diese Reservierung. Eine vorhandene Datei ist immer belegt, unabhängig von Inhalt, Alter oder PID. Andere Fehler beim Ermitteln oder Anlegen der Reservierung erlauben ebenfalls keinen Prozessstart. Es gibt keinen unkoordinierten Ersatzpfad.

Die UTF-8-Datei enthält JSON mit `protocol: 1`, einem zufälligen `token`, `ownerPid`, `action` und `startedUtc`. Die PID dient ausschließlich der Diagnose und wird nicht zur Freigabe ausgewertet. Es gibt keine Ablaufzeit und keine automatische Übernahme vermeintlich veralteter Reservierungen. Der Notifier entfernt nur seine eigene unveränderte Datei, nachdem sein Prozess beendet ist. Die Datei ist unabhängig von den Sperren und Journaldateien, die SVN und SQLite selbst verwenden.

## Grenzen und Fehlerfälle

Extern gestartete Aufrufe von `svn`, TortoiseSVN aus dem Explorer, der TortoiseSVN-Statuscache, IDE-Integrationen und SVN-Prozesse in Containern werden nicht koordiniert. Auch ältere Notifier-Ausgaben ohne Reservierung werden nicht erfasst. Der Notifier verändert weder den Statuscache noch dessen Einstellungen. Es wird kein Werkzeug zur Koordination externer Aufrufe mitgeliefert; Python wird nicht benötigt.

Separat gestartete oder abgekoppelte Vorgänge aus einem TortoiseSVN-Dialog können außerhalb der beobachteten Prozesslebensdauer liegen. Die Reservierung ersetzt nicht die internen SVN-/SQLite-Sperren und repariert keine Dateisystem- oder Datenbankfehler.

Bei Absturz, erzwungenem Prozessende, Stromausfall oder fehlgeschlagener Freigabe kann die Datei bestehen bleiben. Das verhindert weitere vom Notifier gestartete Aufrufe. Es gibt absichtlich weder automatisches Cleanup noch automatisches Löschen fremder Dateien. Eine manuelle Wiederherstellung erfordert vorher die Prüfung, dass kein zugehöriger Notifier-, SVN-Prozess oder abgekoppelter Vorgang mehr läuft. Eine lokale PID-Prüfung oder das Alter der Datei genügt dafür nicht. SVN-interne Sperren und `wc.db` dürfen dabei nicht verändert werden.

Die Reservierung schützt die vom Notifier gestarteten Vorgänge und liefert keine Windows-/Container-Koordination. Sie belegt auch keine Zuverlässigkeit von SQLite-Sperren auf 9p/DrvFS.

## Diagnose

Die bestehende Logdatei neben der verwendeten INI-Datei enthält unter `SvnOperation` JSON-Ereignisse mit UTC-Zeit, Arbeitskopiewurzel, Aktion, Phase, Prozess-ID, Exit-Code und Grund. Phasen sind `reserve`, `begin`, `end`, `skip` und bei fehlgeschlagener Freigabe `release-error`. Vor einem tatsächlichen Start bleiben Prozess-ID und Exit-Code leer. Befehlszeilen, Umgebungsvariablen und SVN-Ausgaben werden nicht in diese Diagnose übernommen.

## Prüfungen

Die Windows-Konsole `tests/Svn.Tests` verwendet dieselben Produktklassen wie die Anwendung. Sie prüft Opt-in und INI-Erhalt, Unterordner und normalisierte Pfade, eingebettete Arbeitskopien, konkurrierende und verzögerte Starts, externe Belegung, Start- und Prozessfehler, volle Streams, Timeout, Abbruch, Programmende, Diagnosefehler und Freigabe. TortoiseSVN-Aufrufe werden zusätzlich mit Leerzeichen, Umlauten und abschließendem Verzeichnistrenner sowie mit ein- und ausgeschaltetem Auto-Update geprüft. Ein optionaler echter SVN-Lauf verwendet ausschließlich ein eigenes lokales Repository und isolierte Arbeitskopien:

```powershell
powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier win-x64 -ArtifactsDirectory artifacts/svn-check -SvnDirectory 'C:\Program Files\TortoiseSVN\bin'
```

Ohne `-SvnDirectory` laufen die Prozess- und Konfigurationstests mit synthetischen Arbeitskopiemarkierungen und eigenen Testprozessen. In GitHub Actions laufen diese für x64 und x86. Die GUI-Prüfungen testen zusätzlich sichtbare Checkboxzustände, Maus und Leertaste sowie Änderungsbenachrichtigungen bei ausgeschaltetem Auto-Update.

Die CLI-Prüfungen erfassen `@name`, `@123`, abschließendes `@` und einen relativen Pfad `-r2`. Die echte SVN-Gegenprobe hält eine Reservierung auf `collision`, während `collision@` aktualisiert wird: Nur `collision@` darf sich ändern. Eine Reservierung auf dem tatsächlichen Ziel muss den Start verhindern.

Die Updater-Gegenproben verwenden eigene lang laufende Testprozesse für Status, Schreiben, Dialoge und Unterprozesse. Sie prüfen das Ende vor der Installerübergabe, gesperrte neue Starts, einen verzögerten Prozessstart, Zeitüberschreitung und Diagnosefehler. Ein separat gestarteter Testprozess muss weiterlaufen. Ein echter WinForms-Installationsübergang mit laufender Hintergrundprüfung prüft die Wiederfreigabe nach einem absichtlich ausgelösten Installerfehler und die erneute Installation.

Mit `-SvnDirectory` prüfen die lokalen Windows-Tests echte TortoiseSVN-Protokoll-, Update-, Commit- und Cleanupdialoge auf isolierten Test-Arbeitskopien. Die Reservierung wird jeweils erst nach Prozessende freigegeben. Der Cleanup-Auswahldialog wird sichtbar erfasst und abgebrochen. Das manuelle Update übernimmt bei ausgeschaltetem Auto-Update eine eingehende Änderung in einem Pfad mit Leerzeichen. Benutzer-Repositories werden dafür nicht verwendet.
