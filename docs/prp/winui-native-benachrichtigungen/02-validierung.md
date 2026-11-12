# Validierung und Merge-Nachweise

Die hier beschriebenen Prüfungen sind Bestandteil der beauftragten Implementierung. Ergebnisse und offene Prüfungen stehen in [03-umsetzungsstand.md](03-umsetzungsstand.md). Die Szenariobeschreibung allein ist kein Testnachweis.

## Arbeitsweise und Nachweise

- Vor einem umfangreichen Lauf mit einer repräsentativen Gegenprobe prüfen, ob Build, Windows-Sitzung, native Anwendung und Testtreiber zusammenarbeiten.
- Nur eigene Konfigurationen, HTTP-Fixtures, lokale Test-Repositories und isolierte Arbeitskopien verwenden. Keine Benutzer-Repositories aktualisieren, bereinigen oder committen.
- Jeder Lauf erhält einen neuen Ausgabeordner, Startzeitpunkt und eine eindeutige Kennung. Alte Logzeilen und alte Screenshots sind keine aktuellen Erfolgsmeldungen.
- Ergebnis je Paritätszeile mit Exit-Code bzw. sichtbarem Verhalten und Artefaktpfad festhalten. Fehlende Ressourcen oder interaktive Shell ausdrücklich als Blocker ausweisen.
- Für gezielte Fehlererkennung jeweils einen gültigen und einen isoliert fehlerhaften Fall ausführen. Vor temporär defekten Builds oder Prozessen einen geprüften Wiederherstellungspfad sichern. Defekte Gegenprobe darf den regulären Arbeitsstand nicht beschädigen.
- Bestehende Testprojekte und Windows UI Automation weiterverwenden. Keine Browserprüfung als Ersatz für native Fenster, Tray oder Benachrichtigungszentrale.
- Nach erfolgreichen Prüfungen nur bei weiteren Änderungen, neuen Fehlern oder offenen betroffenen Risiken erneut ausführen bzw. erweitern.
- Benutzeranforderung: Keine Testfenster, Konsolen oder Fokuswechsel auf dem aktiven Benutzerdesktop. Windows-Aufrufe aus WSL über `.github/scripts/Invoke-QuietWindows.vbs` starten, Desktoptests zusätzlich über `.github/scripts/Invoke-IsolatedDesktop.ps1`. Dieser Weg erstellt einen eigenen, nicht eingeblendeten Windows-Desktop und prüft vor dem Teststart, dass er vom Eingabedesktop abweicht. Nicht auf den Benutzerdesktop ausweichen, wenn eine Prüfung dort nicht ausführbar ist.
- Fensteraufnahmen direkt am getesteten Fenster erstellen; `CopyFromScreen` würde auf einem anderen Desktop keine verlässliche Aufnahme liefern und darf keine Benutzeroberflächen erfassen. Für Shell-Banner und Benachrichtigungszentrale eine getrennte Windows-Sitzung oder VM verwenden, wenn die isolierte Desktopprüfung keinen tatsächlichen Shellnachweis liefern kann.

## T-01: Baseline und technische Vorprüfung

1. Branch, Ausgangscommit und bestehende Benutzeränderungen festhalten. Der Hauptbranch bleibt unverändert; keine fremden Änderungen in einen Testcheckpoint aufnehmen.
2. Vorhandene Anwendung aus einem reproduzierbaren Baseline-Paket mit eigenen Testdaten starten. Alle Befehle, Einstellungen und Startmodi aus der Matrix prüfen und fehlende Matrixzeilen ergänzen.
3. Vorhandene Build-, GUI-, SVN- und Updaterprüfungen für x64 und x86 ausführen. Bereits bestehende Fehler getrennt dokumentieren und deren Auswirkung auf die Migration klären.
4. Benachrichtigungsdauer mit unterschiedlichen Werten einschließlich eines gespeicherten Nullwerts vor/nach INI-Migration prüfen. Banneranzeige und Historie getrennt messen. Die Erwartung aus der UI nicht als beobachtetes Windows-Verhalten ausgeben.
5. Original und neuer SDK-Weg normal sowie erhöht prüfen. Keine Erhöhung allein zum Umgehen eines Registrierungsfehlers verwenden.
6. Konkrete stabile SDK-Version, .NET-SDK, Windows-Versionen und Architektur pinnen/dokumentieren. Quell-/Paketprüfung der Properties mit einem echten Paketstart verbinden.
7. Frühen Nachweis aus dem produktiven Einzel-EXE-Paket führen: Hauptfenster, Einstellungen, Tray, Meldung, Klick nach Prozessende und tatsächlicher Updateaustausch. Erst danach breiten UI-Umbau übertragen.

