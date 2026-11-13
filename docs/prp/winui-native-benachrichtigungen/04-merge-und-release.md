# Merge und Release

## Auftrag und Voraussetzungen

Die Korrektur liegt auf `feature/winui-native-notifications`. Der veröffentlichte Ausgangsstand ist 3.1.0.0, die beauftragte Release-Version lautet 3.1.0.1. Der Benutzer hat Korrektur, Prüfung und Veröffentlichung ausdrücklich beauftragt. Hauptbranch ist `master`. Die getrennte Release-Arbeitskopie enthält nur versionierte Projektbestände. Der geprüfte Windows-Git-Zugriff steht im [PRP](PRP.md#geprüfter-git-push-aus-wsl), ohne Zugangsdaten.

Maßgeblich sind [Funktionsparität](01-funktionsparitaet.md), [Validierung](02-validierung.md), [Nachweise](03-umsetzungsstand.md) und [Fehlerdiagnose](../../Fehlerdiagnose.md). Nach erfolgreichen x64- und x86-Prüfungen die Korrektur auf `master` integrieren und Release 3.1.0.1 auslösen. Die tatsächlichen Ergebnisse werden im Umsetzungsstand erfasst. Bestehende offene Nachweise für Banner, Tray, Meldungsklick, Drag-and-drop, Datei-/Ordnerauswahl, DPI, Kontrast und einen Rechner ohne separat installierte Laufzeiten bleiben ausdrücklich benannt; die Veröffentlichung ersetzt diese Prüfungen nicht.

## Merge

1. Aktuellen Hauptbranch ermitteln. Der vorhandene Workflow verwendet `master`; bei einer Umbenennung auch Workflow, Dokumentation und PR-Ziel abgleichen. Änderungen des Hauptbranches in den Featurebranch integrieren, Konflikte fachlich lösen und betroffene Prüfungen wiederholen. Benutzeränderungen vorher gezielt erhalten, kein pauschales Zurücksetzen oder Stagen.
2. Nach ausdrücklichem Pushauftrag den dokumentierten Windows-Git-Weg verwenden und den Remote-Commit vergleichen. Pull Request gegen den tatsächlichen Hauptbranch erstellen. Problem, Ergebnis, erhaltene Funktionen, Windows-Voraussetzungen und konkrete Prüfnachweise beschreiben. Keine offenen Nachweise stillschweigend als bestanden übernehmen.
3. Nach Review und ausdrücklichem Mergeauftrag zusammenführen. Den konkreten Hauptbranch-Commit und seine erfolgreichen Windows-Prüfungen dokumentieren. Das integrierte Ergebnis prüfen, nicht nur einen davor gebauten Featurebranch.

## Build und Prüfungen

Unter Windows aus dem Repository-Hauptverzeichnis, mit einem frischen Artefaktordner für SVN- und Installationstests:

```powershell
dotnet build SVN_Notifier.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Solution-Build fehlgeschlagen.' }
$releaseArtifacts = 'artifacts/release-validation-' + [Guid]::NewGuid().ToString('N')
foreach ($runtime in @('win-x64', 'win-x86')) {
    powershell -File .github/scripts/Build-Windows.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $releaseArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'Paketbuild fehlgeschlagen.' }
    powershell -File .github/scripts/Test-StartModes.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $releaseArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'Startmodi fehlgeschlagen.' }
    powershell -File .github/scripts/Test-Gui.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $releaseArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'GUI-Prüfung fehlgeschlagen.' }
    powershell -File .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $releaseArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'SVN-Prüfung fehlgeschlagen.' }
    powershell -File .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier $runtime -ArtifactsDirectory $releaseArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'Updater-Prüfung fehlgeschlagen.' }
}
```

Diese Zusammenstellung ist eine noch nicht ausgeführte Release-Ablaufvorlage. Die einzelnen Skripte und ihre tatsächlichen bisherigen Ergebnisse sind im Umsetzungsstand belegt. Bei unbeaufsichtigter lokaler Ausführung den dokumentierten unsichtbaren Starter verwenden; eine direkte GUI-Ausführung öffnet Fenster. Shellprüfungen benötigen zusätzlich eine ausdrücklich erlaubte, entsperrte Testsitzung. Parameter und Zeitbegrenzung von `Test-NativeShell.ps1` stehen in der Validierungsdokumentation. Nach jedem Lauf ausschließlich eigene Testinstanzen beenden.

Der Workflow `.github/workflows/windows-build.yml` baut x64/x86 und führt Paketstart, Updater-, Startmodus-, GUI- und SVN-Prüfungen aus. Zusätzlich lädt er das veröffentlichte WinForms-Ausgangspaket 3.0.0.13, prüft dessen SHA-256 und testet den tatsächlichen EXE-Austausch samt WinUI-Neustart und Konfiguration. Manuelle Shell-, Sicht- und Systemwechselprüfungen deckt er nicht ab.

Die Produktprüfungen laufen über `Invoke-CiStandardUser.ps1` und `Test-WindowsProduct.ps1` in einem temporären Standardkonto mit geladenem Profil auf eigenem, nicht aktiviertem Desktop innerhalb der interaktiven Fensterstation. Kontoanlage ist nur auf gehosteten GitHub-Runnern erlaubt. Das zufällige Kennwort bleibt im Speicher; gestartete Prozesse werden begrenzt, Arbeitsverzeichnisrechte und vorübergehende Fensterstationsrechte zurückgesetzt und das Konto entfernt. Für eigene benannte SDK- und CLI-Objekte erhält ausschließlich dieses Konto vorübergehend Erstell- und Auflösungsrechte im Objektverzeichnis der Runnersitzung, ohne Vererbung oder Rechteänderungsbefugnis. Die Ausgangs-DACL wird nach Ende der eigenen Testprozesse wiederhergestellt. Die lokalen Gegenproben und der noch offene gehostete Nachweis stehen im Umsetzungsstand. Der Standarddesktop erhält keine zusätzliche Freigabe. Der Paketbuild bleibt davon getrennt. Das Prüfprotokoll wird auch bei Fehlern als eigenes Actions-Artefakt hochgeladen; nur Paketartefakte mit Namen `windows-*` gelangen in den Releasejob. Überholte Branchläufe werden abgebrochen, laufende Tag-Veröffentlichungen erhalten.

## Release

1. Die beauftragte Version ist 3.1.0.1. `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` in `SVN_Notifier/SVN_Notifier.csproj` müssen identisch sein. Vor Veröffentlichung prüfen, dass der Tag frei und die Version höher als die letzte stabile Veröffentlichung ist.
2. README und `.github/RELEASE_NOTES.md` auf die gewählte Veröffentlichung ausrichten. Den Entwicklungsstatus erst nach den zugehörigen Nachweisen ersetzen. Windows 10 1809+, normalen Start ohne Administratorrechte, native Meldungen, den standardmäßig eingeschalteten Benachrichtigungsschalter und Windows-gesteuerte Banneranzeigedauer nennen. Portablen Ordnerwechsel und ungültige alte Meldungen erläutern.
3. Genau den vorgesehenen Hauptbranch-Commit bauen und prüfen. Pakete, Architektur, EXE-Version und SHA-256 kontrollieren. ZIP-Dateien enthalten weiterhin EXE, README und Lizenz; keine Benutzer-INI-Dateien einpacken.
4. Der Veröffentlichungsauftrag liegt vor. Nach den erforderlichen Prüfungen den Tag auf den nachgewiesenen Hauptbranch-Commit setzen und pushen, in einer geprüften Windows-Git-Arbeitskopie mit ausgechecktem Hauptbranch:

```powershell
git tag v3.1.0.1
git push mggm refs/tags/v3.1.0.1
```

Vor diesen Befehlen prüfen, dass `HEAD` genau der freigegebene Hauptbranch-Commit ist und der Tag nicht existiert. Vorhandene Tags nicht verschieben. Tag und zugehörigen Workflow nach dem Push auf GitHub kontrollieren.

Der aktuelle Workflow reagiert auf `v*`-Tags und prüft zusätzlich, dass der Tag-Commit in `master` enthalten ist. Nach beiden erfolgreichen Windows-Jobs prüft er die Paketprüfsummen, erstellt einen Release-Entwurf und veröffentlicht ihn automatisch als neuestes Release. Ein Tag-Push löst daher bereits die Veröffentlichung aus. Ein Branch-Push veröffentlicht kein Release. Das automatische `GITHUB_TOKEN` genügt; keine Zugangsdaten in Dateien oder Befehle übernehmen.

## Letzter Ausweg: lokaler Build und manuelle Veröffentlichung

Benutzerentscheidung: Falls GitHub Actions trotz Ursachenprüfung und Korrekturen nicht nutzbar ist, kann die Veröffentlichung mit lokal gebauten und geprüften Paketen erfolgen. GitHub Actions bleibt der vorgesehene Weg. Der lokale Veröffentlichungsweg ist vorgemerkt und noch nicht ausgeführt.

1. Den vorgesehenen integrierten `master`-Commit mit allen vier Versionsfeldern auf 3.1.0.1 unter Windows für x64 und x86 bauen. Die vorhandenen Build- und Prüfscripte verwenden und neue Artefaktordner anlegen.
2. Die erforderlichen Paketstart-, Startmodus-, GUI-, SVN-, Updater- und WinForms-Übergangsprüfungen lokal ausführen. Offene Shell-, Sicht- und Systemprüfungen bleiben erforderlich. Einen Fehler der Anwendung oder einen fehlenden Funktionsnachweis nicht durch den Wechsel des Buildorts umgehen. Unsichtbare Tests und das Beenden eigener Testinstanzen beibehalten; sichtbare Desktopläufe benötigen eine konkrete Freigabe.
3. Die geprüften ZIP-Pakete für beide Architekturen samt README, Lizenz und gemeinsamer `SHA256SUMS.txt` bereitstellen. EXE-Version, Architektur, Paketinhalt und SHA-256 prüfen. Die bisherigen Paketnamen und das vom Programmupdater erwartete Prüfsummenformat erhalten; keine Benutzerkonfiguration oder Zugangsdaten veröffentlichen.
4. Vor dem Tag-Push die konkurrierende automatische Actions-Veröffentlichung gezielt ausschließen und die gewählte Workflow-Anpassung dokumentieren. Der bestehende `v*`-Auslöser darf nicht parallel dieselbe Release-Version veröffentlichen.
5. Den freien Tag `v3.1.0.1` auf genau den geprüften Hauptbranch-Commit setzen und pushen. Über die GitHub-Oberfläche oder einen dafür autorisierten Zugriff einen Release-Entwurf erstellen, beide ZIP-Dateien und `SHA256SUMS.txt` hochladen und nach Paketprüfung als stabiles neuestes Release veröffentlichen. Keine vorhandenen Tags verschieben oder Release-Dateien ungeprüft ersetzen.
6. Release-Version, Tag-Zuordnung, Download-Dateien und Prüfsummen nach Veröffentlichung prüfen und die tatsächlichen Nachweise im Umsetzungsstand erfassen. Der vorhandene Programmupdater verwendet weiterhin das stabile GitHub-Release mit unveränderten Paketnamen und Prüfsummenregeln.

## Update von WinForms und Rückkehr

Der vorhandene Updater ab 3.0.0.4 kann eine neuere stabile WinUI-Ausgabe anhand derselben Paketnamen und Architektur installieren. Der tatsächliche Übergang 3.0.0.13 zu 3.1.0.0 wurde lokal für x64 und x86 mit EXE-Austausch, Neustart, INI und Repository-Zuordnung geprüft (`release-3100.log` und `release-3100-continue.log`). Erst ein veröffentlichtes stabiles Release wird regulären Benutzern angeboten; der Featurebranch ist keine Updatequelle.

Eine lokale Entwicklungsinstanz mit derselben Versionsnummer erhält diese Veröffentlichung nicht erneut als Update (`ApplicationUpdater.CheckAsync` verwirft gleiche oder kleinere Versionen). Eine solche Vergleichsinstanz gegebenenfalls manuell gegen das veröffentlichte Paket austauschen; die reguläre WinForms-Ausgabe 3.0.0.13 hat eine kleinere Versionsnummer.

Ab 3.0.0.10 beendet der Updater vor Installation seine eigenen SVN-Prozesse und wartet auf deren Ende. Bei älteren Ausgangsversionen deren laufende Notifier-SVN-Aufträge vorher beenden. Versionen bis 3.0.0.3 benötigen einen manuellen Einstieg in eine Ausgabe mit Updater. Schreibzugriff auf den Programmordner, passende Windows-Version und normale Benutzerrechte bleiben Voraussetzungen.

Vor manuellem Vergleich oder Rückkehr Paket und INI getrennt sichern. Der Updater installiert keine Downgrades. Eine Rückkehr auf WinForms ist daher ein manueller Vorgang mit eigenem Auftrag; die Rückwärtskompatibilität der konkreten INI zunächst mit einer Kopie prüfen. Bereits veröffentlichte Releases und Tags nicht ungefragt entfernen oder umhängen.
