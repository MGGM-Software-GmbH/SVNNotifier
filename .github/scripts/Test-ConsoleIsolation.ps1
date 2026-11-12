param([Parameter(Mandatory)] [string] $ArtifactsDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
$fixture = (Resolve-Path (Join-Path $PSScriptRoot '../../tests/fixtures/ConsoleProbe.ps1')).Path
$resultDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)
if (Test-Path -LiteralPath $resultDirectory) { throw 'Die Konsolen-Gegenprobe benötigt einen neuen Ergebnisordner.' }
New-Item -ItemType Directory -Path $resultDirectory | Out-Null
$expected = @('-p:First=one', '-p:Second=two', 'Ordner mit Leerzeichen und äß\', 'Literal "mit" Anführungszeichen', 'Literal $() und `Zeichen')
foreach ($scenario in @('valid', 'failure')) {
    $resultPath = Join-Path $resultDirectory ($scenario + '.json')
    $parameters = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $fixture, $resultPath) + $expected
    if ($scenario -eq 'failure') { $parameters += '--failure' }
    Invoke-QuietConsole (Join-Path $PSHOME 'powershell.exe') @parameters | Out-Host
    $expectedExit = if ($scenario -eq 'failure') { 9 } else { 0 }
    if ($LASTEXITCODE -ne $expectedExit) { throw "Kindprozess-Exit-Code stimmt nicht: $scenario, $LASTEXITCODE" }
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($result.visible) { throw 'Eine Kindkonsole ist sichtbar.' }
    # powershell.exe -File zerlegt unbekannte benannte Argumente am Doppelpunkt.
    $parsed = @('-p', 'First=one', '-p', 'Second=two') + $expected[2..4]
    if ($scenario -eq 'failure') { $parsed += '--failure' }
    if ($result.arguments.Count -ne $parsed.Count) { throw 'Anzahl der Kindprozessargumente stimmt nicht.' }
    for ($index = 0; $index -lt $parsed.Count; $index++) {
        if ($result.arguments[$index] -cne $parsed[$index]) { throw "Argumentübergabe verändert: Index $index" }
    }
    Write-Host "Konsolen-Gegenprobe: $scenario, unsichtbar, Exit-Code $expectedExit, Argumente erhalten"
}
