param(
    [ValidateSet('win-x64', 'win-x86')] [string] $RuntimeIdentifier = $env:RUNTIME_IDENTIFIER,
    [string] $ArtifactsDirectory = 'artifacts',
    [string] $TestDirectory = ''
)

# Prüft die echte Einzel-EXE aus dem ZIP-Paket und meldet eigene Startfehler.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-OwnProcessWindows.ps1')
Write-Host "Testprozess mit Administratorrechten: $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
$archive = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactsDirectory 'packages') -Filter "*-$RuntimeIdentifier.zip")
if ($archive.Count -ne 1) { throw 'Genau ein ZIP-Paket wird erwartet.' }
if (-not $TestDirectory) { $TestDirectory = [IO.Path]::GetTempPath() }
$testPath = Join-Path $TestDirectory ("smoke-$RuntimeIdentifier-" + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive[0].FullName -DestinationPath $testPath
foreach ($fileName in @('SVN_Notifier.exe', 'LICENSE', 'README.md')) {
  if (-not (Test-Path -LiteralPath (Join-Path $testPath $fileName))) {
    throw "Datei fehlt im ZIP-Paket: $fileName"
  }
}
# Eine portable Testkonfiguration verhindert Zugriffe auf vorhandene Benutzer-Repositories.
@('[Settings]', 'Version=1.9', 'Language=0', 'SVN_path=missing-svn.exe', 'TortoiseSVN_path=missing-tortoise.exe') |
  Set-Content -LiteralPath (Join-Path $testPath 'SVN_Notifier.ini') -Encoding ascii
$process = Start-Process -FilePath (Join-Path $testPath 'SVN_Notifier.exe') -WorkingDirectory $testPath -PassThru
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { throw "Die Anwendung wurde beendet: Exit-Code $($process.ExitCode)" }
    $windows = @(Get-OwnProcessWindows $process.Id)
    $dialogCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SettingsViewDialog')
    $settingsWindow = $windows | ForEach-Object { $_.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dialogCondition) } |
      Where-Object { $null -ne $_ -and -not $_.Current.IsOffscreen } | Select-Object -First 1
  } while ($null -eq $settingsWindow -and [DateTime]::UtcNow -lt $deadline)
  if ($null -eq $settingsWindow) {
    Write-Host "Eigene sichtbare Fenster: $($windows.Count); Prozessdesktops: $([OwnProcessWindows]::Desktops($process.Id) -join ', ')"
    $textCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($window in $windows) {
      Write-Host "Eigenes Fenster: $($window.Current.Name); Offscreen=$($window.Current.IsOffscreen)"
      $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) | ForEach-Object { Write-Host "Eigene Startmeldung: $($_.Current.Name)" }
    }
    if ($env:GITHUB_ACTIONS -eq 'true') {
      # Ausschließlich den weiterhin lebenden eigenen Prozess untersuchen, bevor die Fehlerbereinigung ihn beendet.
      . (Join-Path $PSScriptRoot 'Invoke-QuietConsole.ps1')
      $diagnostics = Join-Path ([IO.Path]::GetFullPath($ArtifactsDirectory)) ('diagnostics/' + [Guid]::NewGuid().ToString('N'))
      Invoke-QuietConsole dotnet tool install dotnet-stack --tool-path $diagnostics
      if ($LASTEXITCODE -ne 0) { throw 'Die Stackdiagnose konnte nicht installiert werden; der Einstellungsdialog fehlt weiterhin.' }
      Invoke-QuietConsole (Join-Path $diagnostics 'dotnet-stack.exe') report --process-id $process.Id
    }
    throw 'Die Anwendung hat den Einstellungsdialog nicht geöffnet.'
  }
  $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  if ($null -eq $settingsWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) { throw 'Der Einstellungsdialog enthält keine bedienbaren Schaltflächen.' }
  Write-Host "Anwendungsstart erfolgreich: $RuntimeIdentifier"
} finally {
  if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
