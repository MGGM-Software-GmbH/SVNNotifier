param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [string] $ReleaseTag = ''
)

# Erstellt ein portables Windows-Paket mit konsistenter Version und Prüfsumme.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$projectPath = Join-Path $repositoryPath 'SVN_Notifier/SVN_Notifier.csproj'
[xml] $project = Get-Content -LiteralPath $projectPath -Raw
$properties = $project.Project.PropertyGroup[0]
$version = [string] $properties.Version

if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Ungültige Projektversion: $version"
}
foreach ($propertyName in @('AssemblyVersion', 'FileVersion', 'InformationalVersion')) {
    if ([string] $properties.$propertyName -cne $version) {
        throw "$propertyName stimmt nicht mit der Projektversion $version überein."
    }
}
if ($ReleaseTag -and $ReleaseTag -cne "v$version") {
    throw "Release-Tag $ReleaseTag stimmt nicht mit der Projektversion v$version überein."
}
if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'Das Paket muss unter Windows gebaut und geprüft werden.'
}

$artifactsPath = Join-Path $repositoryPath 'artifacts'
$publishPath = Join-Path $artifactsPath "publish/$RuntimeIdentifier"
$packagePath = Join-Path $artifactsPath 'packages'
$archiveName = "SVNNotifier-$version-$RuntimeIdentifier.zip"
$archivePath = Join-Path $packagePath $archiveName
if (Test-Path -LiteralPath $publishPath) {
    throw "Der Ausgabeordner existiert bereits: $publishPath. Für einen neuen Build einen sauberen Checkout verwenden."
}
if (Test-Path -LiteralPath $archivePath) {
    throw "Das Paket existiert bereits: $archivePath"
}

dotnet publish $projectPath --configuration Release --runtime $RuntimeIdentifier `
    --self-contained true --output $publishPath `
    --artifacts-path (Join-Path $artifactsPath "build/$RuntimeIdentifier") `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:PublishReadyToRun=false --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ist mit Exit-Code $LASTEXITCODE fehlgeschlagen."
}

$executablePath = Join-Path $publishPath 'SVN_Notifier.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($versionInfo.FileVersion -cne $version -or $versionInfo.ProductVersion -cne $version) {
    throw "Die EXE enthält eine abweichende Datei- oder Produktversion: $($versionInfo.FileVersion), $($versionInfo.ProductVersion)"
}
Copy-Item -LiteralPath (Join-Path $repositoryPath 'LICENSE') -Destination $publishPath
Copy-Item -LiteralPath (Join-Path $repositoryPath 'README.md') -Destination $publishPath
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $archivePath
$checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archivePath.sha256" -Value "$checksum  $archiveName" -Encoding ascii
Write-Host "Paket erstellt: $archivePath (Version $version)"
