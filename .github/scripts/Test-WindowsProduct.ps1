# Prüft das gebaute Produkt mit normalen Benutzerrechten und eigenen Testkonfigurationen.
$ErrorActionPreference = 'Stop'
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Produktprüfungen müssen ohne Administratorrechte laufen.'
}
if ($env:RUNTIME_IDENTIFIER -notin @('win-x64', 'win-x86')) { throw 'Testarchitektur fehlt oder ist ungültig.' }
& .github/scripts/Test-PackageStart.ps1
& .github/scripts/Test-ApplicationUpdater.ps1 -RuntimeIdentifier $env:RUNTIME_IDENTIFIER
& .github/scripts/Test-StartModes.ps1 -RuntimeIdentifier $env:RUNTIME_IDENTIFIER -ArtifactsDirectory artifacts
$ErrorActionPreference = 'Stop'
$legacyPath = Join-Path $env:RUNNER_TEMP 'svnnotifier-legacy'
New-Item -ItemType Directory -Path $legacyPath | Out-Null
$packageName = "SVNNotifier-3.0.0.13-$env:RUNTIME_IDENTIFIER.zip"
$baseUrl = 'https://github.com/MGGM-Software-GmbH/SVNNotifier/releases/download/v3.0.0.13'
Invoke-WebRequest "$baseUrl/$packageName" -OutFile (Join-Path $legacyPath $packageName) -UseBasicParsing
Invoke-WebRequest "$baseUrl/SHA256SUMS.txt" -OutFile (Join-Path $legacyPath 'SHA256SUMS.txt') -UseBasicParsing
$checksumLines = @(Get-Content (Join-Path $legacyPath 'SHA256SUMS.txt') | Where-Object { $_ -match ('^[a-fA-F0-9]{64}\s+\*?' + [regex]::Escape($packageName) + '$') })
if ($checksumLines.Count -ne 1) { throw 'Eindeutige Prüfsumme des WinForms-Pakets fehlt.' }
$expectedHash = ($checksumLines[0] -split '\s+')[0]
if ((Get-FileHash (Join-Path $legacyPath $packageName) -Algorithm SHA256).Hash -ine $expectedHash) { throw 'Prüfsumme des WinForms-Pakets stimmt nicht.' }
./.github/scripts/Test-LegacyUpgrade.ps1 -RuntimeIdentifier $env:RUNTIME_IDENTIFIER -ArtifactsDirectory artifacts -LegacyPackagesDirectory $legacyPath
& .github/scripts/Test-Gui.ps1 -RuntimeIdentifier $env:RUNTIME_IDENTIFIER
& .github/scripts/Test-SvnOperations.ps1 -RuntimeIdentifier $env:RUNTIME_IDENTIFIER
