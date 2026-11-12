param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [string] $ArtifactsDirectory = 'artifacts'
)

# Prüft den Austausch nur in eigenen Verzeichnissen mit einer portablen Testkonfiguration.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
$repositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsDirectory)) { $ArtifactsDirectory } else { Join-Path $repositoryPath $ArtifactsDirectory }
$archives = @(Get-ChildItem -LiteralPath (Join-Path $artifactsPath 'packages') -Filter "*-$RuntimeIdentifier.zip")
if ($archives.Count -ne 1) { throw 'Genau ein Release-Paket wird erwartet.' }
$projectPath = Join-Path $repositoryPath 'tests/Updater.Tests/Updater.Tests.csproj'
Invoke-QuietConsole dotnet run --project $projectPath --configuration Release --artifacts-path (Join-Path $artifactsPath "test-build/$RuntimeIdentifier") -- $RuntimeIdentifier $archives[0].FullName
if ($LASTEXITCODE -ne 0) { throw 'Die Updater-Regressionstests sind fehlgeschlagen.' }
$driverPath = Join-Path $artifactsPath "test-driver/$RuntimeIdentifier"
Invoke-QuietConsole dotnet publish $projectPath --configuration Release --runtime $RuntimeIdentifier --self-contained true --output $driverPath `
    --artifacts-path (Join-Path $artifactsPath "driver-build/$RuntimeIdentifier") `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none --nologo
if ($LASTEXITCODE -ne 0) { throw 'Der Installer-Testprozess konnte nicht gebaut werden.' }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$payloadPath = Join-Path $artifactsPath "publish/$RuntimeIdentifier/SVN_Notifier.exe"
$expectedHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
$installationRun = [Guid]::NewGuid().ToString('N')
foreach ($scenario in @('valid', 'tampered', 'invalid')) {
    $testPath = Join-Path $artifactsPath "installation-test/$RuntimeIdentifier/$installationRun/$scenario"
    if (Test-Path -LiteralPath $testPath) { throw "Der Testordner existiert bereits: $testPath" }
    New-Item -ItemType Directory -Path $testPath -Force | Out-Null
    $targetPath = Join-Path $testPath 'SVN_Notifier.exe'
    Copy-Item -LiteralPath (Join-Path $driverPath 'SVN_Notifier.exe') -Destination $targetPath
    $originalHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
    $configPath = Join-Path $testPath 'SVN_Notifier.ini'
    @('[Settings]', 'Version=1.9', 'Language=0', 'CheckForNewVersion=false', 'SVN_path=missing-svn.exe', 'TortoiseSVN_path=missing-tortoise.exe', '[Folders]', ('Folder1=' + (Join-Path $testPath 'isolated-test-fixture') + '|-1|-1|False|0|True')) |
        Set-Content -LiteralPath $configPath -Encoding ascii
    $configHash = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
    $mode = switch ($scenario) { 'valid' { '--install-payload' } 'tampered' { '--install-payload-tampered' } 'invalid' { '--install-payload-invalid' } }
    $arguments = @($mode, ('"' + $payloadPath + '"'))
    $driverStart = New-Object System.Diagnostics.ProcessStartInfo
    $driverStart.FileName = $targetPath
    $driverStart.WorkingDirectory = $testPath
    $driverStart.Arguments = $arguments -join ' '
    $driverStart.UseShellExecute = $false
    $driverStart.CreateNoWindow = $true
    $driver = [System.Diagnostics.Process]::Start($driverStart)
    $updatedProcess = $null
    try {
        if (-not $driver.WaitForExit(60000)) { throw 'Der ursprüngliche Testprozess wurde nicht beendet.' }
        if ($scenario -ne 'invalid' -and $driver.ExitCode -ne 0) { throw "Installer-Start fehlgeschlagen: $($driver.ExitCode)" }
        if ($scenario -eq 'invalid' -and $driver.ExitCode -eq 0) { throw 'Der fehlerhafte Installer-Start wurde als Erfolg behandelt.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        if ($scenario -eq 'valid') {
            $settingsWindow = $null
            do {
                Start-Sleep -Milliseconds 500
                $candidates = @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object {
                    -not $_.HasExited -and $null -ne $_.MainModule -and $_.MainModule.FileName -eq $targetPath
                })
                if ($candidates.Count -gt 0) {
                    $updatedProcess = $candidates[0]
                    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $updatedProcess.Id)
                    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
                    $dialogCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SettingsViewDialog')
                    $settingsWindow = $windows | ForEach-Object { $_.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dialogCondition) } |
                        Where-Object { $null -ne $_ -and -not $_.Current.IsOffscreen } | Select-Object -First 1
                }
            } while ($null -eq $settingsWindow -and [DateTime]::UtcNow -lt $deadline)
            if ($null -eq $settingsWindow) {
                if ($null -ne $updatedProcess -and -not $updatedProcess.HasExited) {
                    foreach ($window in $windows) {
                        $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
                            ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -First 30 | Out-Host
                    }
                }
                throw 'Die aktualisierte Anwendung hat den Einstellungsdialog nicht geöffnet.'
            }
            if ((Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Die neue EXE wurde nicht installiert.' }
            do {
                $stages = @(Get-ChildItem -LiteralPath $testPath -Directory -Force | Where-Object Name -Like '.svnnotifier-update-*')
                if ($stages.Count -eq 0) { break }
                Start-Sleep -Milliseconds 250
            } while ([DateTime]::UtcNow -lt $deadline)
            if ($stages.Count -ne 0) { throw 'Die Installationsdateien wurden nach dem Neustart nicht entfernt.' }
            Write-Host "Installer-Test erfolgreich: $RuntimeIdentifier, Austausch und Neustart"
        } else {
            $errorPath = Join-Path $testPath 'installer-error.txt'
            while (-not (Test-Path -LiteralPath $errorPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
            if (-not (Test-Path -LiteralPath $errorPath)) { throw 'Die Manipulation wurde nicht gemeldet.' }
            $expectedError = if ($scenario -eq 'tampered') { '*staged executable has changed*' } else { '*not a Windows executable*' }
            if ((Get-Content -LiteralPath $errorPath -Raw) -notlike $expectedError) { throw 'Unerwarteter Installer-Fehler.' }
            if ((Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash -ne $originalHash) { throw 'Die ursprüngliche EXE wurde trotz Manipulation ersetzt.' }
            Write-Host "Installer-Gegenprobe erkannt: $RuntimeIdentifier, $scenario, ursprüngliche EXE erhalten"
        }
        if ((Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash -ne $configHash) { throw 'Die Benutzerkonfiguration wurde verändert.' }
    } finally {
        if ($null -ne $updatedProcess -and -not $updatedProcess.HasExited) { Stop-Process -Id $updatedProcess.Id }
        if (-not $driver.HasExited) { Stop-Process -Id $driver.Id }
        $driver.Dispose()
        if ($null -ne $updatedProcess) { $updatedProcess.Dispose() }
    }
}
