param(
    [ValidateSet('win-x64', 'win-x86')] [string] $RuntimeIdentifier = $env:RUNTIME_IDENTIFIER,
    [string] $ArtifactsDirectory = 'artifacts',
    [string] $TestDirectory = ''
)

# Prüft die echte Einzel-EXE aus dem ZIP-Paket und meldet eigene Startfehler.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
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
  $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { throw "Die Anwendung wurde beendet: Exit-Code $($process.ExitCode)" }
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    $dialogCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SettingsViewDialog')
    $settingsWindow = $windows | ForEach-Object { $_.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dialogCondition) } |
      Where-Object { $null -ne $_ -and -not $_.Current.IsOffscreen } | Select-Object -First 1
  } while ($null -eq $settingsWindow -and [DateTime]::UtcNow -lt $deadline)
  if ($null -eq $settingsWindow) {
    $textCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($window in $windows) {
      $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) | ForEach-Object { Write-Host "Eigene Startmeldung: $($_.Current.Name)" }
    }
    throw 'Die Anwendung hat den Einstellungsdialog nicht geöffnet.'
  }
  $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  if ($null -eq $settingsWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) { throw 'Der Einstellungsdialog enthält keine bedienbaren Schaltflächen.' }
  Write-Host "Anwendungsstart erfolgreich: $RuntimeIdentifier"
} finally {
  if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