## T-02: Repository-Liste und Eingaben

- Alle Haupt-, Kontext- und Symbolleistenbefehle gegen das Inventar prüfen. Unterschiedliche Platzierung darf keine Aktion unzugänglich machen.
- Ordner und Datei hinzufügen, entfernen, neu laden; Pfadtyp, Sortierung, gemeinsame Pfadkürzung, Auswahl, vollständige Tooltips und lange Unicode-Pfade prüfen.
- Alle Statuskombinationen einschließlich keiner Auswahl und deaktivierter Prüfung prüfen. Aktivierungsbedingungen mit dem Ausgangscode vergleichen.
- Echte Maus-/Tastaturaktionen für Prüfung und Auto-Update, Leertaste, Entf, Einfg, Eingabe, Escape und fünf Doppelklickoptionen ausführen.
- Mehrere gemischte Dateisystemziele per Drag-and-drop hinzufügen; ungeeignete Inhalte ablehnen. Kein direkter Methodenaufruf als alleiniger Nachweis eines Drag-and-drop-Flows.
- Schmale Fensterbreite: jede Aktion über sichtbaren Button, Menü oder Überlauf erreichen. Fokus darf nicht in unsichtbaren Controls hängen bleiben.

## T-03: Einstellungen und INI

- Alle in `Config.Init`/`SaveSettings` gelesenen/geschriebenen Benutzereinstellungen inventarisieren und roundtrippen; nicht nur die im GUI-Test bereits berührten Felder prüfen.
- Jede Einstellung ändern, OK ausführen, laufenden Zustand und Datei prüfen, neu starten und erneut anzeigen. Einen zweiten Durchlauf mit Abbrechen und unveränderten Ausgangswerten ausführen.
- Programmdateipfade und Dateiauswahl einschließlich Fehler, Abbruch und fehlender Programme prüfen.
- Alle vorhandenen INI-Migrationszweige mit isolierten Originalformat-Fixtures prüfen; neue Repository-Einträge und alte Einträge ohne Auto-Update bleiben ausgeschaltet, gespeicherte explizite Werte bleiben erhalten.
- Import mit leerer/gefüllter Liste, Rückfrage bestätigen/abbrechen, ungültige Datei und schreibgeschütztes Ziel prüfen. Exportinhalt und erneuter Import müssen dieselben fachlichen Einstellungen/Einträge ergeben.
- Portable INI im Arbeitsverzeichnis und AppData-Konfiguration getrennt testen. Neustart, Toastaktivierung und Update dürfen die Konfiguration nicht wechseln.
- Schriftfamilie, 8,5/12/24 Punkt, Sprache und gegebenenfalls Neustarthinweise erhalten. Punktgröße darf nicht ungeprüft als WinUI-DIP-Wert behandelt werden.
- S-06/K-01 gemäß Benutzerentscheidung prüfen: Zugang zu Windows-Benachrichtigungseinstellungen und Erhalt bestehender INI-Werte; keine wirkungslose Sekunden-Einstellung anbieten.

## T-04: Repository-Eigenschaften

- Aktivierung, Standardintervalle/eigene Intervalle, Vererbung, abhängige Eingaben, OK und Abbrechen prüfen.
- Betroffener Eintrag und seine Objektidentität müssen nach dem Speichern erhalten bleiben; Liste, Hintergrundprüfung und INI müssen dieselben Werte verwenden.
- Veränderungen bei offenem Eigenschaftenfenster und ein später entfernter Eintrag dürfen keine fremden Einträge ändern.

