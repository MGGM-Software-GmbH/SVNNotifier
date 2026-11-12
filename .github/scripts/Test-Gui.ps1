param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [string] $ArtifactsDirectory = 'artifacts'
)

# Die Tests öffnen echte WinUI-Fenster und verwenden ausschließlich eine eigene INI-Datei.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsDirectory)) { $ArtifactsDirectory } else { Join-Path $repositoryPath $ArtifactsDirectory }
$projectPath = Join-Path $repositoryPath 'tests/Gui.Tests/Gui.Tests.csproj'
$driverPath = Join-Path $artifactsPath "gui-driver/$RuntimeIdentifier"
Invoke-QuietConsole dotnet publish $projectPath --configuration Release --runtime $RuntimeIdentifier --self-contained true --output $driverPath `
    --artifacts-path (Join-Path $artifactsPath "gui-build/$RuntimeIdentifier") `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:Platform=$($RuntimeIdentifier.Substring(4)) --nologo
if ($LASTEXITCODE -ne 0) { throw 'Die GUI-Prüfung konnte nicht gebaut werden.' }
$executablePath = Join-Path $driverPath 'Gui.Tests.exe'
foreach ($font in @('default', 'large', 'maximum', 'dark')) {
    $testPath = Join-Path $artifactsPath "gui-checks/$RuntimeIdentifier/$font"
    $arguments = @($testPath, '--capture')
    if ($font -eq 'large') { $arguments += '--large-font' }
    if ($font -eq 'maximum') { $arguments += '--maximum-font' }
    if ($font -eq 'dark') { $arguments += '--dark' }
    Invoke-QuietConsole $executablePath @arguments
    if ($LASTEXITCODE -ne 0) { throw "GUI-Prüfung fehlgeschlagen: $RuntimeIdentifier, $font" }
}
foreach ($scenario in @('missing-command', 'save-option', 'button-size', 'cleanup-command', 'cleanup-click', 'font-size')) {
    $testPath = Join-Path $artifactsPath "gui-checks/$RuntimeIdentifier/$scenario"
    New-Item -ItemType Directory -Path $testPath -Force | Out-Null
    $outputPath = Join-Path $testPath 'output.log'
    $errorPath = Join-Path $testPath 'error.log'
    $process = Start-Process -FilePath $executablePath -ArgumentList @(('"' + $testPath + '"'), "--negative-$scenario") `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
    $expected = switch ($scenario) {
        'missing-command' { '*Befehle erhalten: FileMenuItem*' }
        'save-option' { '*Benachrichtigungsdauer werden gespeichert*' }
        'button-size' { '*Schriftbedarf*' }
        'cleanup-command' { '*Befehle erhalten: ItemMenu*' }
        'cleanup-click' { '*Cleanup-Menüklick startet den Dialog*' }
        'font-size' { '*Repository-Statustext verwendet die gespeicherte Schriftgr*' }
    }
    if ($process.ExitCode -eq 0 -or (Get-Content -LiteralPath $errorPath -Raw -Encoding UTF8) -notlike $expected) { throw "GUI-Gegenprobe hat den Fehler nicht erkannt: $scenario" }
    Write-Host "GUI-Gegenprobe erkannt: $RuntimeIdentifier, $scenario"
}
