# Fehlerprotokoll und SVN-Zeitlimit

Das Fehlerprotokoll zeigt für jede betroffene Arbeitskopie den zuletzt erfassten Fehler mit Zeitstempel, Pfad und Ursache. Neue Fehler derselben Arbeitskopie ersetzen deren Eintrag. Erfolgreiche spätere Prüfungen löschen gespeicherte Fehler nicht automatisch. Die Schaltfläche **Leeren** entfernt den Fehlerpuffer; ein Neustart beginnt mit einem leeren Puffer.

## Fehlerarten

- **Zeitüberschreitung:** Die Status- oder Revisionsprüfung hat das beim Start verwendete Zeitlimit erreicht. Die Meldung nennt die Anzahl Sekunden. Die Prozessdiagnose verwendet `reason=timeout`.
- **Abbruch:** Eine Prüfung wurde ohne Ablauf ihres Zeitlimits abgebrochen. Die Diagnose verwendet `reason=cancelled`. Das bestätigte Programmupdate behält seinen gesonderten Grund `application-update`.
- **Prozessfehler:** SVN hat einen Fehlercode zurückgegeben. Die Meldung nennt den Exit-Code, die Diagnose verwendet `reason=process-error`.
- **Start- oder Ausführungsfehler ohne Exit-Code:** Die Meldung nennt den fehlgeschlagenen Start beziehungsweise die Ausführung ohne leeres Exit-Code-Feld.

Das konfigurierte `SvnCommandTimeout` gilt weiterhin für lesende Prüfungen, standardmäßig 60 Sekunden. Der Wert `0` deaktiviert das Zeitlimit. Schreibende SVN-Aufträge erhalten dadurch kein zusätzliches Zeitlimit. Bei einer Zeitüberschreitung wird der eigene Prozessbaum beendet; die Reservierung wird erst nach dem tatsächlichen Prozessende freigegeben.

Die Fehleranzeige übernimmt keine ungefilterte SVN-Ausgabe. Diagnosezeilen enthalten weiterhin keine Befehlsargumente oder Zugangsdaten. Ein Timeout belegt nicht die Ursache einer langsamen SVN-Antwort; dafür müssen die konkret betroffene Verbindung und Arbeitskopie gesondert geprüft werden.

## Prüfungen

`tests/Gui.Tests` prüft mehrere Fehler mit unterschiedlichen Ursachen, Zeitstempeln, Umlauten und Arbeitskopiepfaden auf Deutsch und Englisch sowie das Leeren des Puffers. Die Gegenprobe `error-log` schneidet den Text gezielt nach der ersten Zeile ab und muss durch die Inhaltsprüfung erkannt werden. Der Mehrzeilenmodus des WinUI-Textfelds wird vor dem Text gesetzt.

`tests/Svn.Tests` prüft echte Prozessabläufe für Zeitüberschreitung, expliziten Abbruch, Prozessfehler, erfolgreiche und unbegrenzte Prüfung sowie die anschließende Reservierungsfreigabe. Die Coordinator-Prüfung verfolgt Timeout, Prozess- und Startfehler bis zum tatsächlichen Fehlerpuffer in beiden Sprachen.

Der [Merge- und Releaseablauf](prp/winui-native-benachrichtigungen/04-merge-und-release.md) beschreibt Paketierung und Veröffentlichung. Tatsächlich ausgeführte Prüfungen stehen im [Umsetzungsstand](prp/winui-native-benachrichtigungen/03-umsetzungsstand.md).