## T-05: Protokolle und Info

- Updateausgaben für hinzugefügt, gelöscht, aktualisiert, Konflikt, zusammengeführt, Revision/Abschluss und Fehler verwenden; Zeilen, Zähler, Hinweis und Ziel des Protokollbuttons prüfen.
- Bestehenden Klangpfad und das Verhalten bei vorhandener bzw. fehlender TortoiseSVN-Klangkonfiguration vergleichen. Keine neue Klangquelle als Ersatz erfinden.
- Fehler anzeigen, leeren, Dialog schließen und erneut öffnen; auch später neu eintreffende Fehler prüfen. Fehlerpuffer und Warnungszugang müssen zusammenpassen.
- Infofenster und Release-Links auf richtige Ziele, Produkt-/Dateiversion und Fehlerbehandlung prüfen.
- Sehr lange Pfade und mehrzeilige Fehler in allen Themes und großer Schrift darstellen; relevante Informationen dürfen nicht abgeschnitten bleiben.

## T-06: Tray, Fenster und Lebenszyklus

- Tray-Symbol, Statuswechsel, Tooltip, Kontextmenü und Klick prüfen, auch nach Neustart von Windows Explorer in einer isolierten Testsitzung.
- Schließen und Escape verbergen; Tray-Klick stellt wieder her. Start-ausgeblendet und Taskleistenanzeige in allen Kombinationen prüfen. Keine verwaisten Tray-Symbole nach Beenden.
- Fenstergröße speichern und nach Neustart, DPI-/Monitorwechsel innerhalb des sichtbaren Arbeitsbereichs wiederherstellen.
- Aktives/inaktives Fenster, Pause nach Start, Suspend/Resume und Windows-Sitzungsende einschließlich abgebrochener Abmeldung prüfen. Simulationen durch einen realen Windows-Nachweis ergänzen.
- Zwei unabhängige Notifier-Instanzen starten. Kein globales Verbot mehrerer Instanzen. Fenster und Konfigurationen bleiben richtig zugeordnet.
- `start`, `--apply-update` und `--update-cleanup` aus echten EXE-Prozessen prüfen. Hilfsmodi dürfen weder Repository-Prüfungen noch zusätzliche Hauptfenster oder Toastregistrierungen starten.

## T-07: Fachliche SVN-Flows

- Prüfen, eingehende Änderung erkennen, Benachrichtigung erhalten, Änderungsprotokoll ansehen und manuell aktualisieren bis zum nutzbaren synchronisierten Ergebnis durchspielen.
- Beide Auto-Update-Zustände prüfen. Manuelles Update bei Auto-Update aus bleibt möglich; automatisches Update ohne aktivierte Checkbox bleibt aus.
- Änderungen-lokal, eingehend plus lokal, Konflikt, Fehler, keine Auswahl, mehrere Einträge und belegte Arbeitskopie durchspielen.
- Commit-, vollständigen Protokoll-, Änderungsprotokoll-, Update- und Cleanupdialog mit echtem TortoiseSVN auf isolierten Arbeitskopien prüfen. Cleanup nur öffnen und abbrechen; keine Bereinigung bestätigen.
- Änderungsprotokoll-vor-Update einschließlich geprüfter Revision und danach erhöhter HEAD-Revision testen.
- Stilles und angezeigtes Update-all, Fensterabschlussoption und Fehleranzeige prüfen. Kein übersprungener Auftrag darf als erfolgreich gemeldet werden.

## T-08: SVN-Koordination und Prozessschutz

Die Szenarien aus `docs/SVN-Koordination.md` und `tests/Svn.Tests/Program.cs` bleiben verbindlich:

