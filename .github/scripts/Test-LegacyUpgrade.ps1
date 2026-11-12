param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-x86')] [string] $RuntimeIdentifier,
    [Parameter(Mandatory)] [string] $ArtifactsDirectory,
    [Parameter(Mandatory)] [string] $LegacyPackagesDirectory
)

# Prüft den echten Versionsübergang mit eigenen Dateien und ohne erforderliche Shell-Interaktion.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-OwnProcessWindows.ps1')
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$legacyArchive = Join-Path ([IO.Path]::GetFullPath($LegacyPackagesDirectory)) "SVNNotifier-3.0.0.13-$RuntimeIdentifier.zip"
$directory = Join-Path $artifacts ('legacy-upgrade/' + $RuntimeIdentifier + '/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
Expand-Archive -LiteralPath $legacyArchive -DestinationPath $directory
$target = Join-Path $directory 'SVN_Notifier.exe'
$payload = Join-Path $artifacts "publish/$RuntimeIdentifier/SVN_Notifier.exe"
$client = Join-Path $directory 'client.exe'
Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32/cmd.exe') -Destination $client
$repository = Join-Path $directory 'Isolierter Repository-Eintrag'
$configuration = Join-Path $directory 'SVN_Notifier.ini'
@('[Settings]', 'Version=1.9', 'Language=0', 'CheckForNewVersion=false', 'HideOnStartup=false', 'ShowInTaskbar=true',
  "SVN_path=$client", "TortoiseSVN_path=$client", '[Folders]', "Folder1=$repository|-1|-1|True|0|True") |
    Set-Content -LiteralPath $configuration -Encoding UTF8
$owned = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
function Start-Own([string] $executable, [string] $workingDirectory, [string] $arguments = '') {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $executable; $info.WorkingDirectory = $workingDirectory; $info.Arguments = $arguments
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($info); $owned.Add($process); return $process
}
function Find-Own($process, [string] $property, [string] $value) {
    $searchProperty = if ($property -eq 'Name') { [System.Windows.Automation.AutomationElement]::NameProperty } else { [System.Windows.Automation.AutomationElement]::AutomationIdProperty }
    $search = New-Object System.Windows.Automation.PropertyCondition($searchProperty, $value)
    foreach ($window in @(Get-OwnProcessWindows $process.Id)) {
        $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $search)
        if ($null -ne $found) { return $found }
    }
    return $null
}
function Wait-Own([scriptblock] $condition, [string] $message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { if (& $condition) { Write-Host "OK: $message"; return }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw $message
}
try {
    $original = Start-Own $target $directory
    Wait-Own { $null -ne (Find-Own $original 'AutomationId' 'ItemListView') } 'WinForms-Ausgangspaket zeigt die eigene Repository-Liste'
    $stage = Join-Path $directory ('.svnnotifier-update-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $payload -Destination (Join-Path $stage 'payload.exe')
    $helper = Join-Path $stage 'installer.exe'
    Copy-Item -LiteralPath $target -Destination $helper
    $readyName = 'Local\SVNNotifier.Update.' + [Guid]::NewGuid().ToString('N')
    $ready = New-Object Threading.EventWaitHandle($false, [Threading.EventResetMode]::ManualReset, $readyName)
    try {
        $plan = @{ TargetPath=$target; WorkingDirectory=$directory; ParentId=$original.Id; ParentStartTicks=$original.StartTime.ToUniversalTime().Ticks;
            TargetHash=(Get-FileHash $target -Algorithm SHA256).Hash; PayloadHash=(Get-FileHash $payload -Algorithm SHA256).Hash;
            Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($payload).FileVersion; Runtime=$RuntimeIdentifier; ReadyEvent=$readyName; Language=0 }
        $planPath = Join-Path $stage 'update.json'
        $plan | ConvertTo-Json | Set-Content -LiteralPath $planPath -Encoding UTF8
        $installer = Start-Own $helper $stage ('--apply-update "' + $planPath + '"')
        if (-not $ready.WaitOne(15000)) { throw 'Der Installer des Ausgangspakets hat die Übergabe nicht bestätigt.' }
        # Der entfernte eigene Testclient führt beim Neustart in die echte Konfiguration statt in den Shell-Tray.
        if (-not (Test-Path -LiteralPath $client -PathType Leaf) -or [IO.Path]::GetDirectoryName($client) -ne $directory) { throw 'Eigener Testclient ist nicht eindeutig.' }
        [IO.File]::Delete($client)
        (Find-Own $original 'Name' 'File').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Wait-Own { $null -ne (Find-Own $original 'Name' 'Exit') } 'Vorhandener Beenden-Befehl ist erreichbar'
        (Find-Own $original 'Name' 'Exit').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        if (-not $original.WaitForExit(15000)) { throw 'Das Ausgangsprogramm wurde nicht beendet.' }
        $configurationHash = (Get-FileHash $configuration -Algorithm SHA256).Hash
        Wait-Own {
            $script:restarted = Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited -and $null -ne $_.MainModule -and $_.MainModule.FileName -eq $target } | Select-Object -First 1
            $null -ne $script:restarted -and $null -ne (Find-Own $script:restarted 'AutomationId' 'SettingsViewDialog')
        } 'WinUI-Neustart erreicht den produktiven Einstellungsdialog'
        $owned.Add($script:restarted)
        if ((Get-FileHash $target -Algorithm SHA256).Hash -ne (Get-FileHash $payload -Algorithm SHA256).Hash) { throw 'Die installierte EXE stimmt nicht mit dem WinUI-Paket überein.' }
        if ((Get-FileHash $configuration -Algorithm SHA256).Hash -ne $configurationHash) { throw 'Die Konfiguration wurde beim Neustart verändert.' }
        if ((Get-Content $configuration -Raw) -notlike ('*' + $repository + '*')) { throw 'Der Repository-Eintrag fehlt nach dem Neustart.' }
        Wait-Own { -not (Test-Path -LiteralPath $stage) } 'Staging wird durch den produktiven Neustart entfernt'
        Write-Host "Alt-zu-Neu-Prüfung erfolgreich: $RuntimeIdentifier, echte Ausgangs-EXE 3.0.0.13, WinUI-Neustart, INI und Repository erhalten."
    } finally { $ready.Dispose() }
} finally {
    foreach ($process in $owned) {
        try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } } finally { $process.Dispose() }
    }
}
