# Merge und Release

## Auftrag und Voraussetzungen

Die Umsetzung liegt auf `feature/winui-native-notifications`. Der veröffentlichte Ausgangsstand ist 3.0.0.13, die beauftragte Release-Version lautet 3.1.0.0. Der Benutzer hat Commit, Push, Merge auf den tatsächlichen Hauptbranch und Veröffentlichung ausdrücklich beauftragt. Der tatsächliche Hauptbranch ist `master`. Die folgenden Schritte beschreiben den Ablauf; ausgeführte Schritte und Prüfergebnisse werden im Umsetzungsstand erfasst. Die geprüfte Windows-Git-Zugriffsmethode steht im [PRP](PRP.md#geprüfter-git-push-aus-wsl), ohne Zugangsdaten.

Maßgeblich sind [Funktionsparität](01-funktionsparitaet.md), [Validierung](02-validierung.md) und [Nachweise](03-umsetzungsstand.md). Ein grüner Build ersetzt keine fehlenden Funktions- und Shellprüfungen. Vor dem Merge insbesondere Banner, Historie, Tray, Meldungsklick nach Prozessende, Drag-and-drop, Datei-/Ordnerauswahl, DPI, Kontrast und portablen Ordnerwechsel nachweisen. Den verweigerten Verzeichniswechsel im Test klären. Einzel-EXE und Start beider Architekturen auch auf einem geeigneten Windows-Rechner ohne separat installierte .NET-/Windows-App-SDK-Laufzeit prüfen; eine solche Umgebung fehlt in dieser Sitzung.

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

## Release

1. Die beauftragte Version ist 3.1.0.0. `Version`, `AssemblyVersion`, `FileVersion` und `InformationalVersion` in `SVN_Notifier/SVN_Notifier.csproj` müssen identisch sein. Vor Veröffentlichung prüfen, dass der Tag frei und die Version höher als die letzte stabile Veröffentlichung ist.
2. README und `.github/RELEASE_NOTES.md` auf die gewählte Veröffentlichung ausrichten. Den Entwicklungsstatus erst nach den zugehörigen Nachweisen ersetzen. Windows 10 1809+, normalen Start ohne Administratorrechte, native Meldungen, den standardmäßig eingeschalteten Benachrichtigungsschalter und Windows-gesteuerte Banneranzeigedauer nennen. Portablen Ordnerwechsel und ungültige alte Meldungen erläutern.
3. Genau den vorgesehenen Hauptbranch-Commit bauen und prüfen. Pakete, Architektur, EXE-Version und SHA-256 kontrollieren. ZIP-Dateien enthalten weiterhin EXE, README und Lizenz; keine Benutzer-INI-Dateien einpacken.
4. Der Veröffentlichungsauftrag liegt vor. Nach den erforderlichen Prüfungen den Tag auf den nachgewiesenen Hauptbranch-Commit setzen und pushen, in einer geprüften Windows-Git-Arbeitskopie mit ausgechecktem Hauptbranch:

```powershell
git tag v3.1.0.0
git push mggm refs/tags/v3.1.0.0
```

Diese Befehle wurden hier nicht ausgeführt. Vorher prüfen, dass `HEAD` genau der freigegebene Commit ist und der Tag nicht existiert. Vorhandene Tags nicht verschieben.

Der aktuelle Workflow reagiert auf `v*`-Tags und prüft zusätzlich, dass der Tag-Commit in `master` enthalten ist. Nach beiden erfolgreichen Windows-Jobs prüft er die Paketprüfsummen, erstellt einen Release-Entwurf und veröffentlicht ihn automatisch als neuestes Release. Ein Tag-Push löst daher bereits die Veröffentlichung aus. Ein Branch-Push veröffentlicht kein Release. Das automatische `GITHUB_TOKEN` genügt; keine Zugangsdaten in Dateien oder Befehle übernehmen.

## Update von WinForms und Rückkehr

Der vorhandene Updater ab 3.0.0.4 kann eine neuere stabile WinUI-Ausgabe anhand derselben Paketnamen und Architektur installieren. Der tatsächliche Übergang 3.0.0.13 zu 3.0.0.14 wurde lokal für x64 und x86 mit EXE-Austausch, Neustart, INI und Repository-Zuordnung geprüft (`legacy-transition.log`). Erst ein veröffentlichtes stabiles Release wird regulären Benutzern angeboten; der Featurebranch ist keine Updatequelle.

Eine lokale Entwicklungsinstanz mit derselben Versionsnummer erhält diese Veröffentlichung nicht erneut als Update (`ApplicationUpdater.CheckAsync` verwirft gleiche oder kleinere Versionen). Eine solche Vergleichsinstanz gegebenenfalls manuell gegen das veröffentlichte Paket austauschen; die reguläre WinForms-Ausgabe 3.0.0.13 hat eine kleinere Versionsnummer.

Ab 3.0.0.10 beendet der Updater vor Installation seine eigenen SVN-Prozesse und wartet auf deren Ende. Bei älteren Ausgangsversionen deren laufende Notifier-SVN-Aufträge vorher beenden. Versionen bis 3.0.0.3 benötigen einen manuellen Einstieg in eine Ausgabe mit Updater. Schreibzugriff auf den Programmordner, passende Windows-Version und normale Benutzerrechte bleiben Voraussetzungen.

Vor manuellem Vergleich oder Rückkehr Paket und INI getrennt sichern. Der Updater installiert keine Downgrades. Eine Rückkehr auf WinForms ist daher ein manueller Vorgang mit eigenem Auftrag; die Rückwärtskompatibilität der konkreten INI zunächst mit einer Kopie prüfen. Bereits veröffentlichte Releases und Tags nicht ungefragt entfernen oder umhängen.
