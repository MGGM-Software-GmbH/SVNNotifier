SVN Notifier 3.0.0.6

- Automatische SVN-Updates benötigen eine aktivierte Checkbox. Neue Einträge und ältere INI-Einträge ohne Auto-Update-Feld beginnen ausgeschaltet. Bereits gespeicherte Werte bleiben erhalten.
- Status, Info, stille Updates und aus dem Notifier gestartete TortoiseSVN-Dialoge werden je Arbeitskopiewurzel koordiniert. Unterordner, normalisierte Windows-Pfade und eingebettete Arbeitskopien werden berücksichtigt.
- Reservierung vor dem Prozessstart und Freigabe erst nach Prozessende. Bei belegter Koordination kein weiterer Prozessstart; Hintergrundaktionen warten vor einem erneuten Versuch mindestens 15 Sekunden.
- Statusbenachrichtigungen bleiben bei ausgeschaltetem Auto-Update erhalten. Die Checkbox zeigt den aktiven und inaktiven Zustand und ist auch per Leertaste bedienbar.
- Gemeinsames Dateiprotokoll für ausdrücklich teilnehmende externe Aufrufe. Optionaler Python-Wrapper und [Einrichtungsvertrag](https://github.com/MGGM-Software-GmbH/SVNNotifier/blob/v3.0.0.6/docs/SVN-Koordination.md) im Repository. Direkte SVN-, Explorer- und IDE-Aufrufe ohne dieses Verfahren werden nicht erfasst.
- Diagnose mit Arbeitskopie, Aktion, Beginn/Ende, Prozess-ID, Exit-Code und Überspringgrund ohne Befehlsargumente oder Zugangsdaten. Keine automatischen Cleanups oder Änderungen an Arbeitskopiedatenbanken.
- Regressionstests für konkurrierende Starts, Belegung, Prozessfehler, Abbruch, Freigabe und Konfiguration. Windows-/Container-Gegenproben wurden lokal auf einer isolierten 9p-Testablage ausgeführt; die konkrete KLIMM-Container-Konfiguration bleibt gesondert zu prüfen.

Bereits gespeichert aktivierte Auto-Updates bleiben aktiv. Bei gemeinsam genutzten Arbeitskopien sollte diese Einstellung bewusst geprüft werden. Die Änderungen liefern keinen Ursachennachweis für den ursprünglich gemeldeten SQLite-I/O-Fehler. Nach einem Absturz oder erzwungenen Ende kann eine Reservierung zur sicheren Blockierung bestehen bleiben; sie wird nicht automatisch entfernt.

Programmupdates und automatische SVN-Updates sind getrennte Funktionen. Versionen bis einschließlich 3.0.0.3 müssen einmal manuell durch diese Ausgabe ersetzt werden, um den Programmupdater zu erhalten.

Das passende ZIP-Paket entpacken und `SVN_Notifier.exe` starten. Eine separate .NET-Installation ist nicht erforderlich. `svn.exe` und TortoiseSVN werden separat benötigt. Automatische Programmupdates benötigen Schreibzugriff auf den Programmordner. Die EXE-Dateien sind nicht digital signiert. `SHA256SUMS.txt` enthält die Prüfsummen der beiden ZIP-Pakete.