- Vorabreservierung, Unterordner, mehrere Instanzen, externe Belegung, eingebettete Wurzeln und physische Pfadidentität.
- Verzögerter Start, Startfehler, Prozessfehler, volle Standard-/Fehlerstreams, Timeout, Abbruch und Diagnosefehler.
- Normales Beenden mit Leseprüfung sowie Schreib-/Dialogprozess; Installation mit Ende aller eigenen Prozesse und Unterprozesse. Extern gestarteter Testprozess muss weiterlaufen.
- Recovery mit lebendem/totem Besitzer und Kindprozess, wiederverwendeten PIDs, alten/unvollständigen Metadaten, fremder Rechnerkennung, konkurrierenden Übernahmen und Löschfehlern.
- Freigabe erst nach tatsächlichem Prozessende, auch nach Fehler im UI-Nachrichtenloop und bei Dateiaustauschversuch.
- Pfadfälle `@name`, `@123`, abschließendes `@`, relatives `-r2`, Leerzeichen und Umlaute. Kollisionsgegenprobe: reserviertes `collision` bleibt unverändert, während `collision@` aktualisiert wird.
- Reale neue Start-/Beenden-/Installationsübergänge der WinUI-Anwendung ergänzen. Die bisherigen `MainForm`-Integrationsfälle nicht nur aus dem Testprojekt streichen.
- Aktuelle Diagnoseereignisse auf Inhalt und Abwesenheit von Zugangsdaten/Befehlsargumenten prüfen.

## T-09: Programmupdates und portable Pakete

- Automatische Prüfung an/aus, Start-/Zwölfstundenintervall und manuelle Prüfung getrennt prüfen; Timer steuerbar testen, nicht zwölf Stunden untätig warten.
- Bestehende Release-/HTTP-Fixtures für aktuelle/neue/ältere/Vorabversion, Netzwerkfehler, Abbruch, SHA-256-, ZIP-, PE- und Versionsfehler erhalten.
- Echte Paketinstallation in neuen isolierten Verzeichnissen ausführen: bestätigter Download, Stilllegung, Hilfsprozess, Ende des Originals, erneute Prüfung, atomarer Austausch, Neustart und Stagingbereinigung.
- Die tatsächliche letzte WinForms-Ausgabe bzw. reproduzierbare Baseline-EXE als Ausgang verwenden. Ein synthetischer Installer-Testtreiber allein belegt keinen Alt-zu-Neu-Übergang.
- Konfigurationsdatei und Repository-Liste vor/nachher vergleichen; Arbeitsverzeichnis ungleich Programmverzeichnis mitprüfen. Die alte Paketprüfung muss das neue Paket weiterhin akzeptieren.
- Manipulation zwischen Download- und Installationsprüfung sowie Installerfehler und erneuten Installationsversuch prüfen.
- Auf einer Windows-Umgebung ohne vorinstallierte .NET-/App-SDK-Laufzeit beide Architekturpakete starten. Keine nachinstallierte Runtime als Erfolg des Self-contained-Vertrags ausgeben.
- EXE und native Aktivierung auch nach Verschieben eines portablen Ordners prüfen. Registrierung und spätere Bereinigung müssen zu diesem Pfad passen.

## T-10: Native Benachrichtigungen

- Alle drei Meldungsarten auf realem Windows versenden; lokalisierter Inhalt, Appname, Symbol, Klickziel und tatsächliche Banner-/Historienanzeige prüfen.
- Eingehende Änderungen für Eintrag A und B erzeugen, danach die ältere Meldung anklicken. Kein Rückgriff auf das zuletzt globale Ziel.
- Klick bei laufendem, ausgeblendetem und beendetem Prozess sowie vor fertiger Fenstererstellung ausführen. Gelöschte Einträge, unbekannte Payloads und mehrere Konfigurationen prüfen.
- Ein Benutzerklick darf keine Commit-, Cleanup- oder sonstige Schreibaktion ohne den bestehenden vorgesehenen Bedienablauf auslösen.
- Nach erfolgreichem Update ausstehende Änderungsmeldung aktualisieren/entfernen. Erneute neue Änderungen müssen wieder eine passende Meldung erzeugen.
- Mehrere Instanzen und Programmupdate mit noch vorhandenen Meldungen prüfen; keine Aktivierung einer Installerkopie und kein doppelter Pollingprozess.
- Deaktivierte Benachrichtigungen, Nicht stören und Registrierungsfehler prüfen. Gemäß Benutzerentscheidung K-02 normalen Betrieb ohne Administratorrechte prüfen; beim erhöhten Start eine konkrete Ablehnung verlangen. API-Annahme, tatsächliche Anzeige und Benutzerunterdrückung getrennt berichten.
- Benachrichtigungsdauer gemäß K-01 systemgesteuert prüfen; Banneranzeige und Historie getrennt betrachten.
- Registrierung unter dem Benutzerkonto, normale Ressourcenfreigabe beim Beenden und dauerhafte Aufräumfunktion beim Entfernen/Verschieben einer Installation planen und prüfen. Nicht bei jedem normalen Beenden sämtliche aufbewahrten Meldungen löschen.

