SVN Notifier 3.0.0.4

- Automatische Versionsprüfung für die Anwendung beim Start und alle zwölf Stunden; manuell unter Hilfe > Nach Updates suchen.
- Download und Installation einer bestätigten neuen stabilen Version mit anschließendem Neustart.
- Automatische Auswahl von x64 oder x86 sowie Prüfung von SHA-256-Prüfsumme, ZIP-Inhalt, EXE-Version und Architektur.
- Erhalt der Repository-Liste und INI-Einstellungen. Laufende SVN-Vorgänge werden vor der Installation berücksichtigt.
- Abschaltbare automatische Programmprüfung unter Einstellungen > Allgemein. Die manuelle Prüfung bleibt verfügbar.
- Regressionstests und Tests für EXE-Austausch, Neustart und manipulierte Downloads in GitHub Actions.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
