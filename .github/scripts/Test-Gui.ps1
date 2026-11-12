param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [string] $ArtifactsDirectory = 'artifacts',
    [switch] $IncludeShellSettings
)

# Die Tests öffnen echte WinUI-Fenster und verwenden ausschließlich eine eigene INI-Datei.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsDirectory)) { $ArtifactsDirectory } else { Join-Path $repositoryPath $ArtifactsDirectory }
$projectPath = Join-Path $repositoryPath 'tests/Gui.Tests/Gui.Tests.csproj'
[xml] $productProject = Get-Content -LiteralPath (Join-Path $repositoryPath 'SVN_Notifier/SVN_Notifier.csproj') -Raw
$productVersion = [string] $productProject.Project.PropertyGroup[0].Version
$productPackage = Join-Path $artifactsPath "packages/SVNNotifier-$productVersion-$RuntimeIdentifier.zip"
if (-not (Test-Path -LiteralPath $productPackage -PathType Leaf)) { throw "Das gebaute Produktpaket für die GUI-Updateprüfung fehlt: $productPackage" }
$driverPath = Join-Path $artifactsPath "gui-driver/$RuntimeIdentifier"
Invoke-QuietConsole dotnet publish $projectPath --configuration Release --runtime $RuntimeIdentifier --self-contained true --output $driverPath `
    --artifacts-path (Join-Path $artifactsPath "gui-build/$RuntimeIdentifier") `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:Platform=$($RuntimeIdentifier.Substring(4)) --nologo
if ($LASTEXITCODE -ne 0) { throw 'Die GUI-Prüfung konnte nicht gebaut werden.' }
$executablePath = Join-Path $driverPath 'Gui.Tests.exe'
$resultsPath = Join-Path $artifactsPath ("gui-checks/$RuntimeIdentifier/" + [Guid]::NewGuid().ToString('N'))
Write-Host "GUI-Nachweise dieses Laufs: $resultsPath"
foreach ($font in @('default', 'large', 'maximum', 'dark')) {
    $testPath = Join-Path $resultsPath $font
    $arguments = @($testPath, '--capture', '--update-package', $productPackage)
    if ($IncludeShellSettings) { $arguments += '--shell-settings' }
    if ($font -eq 'large') { $arguments += '--large-font' }
    if ($font -eq 'maximum') { $arguments += '--maximum-font' }
    if ($font -eq 'dark') { $arguments += '--dark' }
    Invoke-QuietConsole $executablePath @arguments
    if ($LASTEXITCODE -ne 0) { throw "GUI-Prüfung fehlgeschlagen: $RuntimeIdentifier, $font" }
}
$counterexamples = @('missing-command', 'save-option', 'button-size', 'cleanup-command', 'cleanup-click', 'font-size', 'heading', 'command-alignment', 'roundtrip', 'session-end', 'properties-cancel', 'remove-binding', 'orphan-properties', 'doubleclick-gate', 'doubleclick-route')
if ($IncludeShellSettings) { $counterexamples += @('settings-cancel','settings-effect') }
foreach ($scenario in $counterexamples) {
    $testPath = Join-Path $resultsPath $scenario
    New-Item -ItemType Directory -Path $testPath -Force | Out-Null
    $outputPath = Join-Path $testPath 'output.log'
    $errorPath = Join-Path $testPath 'error.log'
    $scenarioArguments = @(('"' + $testPath + '"'), "--negative-$scenario", '--update-package', ('"' + $productPackage + '"'))
    if ($IncludeShellSettings) { $scenarioArguments += '--shell-settings' }
    $process = Start-Process -FilePath $executablePath -ArgumentList $scenarioArguments `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
    $expected = switch ($scenario) {
        'missing-command' { '*Befehle erhalten: FileMenuItem*' }
        'save-option' { '*Benachrichtigungsdauer werden gespeichert*' }
        'button-size' { '*Schriftbedarf*' }
        'cleanup-command' { '*Befehle erhalten: ItemMenu*' }
        'cleanup-click' { '*Cleanup-Menüklick startet den Dialog*' }
        'font-size' { '*Repository-Statustext verwendet die gespeicherte Schriftgr*' }
        'heading' { '*Listenüberschrift besitzt eine erkennbare typografische Hierarchie*' }
        'command-alignment' { '*Listenaktion beginnt am linken Rand*' }
        'roundtrip' { '*Einstellung bleibt nach erneutem INI-Laden erhalten: DefaultIdleStatusUpdateInterval*' }
        'session-end' { '*Bestätigte Sitzungsbeendigung schließt das Fenster statt es nur auszublenden*' }
        'properties-cancel' { '*Abbrechen im tatsächlichen Eigenschaftendialog erhält Objekt und gespeicherten Eintrag*' }
        'remove-binding' { '*Repository-Befehl öffnet den zugehörigen Dialog: Remove*' }
        'orphan-properties' { '*Speichern nach zwischenzeitlichem Entfernen verändert weder fremde Einträge*' }
        'settings-cancel' { '*Abbrechen im tatsächlichen Einstellungsdialog erhält Optionen und INI-Datei*' }
        'settings-effect' { '*Bestätigte Einstellungen werden gespeichert und auf das tatsächliche Taskleistenfenster angewendet*' }
        'doubleclick-gate' { '*Deaktivierte Doppelklickaktion bleibt ohne SVN-Auftrag*' }
        'doubleclick-route' { '*Doppelklick startet den erwarteten eigenen Prozess*' }
    }
    if ($process.ExitCode -eq 0 -or (Get-Content -LiteralPath $errorPath -Raw -Encoding UTF8) -notlike $expected) { throw "GUI-Gegenprobe hat den Fehler nicht erkannt: $scenario" }
    Write-Host "GUI-Gegenprobe erkannt: $RuntimeIdentifier, $scenario"
}