## T-11: Gegenproben und Fehlerfälle

Mindestens die bestehenden GUI-Gegenproben für entfernten Befehl, nicht gespeicherte Option, übergroße Buttons, fehlendes Cleanup und abgetrennten Cleanup-Klick behalten. Zusätzlich isoliert nachweisen:

| Fehler | Erforderlicher Befund |
|---|---|
| Zwei Meldungen verwenden absichtlich dasselbe veränderliche Klickziel. | Aktivierungstest erkennt die falsche Zuordnung. |
| Eine Option wird nur angezeigt oder gespeichert, aber nicht angewendet. | Vergleich von Laufzeitverhalten und Neustart erkennt die fehlende Wirkung. |
| Ein neuer Command startet SVN ohne vorhandene Reservierungsstrecke. | Konflikt-/Startbeobachtung erkennt den zusätzlichen Start. |
| Hilfsprozess startet versehentlich normale UI oder Polling. | Modus-/Prozessprüfung erkennt den Zusatzprozess oder zusätzlichen Auftrag. |
| Ein portables Paket benötigt eine lose, nicht ausgelieferte SDK-Datei. | Test in sauberer Windows-Umgebung schlägt fehl. |
| Eine Benachrichtigungsaktivierung lädt die falsche INI. | Zwei Konfigurationen mit unterscheidbaren Einträgen decken den Wechsel auf. |

Gegenproben als Fixtures, injizierte Abhängigkeiten oder gesicherte temporäre Testbuilds ausführen. Keine gezielt defekte Implementierung im Produktstand belassen.

## T-12: Gestaltung, Zugänglichkeit und Sichtprüfung

- Vor Übertragung auf weitere Ansichten Hauptfenster und Einstellungen repräsentativ rendern und korrigieren.
- Vorhandene Matrix Deutsch/Englisch, 8,5/12/24 Punkt sowie x64/x86 erhalten. Alle sieben Ansichten zusätzlich in Hell-, Dunkel- und Kontrastdarstellung ansehen.
- Größen: Desktop 1920 × 1080, begrenzter Desktop 1366 × 768 und schmales Hauptfenster entsprechend der bisherigen Mindestbreite von 760 logischen Pixeln. Dialoge müssen im jeweiligen Arbeitsbereich bedienbar bleiben; bei großer Schrift scrollen statt Inhalte abschneiden.
- DPI 100 %, 150 % und 200 % gezielt mit Hauptfenster, Einstellungen, nativen Dialogen und Monitorwechsel prüfen. Zusätzliche Kombinationen bei tatsächlich erkannten Problemen oder betroffenen Ansichten ergänzen; keine pauschale Behauptung über ungeprüfte Kombinationen.
- Zustände gemäß UI-Zielbild aufnehmen: leer, Auswahl, alle Status, lange Pfade/Fehler, Prüfung/Update läuft, belegtes Ziel, Download, Abbruch und Fehler. Für jede wesentliche Ansicht einen aktuellen visuellen Nachweis vorhalten.
- Screenshot tatsächlich ansehen: Hierarchie, Dichte, Abstände, Ausrichtung, Typografie, Kontrast, Auswahl/Fokus, abgeschnittene Inhalte und Überläufe. Unveränderte Screenshots nicht wiederholt laden.
- Tastaturreihenfolge, zugängliche Namen, Checkboxzustände, Dialogfokus, Tooltips und Rückkehr zum Besitzerfenster prüfen. Notwendige Informationen dürfen nicht ausschließlich per Maus erreichbar sein.
- Keine Eyebrows, Overlines, Subtitles oder redundanten Erklärungstexte in den bearbeiteten Ansichten. Das Entfernen solcher Texte darf keine notwendige Fehlermeldung/Identifikation entfernen.
- Nach gestalterischer Korrektur betroffene Ansichten neu rendern, aufnehmen und ansehen. Keine visuelle Freigabe allein aus AutomationIds, Größenassertions oder erfolgreichem Build ableiten.

