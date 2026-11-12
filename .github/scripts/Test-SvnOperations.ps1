param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [string] $ArtifactsDirectory = 'artifacts',
    [string] $SvnDirectory = ''
)

# Alle Prozesse und Konfigurationen verwenden eigene Testverzeichnisse.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsDirectory)) { $ArtifactsDirectory } else { Join-Path $repositoryPath $ArtifactsDirectory }
$driverPath = Join-Path $artifactsPath "svn-driver/$RuntimeIdentifier"
$testPath = Join-Path $artifactsPath "svn-checks/$RuntimeIdentifier"
if (Test-Path -LiteralPath $testPath) { throw 'Das SVN-Testverzeichnis muss neu sein.' }
Invoke-QuietConsole dotnet publish (Join-Path $repositoryPath 'tests/Svn.Tests/Svn.Tests.csproj') --configuration Release --runtime $RuntimeIdentifier `
    --self-contained true --output $driverPath --artifacts-path (Join-Path $artifactsPath "svn-build/$RuntimeIdentifier") `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo
if ($LASTEXITCODE -ne 0) { throw 'Die SVN-Prüfung konnte nicht gebaut werden.' }
$arguments = @($testPath)
if ($SvnDirectory) { $arguments += @('--svn-directory', $SvnDirectory) }
Invoke-QuietConsole (Join-Path $driverPath 'Svn.Tests.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Die SVN-Koordinationsprüfung ist fehlgeschlagen.' }
