# Prüft den eingeschränkten CI-Prozess vor Paketbuild und Produktprüfungen.
$ErrorActionPreference = 'Stop'
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Der Testprozess besitzt weiterhin Administratorrechte.'
}
Write-Host 'Windows-Testprozess ohne aktive Administratorgruppe gestartet.'
if (-not ([Security.Principal.WindowsIdentity]::GetCurrent()).Groups.Value.Contains('S-1-5-32-545')) {
    throw 'Dem Testkonto fehlt die normale Windows-Benutzergruppe.'
}
Write-Host "Testsitzung: $([Diagnostics.Process]::GetCurrentProcess().SessionId)"

# Eine eigene echte Windows-Ansicht trennt Desktop-/UI-Automation-Probleme vom WinUI-Produktstart.
. (Join-Path $PSScriptRoot 'Get-OwnProcessWindows.ps1')
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object Windows.Forms.Form
$form.Text = 'SVNNotifier-CI-Umgebung'
try {
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $nativeWindows = @(Get-OwnProcessWindows $PID)
    if (-not @($nativeWindows | Where-Object { $_.Current.NativeWindowHandle -eq $form.Handle.ToInt32() }).Count) {
        throw 'Das eigene Windows-Testfenster ist auf dem Testdesktop nicht über UI Automation erreichbar.'
    }
    if (@(Get-OwnProcessWindows ([int]::MaxValue)).Count) { throw 'Die Fensterzuordnung akzeptiert eine fremde Prozess-ID.' }
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $PID)
    $rootWindows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $condition)
    Write-Host "Eigene UI-Automation-Fenster: native Zuordnung=$($nativeWindows.Count); Desktopwurzel=$($rootWindows.Count)"
} finally { $form.Close(); $form.Dispose() }