## T-13: Build, CI und Merge

Die folgenden Befehle verwenden die bereits vorhandenen Windows-Skripte. Sie sind im späteren Umbau weiter ausführbar zu halten. Aus einer Windows-PowerShell im Repository starten; einen neuen Ausgabeordner verwenden:

```powershell
$validationRoot = Join-Path 'artifacts' ('winui-validation-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet build SVN_Notifier.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Solution-Build fehlgeschlagen.' }
foreach ($runtime in @('win-x64', 'win-x86')) {
    pwsh -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $validationRoot
    if ($LASTEXITCODE -ne 0) { throw "Paketbuild fehlgeschlagen: $runtime" }
    powershell -File .github/scripts/Test-Gui.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $validationRoot
    if ($LASTEXITCODE -ne 0) { throw "GUI-Prüfung fehlgeschlagen: $runtime" }
    powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $validationRoot
    if ($LASTEXITCODE -ne 0) { throw "SVN-Prüfung fehlgeschlagen: $runtime" }
    powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $validationRoot
    if ($LASTEXITCODE -ne 0) { throw "Updater-Prüfung fehlgeschlagen: $runtime" }
}
```

Echte SVN-/TortoiseSVN-Prüfungen zusätzlich mit einem separat neuen Ausgabeordner und dem vorhandenen Parameter `-SvnDirectory` ausführen, zum Beispiel dem lokal tatsächlich installierten TortoiseSVN-Verzeichnis. Diesen Pfad vor Ausführung prüfen; keinen unbestätigten Programmpfad voraussetzen.

- Die GUI-/SVN-Testtreiber im Zuge des Umbaus auf native WinUI-Prozesse/Windows UI Automation umstellen; heutige WinForms-Reflection ist keine Vorgabe für den neuen Treiber.
- Neue native Benachrichtigungsprüfungen an den passenden vorhandenen Test-/CI-Einstieg anbinden. Ihr tatsächliches Kommando und Voraussetzungen bei Implementierung dokumentieren, keine heute nicht existierenden Skripte als ausführbar ausgeben.
- GitHub-Runner ohne nutzbare interaktive Windows-Shell können Payload-/Prozessprüfungen ausführen, liefern aber keinen Beleg für sichtbare Toasts oder Benachrichtigungszentrale. Den erforderlichen echten Desktopnachweis separat erbringen; diese Lücke blockiert den Merge, bis der Nachweis vorliegt.
- Featurebranch-Pushes oder Pull Requests müssen beide Architekturjobs auslösen. Stabile Releaseveröffentlichung bleibt gesondert und darf keine Featurebranch-Ausgabe über den produktiven Updater verteilen.
- Vor Merge Veränderungen auf `master` bzw. dem dann gültigen Hauptbranch prüfen, Konflikte kontrolliert integrieren und direkt betroffene Prüfungen wiederholen.
- Alle Matrixzeilen erhalten Ergebnis-/Artefaktverweise. Fehlende Nachweise, bekannte Mängel, offene K-Punkte und nicht entschiedene Funktionsabweichungen blockieren den Merge.
- Der Benutzer entscheidet ausdrücklich über Merge und Veröffentlichung. Ein grüner Branch-Build allein ist kein Mergeauftrag.
