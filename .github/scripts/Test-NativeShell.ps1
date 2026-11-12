param(
    [Parameter(Mandatory)] [string] $ArtifactsDirectory,
    [Parameter(Mandatory)] [switch] $UseUserDesktop
)

# Dieser Lauf benötigt eine ausdrücklich erlaubte sichtbare Windows-Testsitzung.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $UseUserDesktop) { throw 'Für diesen Test ist die Freigabe einer sichtbaren Testsitzung erforderlich.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeShellInput {
    [StructLayout(LayoutKind.Sequential)] private struct HighContrast { public uint Size, Flags; public IntPtr Scheme; }
    public sealed class ContrastState { public uint Flags; public string Scheme; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool SystemParametersInfo(uint action, uint size, ref HighContrast value, uint flags);
    public static ContrastState ReadContrast() {
        var value = new HighContrast { Size = (uint)Marshal.SizeOf(typeof(HighContrast)) };
        if (!SystemParametersInfo(0x42, value.Size, ref value, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new ContrastState { Flags = value.Flags, Scheme = Marshal.PtrToStringUni(value.Scheme) };
    }
    public static void SetContrast(ContrastState state, bool enabled) { WriteContrast(state, enabled ? state.Flags | 1u : state.Flags & ~1u); }
    public static void RestoreContrast(ContrastState state) { WriteContrast(state, state.Flags); }
    private static void WriteContrast(ContrastState state, uint flags) {
        var value = new HighContrast { Size = (uint)Marshal.SizeOf(typeof(HighContrast)), Flags = flags, Scheme = Marshal.StringToHGlobalUni(state.Scheme) };
        try { if (!SystemParametersInfo(0x43, value.Size, ref value, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        finally { Marshal.FreeHGlobal(value.Scheme); }
    }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    public static uint WindowProcess(IntPtr window) { uint process; GetWindowThreadProcessId(window, out process); return process; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct CursorPoint { public int X, Y; }
    public static void Click(int x, int y) {
        CursorPoint saved; GetCursorPos(out saved); SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(saved.X, saved.Y);
    }
    public static void RightClick(int x, int y) {
        CursorPoint saved; GetCursorPos(out saved); SetCursorPos(x, y);
        mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(saved.X, saved.Y);
    }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void NotificationCenter() {
        keybd_event(0x5b, 0, 0, UIntPtr.Zero); keybd_event(0x4e, 0, 0, UIntPtr.Zero);
        keybd_event(0x4e, 0, 2, UIntPtr.Zero); keybd_event(0x5b, 0, 2, UIntPtr.Zero);
    }
    public static void Escape() { keybd_event(0x1b, 0, 0, UIntPtr.Zero); keybd_event(0x1b, 0, 2, UIntPtr.Zero); }
    public static void Key(byte key) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
    public static void AltKey(byte key) { keybd_event(0x12, 0, 0, UIntPtr.Zero); Key(key); keybd_event(0x12, 0, 2, UIntPtr.Zero); }
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    public static bool IsOwnedBy(IntPtr window, IntPtr owner) { return GetAncestor(window, 3) == owner; }
}
'@
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$run = Join-Path $artifacts ('native-shell/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
Start-Transcript -LiteralPath (Join-Path $run 'test.log') | Out-Null
$processes = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$failures = New-Object 'System.Collections.Generic.List[string]'
$windowHandles = @{}
$desktopTestStarted = [DateTime]::UtcNow
Write-Host "Sichtbare Testsitzung beginnt: $($desktopTestStarted.ToString('o'))"

function Check($condition, [string] $message) {
    if (-not $condition) { throw $message }
    Write-Host "OK: $message"
}
function Wait-Test([scriptblock] $condition, [string] $message, [int] $seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do { if (& $condition) { Write-Host "OK: $message"; return }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw $message
}
function Scenario([string] $name, [scriptblock] $test) {
    Write-Host "PRÜFUNG: $name"
    try { & $test } catch { $failures.Add($name + ': ' + $_.Exception.Message); Write-Host ('FEHLER: ' + $_.Exception.Message) }
}
function Start-Product([string] $exe, [string] $directory, [string] $arguments = '', [bool] $proxyFailure = $false) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe; $start.WorkingDirectory = $directory; $start.Arguments = $arguments
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    if ($proxyFailure) { $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:9' }
    $process = [Diagnostics.Process]::Start($start); $processes.Add($process)
    return $process
}
function App-Window($process) {
    if ($process.HasExited) { throw "Anwendungsprozess beendet: $($process.ExitCode)" }
    $process.Refresh()
    $handle = $process.MainWindowHandle
    if ($handle -ne [IntPtr]::Zero) { $windowHandles[$process.Id] = $handle }
    elseif ($windowHandles.ContainsKey($process.Id)) { $handle = $windowHandles[$process.Id] }
    if ($handle -eq [IntPtr]::Zero -or -not [NativeShellInput]::IsWindow($handle) -or [NativeShellInput]::WindowProcess($handle) -ne $process.Id) { return $null }
    return [System.Windows.Automation.AutomationElement]::FromHandle($handle)
}
function By-Id($root, [string] $id) {
    if ($null -eq $root) { return $null }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -ne $found) { return $found }
    # XAML-Popups können als eigenes UIA-Fenster desselben Prozesses erscheinen.
    foreach ($popup in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($popup.Current.ProcessId -ne $root.Current.ProcessId -or $popup.Current.IsOffscreen) { continue }
        $found = $popup.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $found) { return $found }
    }
    return $null
}
function By-Name($root, [string] $name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Capture($element, [string] $name) {
    $window = [IntPtr]$element.Current.NativeWindowHandle
    if ($window -ne [IntPtr]::Zero) {
        [NativeShellInput]::ShowWindow($window, 9) | Out-Null
        [NativeShellInput]::SetForegroundWindow($window) | Out-Null
        Start-Sleep -Milliseconds 300
        Check ([NativeShellInput]::GetForegroundWindow() -eq $window) "Eigenes Testfenster ist für die Aufnahme im Vordergrund: $name"
    }
    $bounds = $element.Current.BoundingRectangle
    Check ($bounds.Width -gt 0 -and $bounds.Height -gt 0) "Bildgrenzen vorhanden: $name"
    $bitmap = New-Object Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $run ($name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Own-Notification([string] $marker) {
    $shellIds = @(Get-Process ShellExperienceHost, ShellHost, explorer -ErrorAction SilentlyContinue | ForEach-Object Id)
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($window.Current.ProcessId -notin $shellIds) { continue }
        $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
        foreach ($text in $texts) {
            if (-not $text.Current.IsOffscreen -and $text.Current.Name.Contains($marker)) { return $text }
        }
    }
    return $null
}
function Click-OwnNotification($text) {
    # Ausschließlich der eindeutig zugeordnete eigene Meldungstext ist das Klickziel.
    $bounds = $text.Current.BoundingRectangle
    Check ($bounds.Width -gt 0 -and $bounds.Height -gt 0) 'Eigene Meldung besitzt ein sichtbares Klickziel'
    [NativeShellInput]::Click([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
}
function Own-TrayIcon {
    $shellIds = @(Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object Id)
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($window.Current.ProcessId -notin $shellIds) { continue }
        foreach ($element in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            if (-not $element.Current.IsOffscreen -and $element.Current.Name.Contains('SVN Notifier') -and $element.Current.Name.Contains('PRP-Systemtest A')) { return $element }
        }
    }
    return $null
}
function Display-ScaleCombo($settings) {
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    foreach ($combo in $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)) {
        $selection = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()
        if ($selection.Count -gt 0 -and $selection[0].Current.Name -match '^\s*\d+\s*%') { return $combo }
    }
    return $null
}
function Select-DisplayScale($settings, [int] $percentage) {
    $combo = Display-ScaleCombo $settings
    if ($null -eq $combo) { throw 'Windows-Skalierungsauswahl ist nicht über UI Automation erreichbar.' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 200
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $option = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { $_.Current.Name -match ('^\s*' + $percentage + '\s*%') } | Select-Object -First 1
    if ($null -eq $option) { throw "Windows bietet $percentage Prozent in dieser Anzeige nicht über den geprüften Bedienweg an." }
    $option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2
    $selected = (Display-ScaleCombo $settings).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()[0].Current.Name
    Check ($selected -match ('^\s*' + $percentage + '\s*%')) "Windows-Skalierung beträgt $percentage Prozent"
}
function Contexts([string] $exe, [string] $config) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $identity = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($exe.ToUpperInvariant())))).Replace('-', '').Substring(0,24) }
    finally { $hash.Dispose() }
    $store = Join-Path $env:LOCALAPPDATA ('MGGM/SVNNotifier/Notifications/' + $identity)
    if (-not (Test-Path -LiteralPath $store)) { return @() }
    return @(Get-ChildItem -LiteralPath $store -Filter '*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
        Where-Object { $_.Configuration -eq $config -and [DateTimeOffset]::Parse($_.Created).UtcDateTime -ge $desktopTestStarted })
}
function Make-Fixture([string] $name, [bool] $autoUpdate, [bool] $checkUpdates) {
    $directory = Join-Path $run $name
    $workingCopy = Join-Path $directory ('PRP-Systemtest ' + $name)
    New-Item -ItemType Directory -Path (Join-Path $workingCopy '.svn') | Out-Null
    'Isolierte synthetische Testarbeitskopie.' | Set-Content (Join-Path $workingCopy '.svn/wc.db')
    if ($autoUpdate) { 'Isolierter erfolgreicher Aktualisierungslauf.' | Set-Content (Join-Path $workingCopy 'notification-autoupdate-fixture') }
    $driver = Join-Path $artifacts 'svn-driver/win-x64/Svn.Tests.exe'
    $config = Join-Path $directory 'SVN_Notifier.ini'
    @('[Settings]', 'Version=1.9', 'Language=1', 'FontFamily=Segoe UI', 'FontSize=8.5', 'HideOnStartup=false', 'ShowInTaskbar=true',
      "CheckForNewVersion=$checkUpdates", "SVN_path=$driver", "TortoiseSVN_path=$driver", 'DoPauseAfterApplicationStartup=false', 'ItemDoubleClickAction=4',
      'DefaultActiveStatusUpdateInterval=2', 'DefaultIdleStatusUpdateInterval=2', '[Folders]',
      ('Folder1=' + $workingCopy + '|-1|-1|False|0|' + (-not $autoUpdate))) | Set-Content -LiteralPath $config -Encoding UTF8
    return @{ Directory=$directory; WorkingCopy=$workingCopy; Configuration=$config }
}
function Force-StatusA {
    $window = App-Window $applicationA
    [NativeShellInput]::ShowWindow([IntPtr]$window.Current.NativeWindowHandle, 9) | Out-Null
    [NativeShellInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
    $list = By-Id $window 'Repositories'
    $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()[0].SetFocus()
    [NativeShellInput]::Key(0x0d)
}
try {
    $exe = Join-Path $artifacts 'publish/win-x64/SVN_Notifier.exe'
    $fixtureA = Make-Fixture 'A' $false $false
    $fixtureB = Make-Fixture 'B' $true $true
    $applicationA = Start-Product $exe $fixtureA.Directory
    Scenario 'Produktstart und eingehende Änderungen' {
        Wait-Test { $null -ne (By-Id (App-Window $applicationA) 'Repositories') } 'Produktive Repository-Liste wird geöffnet'
        Wait-Test { @(Contexts $exe $fixtureA.Configuration | Where-Object Kind -eq 0).Count -gt 0 } 'Eingehende Änderung erzeugt einen eigenen Meldungskontext'
        Wait-Test { $null -ne (Own-Notification 'PRP-Systemtest A') } 'Echte Windows-Banneranzeige für den eigenen Eintrag' 10
        Capture (Own-Notification 'PRP-Systemtest A') 'banner-incoming-text'
        Capture (App-Window $applicationA) 'product-incoming'
    }
    $applicationB = Start-Product $exe $fixtureB.Directory '' $true
    Scenario 'Stilles Auto-Update und automatischer Programmprüffehler' {
        Wait-Test { Test-Path (Join-Path $fixtureB.WorkingCopy 'notification-update-completed') } 'Stilles Update wurde tatsächlich ausgeführt'
        Wait-Test { @(Contexts $exe $fixtureB.Configuration | Where-Object Kind -eq 1).Count -gt 0 } 'Abgeschlossenes Auto-Update erzeugt eine Erfolgsmeldung'
        Wait-Test { @(Contexts $exe $fixtureB.Configuration | Where-Object Kind -eq 2).Count -gt 0 } 'Erzwungener Netzwerkfehler erzeugt die automatische Programmmeldung'
        Capture (App-Window $applicationB) 'product-autoupdate'
    }
    Scenario 'Aktivierung bei zwei laufenden Konfigurationen' {
        $context = @(Contexts $exe $fixtureA.Configuration | Where-Object Kind -eq 0)[0]
        [NativeShellInput]::PostMessage($applicationA.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 300
        Check (-not $applicationA.HasExited) 'Schließen erhält den Tray-Prozess'
        $activation = Start-Product $exe $fixtureB.Directory ('--notification ' + $context.Id)
        Check ($activation.WaitForExit(15000) -and $activation.ExitCode -eq 0) 'Aktivierung wird ohne zusätzlichen dauerhaften Prozess weitergeleitet'
        $list = By-Id (App-Window $applicationA) 'Repositories'
        $selection = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()
        Check ($selection.Count -eq 1) 'Die ursprüngliche Instanz erhält die Auswahl'
        Capture (App-Window $applicationA) 'activation-origin'
    }
    Scenario 'Echte Tastatureingaben, Einstellungen und native Ordnerauswahl' {
        try {
        $window = App-Window $applicationA
        Capture $window 'keyboard-start'
        $toggle = By-Id $window 'MonitorToggle'
        $toggle.SetFocus()
        $togglePattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $previous = $togglePattern.Current.ToggleState
        [NativeShellInput]::Key(0x20)
        Wait-Test { $togglePattern.Current.ToggleState -ne $previous } 'Leertaste schaltet die fokussierte Repository-Prüfung'
        [NativeShellInput]::Key(0x20)
        Wait-Test { $togglePattern.Current.ToggleState -eq $previous } 'Leertaste stellt den ursprünglichen Repository-Schalter wieder her'
        [NativeShellInput]::AltKey(0x44)
        Wait-Test { $null -ne (By-Id $window 'ImportConfiguration') } 'Alt+D öffnet das deutsche Dateimenü'
        [NativeShellInput]::Escape()
        [NativeShellInput]::AltKey(0x57)
        Wait-Test { $null -ne (By-Id $window 'Settings') } 'Alt+W öffnet das deutsche Werkzeugmenü'
        (By-Id $window 'Settings').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Test { $null -ne (By-Id $window 'SettingsViewDialog') } 'Einstellungen werden über den tatsächlichen Menüweg geöffnet'
        $dialog = By-Id $window 'SettingsViewDialog'
        (By-Name $dialog 'Status').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        foreach ($field in @('DefaultActiveStatusUpdateInterval','DefaultIdleStatusUpdateInterval')) {
            (By-Id $dialog $field).GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(600)
        }
        (By-Name $dialog 'OK').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Test { $null -eq (By-Id $window 'SettingsViewDialog') } 'Geänderte Prüfintervalle werden gespeichert'
        'Isolierte Tastaturgegenprobe.' | Set-Content (Join-Path $fixtureA.WorkingCopy 'notification-keyboard-fixture')
        $calls = Join-Path $fixtureA.WorkingCopy 'notification-status-calls'
        Start-Sleep -Seconds 1
        $before = if (Test-Path $calls) { @(Get-Content $calls).Count } else { 0 }
        Start-Sleep -Seconds 1
        $withoutInput = if (Test-Path $calls) { @(Get-Content $calls).Count } else { 0 }
        Check ($withoutInput -eq $before) 'Ohne Eingabe läuft bei großem Intervall kein zusätzlicher Statusauftrag'
        $list = By-Id $window 'Repositories'
        $selected = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()[0]
        $selected.SetFocus(); [NativeShellInput]::Key(0x0d)
        Wait-Test { (Test-Path $calls) -and @(Get-Content $calls).Count -gt $before } 'Eingabe führt die konfigurierte Aktion Jetzt prüfen aus'
        $selected.SetFocus(); [NativeShellInput]::Key(0x2e)
        Wait-Test { $null -ne (By-Id $window 'TextBlockDialog') } 'Entf öffnet die vorhandene Entfernen-Bestätigung'
        (By-Name (By-Id $window 'TextBlockDialog') 'Abbrechen').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Check ((Get-Content $fixtureA.Configuration -Raw) -like ('*' + $fixtureA.WorkingCopy + '*')) 'Abbruch erhält den Repository-Eintrag'
        $selected.SetFocus(); [NativeShellInput]::Key(0x2d)
        $owner = [IntPtr]$window.Current.NativeWindowHandle
        Wait-Test { [NativeShellInput]::GetForegroundWindow() -ne $owner -and [NativeShellInput]::IsOwnedBy([NativeShellInput]::GetForegroundWindow(), $owner) } 'Einfg öffnet die native Ordnerauswahl mit richtigem Besitzer'
        [NativeShellInput]::Escape()
        } finally { [NativeShellInput]::Escape() }
    }
    Scenario 'Windows-Benachrichtigungszentrale und nativer Klick' {
        Start-Sleep -Seconds 7
        [NativeShellInput]::NotificationCenter()
        try {
            Wait-Test { $null -ne (Own-Notification 'PRP-Systemtest A') } 'Ältere eigene Meldung bleibt nach Bannerende in der Benachrichtigungszentrale' 10
            $notification = Own-Notification 'PRP-Systemtest A'
            Capture $notification 'notification-history-incoming-text'
            Click-OwnNotification $notification
            Wait-Test { -not (App-Window $applicationA).Current.IsOffscreen } 'Nativer Meldungsklick zeigt die ursprüngliche Instanz'
        } finally {
            # Ist die eigene Meldung noch sichtbar, blieb die Zentrale offen.
            [NativeShellInput]::Escape()
        }
    }
    Scenario 'Eigenes Tray-Symbol, Status und native Menübefehle' {
        try {
            if ($null -eq (Own-TrayIcon)) {
                $explorerIds = @(Get-Process explorer | ForEach-Object Id)
                foreach ($shell in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
                    if ($shell.Current.ProcessId -notin $explorerIds) { continue }
                    $chevron = By-Name $shell 'Ausgeblendete Symbole anzeigen'
                    if ($null -eq $chevron) { $chevron = By-Name $shell 'Show hidden icons' }
                    if ($null -ne $chevron) { $chevron.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
                }
            }
            Wait-Test { $null -ne (Own-TrayIcon) } 'Eigenes Tray-Symbol enthält Status und den richtigen Testpfad' 5
            $icon = Own-TrayIcon
            Capture $icon 'tray-own-status'
            [NativeShellInput]::PostMessage($windowHandles[$applicationA.Id], 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            $bounds = $icon.Current.BoundingRectangle
            [NativeShellInput]::RightClick([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
            $menu = $null
            Wait-Test {
                $script:ownTrayMenu = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
                    $_.Current.ProcessId -eq $applicationA.Id -and $null -ne (By-Name $_ 'Programm anzeigen')
                } | Select-Object -First 1
                $null -ne $script:ownTrayMenu
            } 'Rechtsklick öffnet das native Menü der eigenen Instanz' 5
            $menu = $script:ownTrayMenu
            Check ($null -ne (By-Name $menu 'Alle aktualisieren') -and $null -ne (By-Name $menu 'Beenden')) 'Alle vorhandenen Tray-Befehle bleiben erreichbar'
            (By-Name $menu 'Programm anzeigen').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Wait-Test { -not (App-Window $applicationA).Current.IsOffscreen } 'Tray-Befehl zeigt die ursprüngliche Instanz'
        } finally { [NativeShellInput]::Escape() }
    }
    Scenario 'Nicht stören und deaktivierte Windows-Benachrichtigungen mit Wiederherstellung' {
        $settingsWasRunning = @(Get-Process SystemSettings -ErrorAction SilentlyContinue).Count -gt 0
        [Diagnostics.Process]::Start((New-Object Diagnostics.ProcessStartInfo -Property @{ FileName='ms-settings:notifications'; UseShellExecute=$true })) | Out-Null
        $settingsProcess = $null
        try {
            Wait-Test {
                $script:notificationSettingsProcess = Get-Process SystemSettings -ErrorAction SilentlyContinue | Select-Object -First 1
                $null -ne $script:notificationSettingsProcess -and $null -ne (App-Window $script:notificationSettingsProcess)
            } 'Windows-Benachrichtigungseinstellungen sind geöffnet'
            $settingsProcess = $script:notificationSettingsProcess
            foreach ($mode in @('Nicht stören', 'Benachrichtigungen')) {
                $settingsWindow = App-Window $settingsProcess
                $toggle = By-Name $settingsWindow $mode
                Check ($null -ne $toggle) "Windows-Schalter ist eindeutig erreichbar: $mode"
                $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                $originalState = $pattern.Current.ToggleState
                $targetState = if ($mode -eq 'Nicht stören') { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
                try {
                    if ($originalState -ne $targetState) { $pattern.Toggle() }
                    Check ($pattern.Current.ToggleState -eq $targetState) "Windows-Testzustand ist gesetzt: $mode"
                    $completed = Join-Path $fixtureA.WorkingCopy 'notification-update-completed'
                    'Isolierter Statuswechsel.' | Set-Content -LiteralPath $completed
                    Force-StatusA
                    Wait-Test { $null -ne (By-Name (App-Window $applicationA) 'Synchronisiert') } 'Erledigte Änderung ist vor dem nächsten Meldungstest erkannt'
                    $before = @(Contexts $exe $fixtureA.Configuration).Count
                    [IO.File]::Delete($completed)
                    Force-StatusA
                    Wait-Test { @(Contexts $exe $fixtureA.Configuration).Count -gt $before } "Produkt verarbeitet neue Änderungen im Windows-Testzustand: $mode"
                    Start-Sleep -Seconds 2
                    Check ($null -eq (Own-Notification 'PRP-Systemtest A')) "Windows unterdrückt das Banner im Testzustand: $mode"
                    Check (-not $applicationA.HasExited) 'Der eigene Prüfer bleibt trotz unterdrückter Windows-Meldungen aktiv'
                } finally {
                    if ($pattern.Current.ToggleState -ne $originalState) { $pattern.Toggle() }
                    Check ($pattern.Current.ToggleState -eq $originalState) "Ursprünglicher Windows-Schalter ist wiederhergestellt: $mode"
                }
            }
        } finally {
            if (-not $settingsWasRunning -and $null -ne $settingsProcess -and -not $settingsProcess.HasExited) {
                [NativeShellInput]::PostMessage($settingsProcess.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            }
        }
    }
    Scenario 'Systemskalierung 100, 150 und 200 Prozent mit Wiederherstellung' {
        $settingsWasRunning = @(Get-Process SystemSettings -ErrorAction SilentlyContinue).Count -gt 0
        [Diagnostics.Process]::Start((New-Object Diagnostics.ProcessStartInfo -Property @{ FileName='ms-settings:display'; UseShellExecute=$true })) | Out-Null
        $settingsProcess = $null; $settingsWindow = $null; $originalScale = $null
        try {
            Wait-Test {
                $script:displaySettingsProcess = Get-Process SystemSettings -ErrorAction SilentlyContinue | Select-Object -First 1
                $null -ne $script:displaySettingsProcess -and $null -ne (App-Window $script:displaySettingsProcess)
            } 'Windows-Anzeigeeinstellungen sind geöffnet'
            $settingsProcess = $script:displaySettingsProcess; $settingsWindow = App-Window $settingsProcess
            Wait-Test { $null -ne (Display-ScaleCombo $settingsWindow) } 'Aktuelle Windows-Skalierung ist lesbar' 10
            $original = (Display-ScaleCombo $settingsWindow).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()[0].Current.Name
            $originalScale = [int]([regex]::Match($original, '\d+').Value)
            foreach ($scale in @(100,150,200)) { Select-DisplayScale $settingsWindow $scale; Capture (App-Window $applicationA) "product-dpi-$scale" }
        } finally {
            if ($null -ne $originalScale) { Select-DisplayScale (App-Window $settingsProcess) $originalScale }
            if (-not $settingsWasRunning -and $null -ne $settingsProcess -and -not $settingsProcess.HasExited) {
                [NativeShellInput]::PostMessage($settingsProcess.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            }
        }
    }
    Scenario 'Windows-Kontrastmodus mit Wiederherstellung' {
        $contrast = [NativeShellInput]::ReadContrast()
        try {
            [NativeShellInput]::SetContrast($contrast, $true)
            Check (([NativeShellInput]::ReadContrast().Flags -band 1) -eq 1) 'Windows-Kontrastmodus wurde aktiviert'
            Start-Sleep -Seconds 2
            Capture (App-Window $applicationA) 'product-high-contrast'
        } finally {
            [NativeShellInput]::RestoreContrast($contrast)
            Check (([NativeShellInput]::ReadContrast().Flags -band 1) -eq ($contrast.Flags -band 1)) 'Ursprünglicher Windows-Kontrastmodus ist wiederhergestellt'
        }
    }
    Scenario 'Native Aktivierung nach Prozessende bei weiterer laufender Konfiguration' {
        $before = @(Contexts $exe $fixtureA.Configuration).Count
        $completed = Join-Path $fixtureA.WorkingCopy 'notification-update-completed'
        Capture (App-Window $applicationA) 'before-native-restart'
        'Isolierter Statuswechsel.' | Set-Content -LiteralPath $completed
        (By-Id (App-Window $applicationA) 'Repositories').SetFocus(); [NativeShellInput]::Key(0x0d)
        Wait-Test { $null -ne (By-Name (App-Window $applicationA) 'Synchronisiert') } 'Statuswechsel entfernt die ausstehende Änderungsmeldung'
        [IO.File]::Delete($completed)
        (By-Id (App-Window $applicationA) 'Repositories').SetFocus(); [NativeShellInput]::Key(0x0d)
        Wait-Test { @(Contexts $exe $fixtureA.Configuration).Count -gt $before } 'Erneute Änderungen erzeugen eine neue Meldung'
        $applicationA.Kill(); $applicationA.WaitForExit()
        Start-Sleep -Seconds 7
        [NativeShellInput]::NotificationCenter()
        try {
            Wait-Test { $null -ne (Own-Notification 'PRP-Systemtest A') } 'Eigene Meldung bleibt nach Prozessende erreichbar' 10
            Click-OwnNotification (Own-Notification 'PRP-Systemtest A')
            Wait-Test {
                $script:restartedA = @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object {
                    -not $_.HasExited -and $_.Id -ne $applicationB.Id -and $null -ne $_.MainModule -and $_.MainModule.FileName -eq $exe
                }) | Select-Object -First 1
                $null -ne $script:restartedA -and $null -ne (By-Id (App-Window $script:restartedA) 'Repositories')
            } 'Windows-Aktivierung stellt die ursprüngliche Konfiguration wieder her'
            $processes.Add($script:restartedA)
            Wait-Test { @(Contexts $exe $fixtureA.Configuration | Where-Object ProcessId -eq $script:restartedA.Id).Count -gt 0 } 'Der neue Prüfer verwendet nachweislich die ursprüngliche INI-Datei'
            Capture (App-Window $script:restartedA) 'native-restart-original-config'
        } finally { [NativeShellInput]::Escape() }
    }
    foreach ($runtime in @('win-x64', 'win-x86')) {
        Scenario "Installeraustausch, Neustart und Gegenproben $runtime" {
            & (Join-Path $PSScriptRoot 'Test-ApplicationUpdater.ps1') -RuntimeIdentifier $runtime -ArtifactsDirectory $artifacts | Out-Host
        }
        Scenario "WinForms-Ausgangspaket auf WinUI aktualisieren $runtime" {
            $fixture = Make-Fixture ('Baseline-' + $runtime) $false $false
            $baseline = Join-Path (Split-Path $artifacts -Parent) "baseline-results/packages/SVNNotifier-3.0.0.13-$runtime.zip"
            Expand-Archive -LiteralPath $baseline -DestinationPath $fixture.Directory
            $target = Join-Path $fixture.Directory 'SVN_Notifier.exe'
            $original = Start-Product $target $fixture.Directory
            Wait-Test { $null -ne (By-Id (App-Window $original) 'ItemListView') } 'Tatsächliches WinForms-Ausgangspaket läuft'
            $payload = Join-Path $artifacts "publish/$runtime/SVN_Notifier.exe"
            $stage = Join-Path $fixture.Directory ('.svnnotifier-update-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $stage | Out-Null
            Copy-Item -LiteralPath $payload -Destination (Join-Path $stage 'payload.exe')
            $helper = Join-Path $stage 'installer.exe'
            Copy-Item -LiteralPath $target -Destination $helper
            $readyName = 'Local\SVNNotifier.Update.' + [Guid]::NewGuid().ToString('N')
            $ready = New-Object Threading.EventWaitHandle($false, [Threading.EventResetMode]::ManualReset, $readyName)
            try {
                $plan = @{ TargetPath=$target; WorkingDirectory=$fixture.Directory; ParentId=$original.Id;
                    ParentStartTicks=$original.StartTime.ToUniversalTime().Ticks; TargetHash=(Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant();
                    PayloadHash=(Get-FileHash $payload -Algorithm SHA256).Hash.ToLowerInvariant(); Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($payload).FileVersion;
                    Runtime=$runtime; ReadyEvent=$readyName; Language=1 }
                $planPath = Join-Path $stage 'update.json'
                $plan | ConvertTo-Json | Set-Content -LiteralPath $planPath -Encoding UTF8
                $installer = Start-Product $helper $stage ('--apply-update "' + $planPath + '"')
                Check ($ready.WaitOne(15000)) 'Installer des Ausgangspakets bestätigt die Übergabe'
                $fileMenu = By-Name (App-Window $original) 'Datei'
                $fileMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
                Start-Sleep -Milliseconds 200
                (By-Name (App-Window $original) 'Beenden').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Check ($original.WaitForExit(15000)) 'WinForms-Anwendung beendet sich über ihren vorhandenen Beenden-Befehl'
                $configurationHash = (Get-FileHash $fixture.Configuration -Algorithm SHA256).Hash
                Wait-Test {
                    $script:migrated = @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object {
                        -not $_.HasExited -and $null -ne $_.MainModule -and $_.MainModule.FileName -eq $target
                    }) | Select-Object -First 1
                    $null -ne $script:migrated -and $null -ne (By-Id (App-Window $script:migrated) 'Repositories')
                } 'WinUI startet nach tatsächlichem EXE-Austausch' 45
                $processes.Add($script:migrated)
                Check ((Get-FileHash $target -Algorithm SHA256).Hash -eq (Get-FileHash $payload -Algorithm SHA256).Hash) 'Installiert ist die gebaute WinUI-EXE'
                Check ((Get-FileHash $fixture.Configuration -Algorithm SHA256).Hash -eq $configurationHash) 'Die vom Ausgangspaket gespeicherte INI bleibt unverändert'
                Check ((Get-Content -LiteralPath $fixture.Configuration -Raw) -like ('*' + $fixture.WorkingCopy + '*')) 'Die eigene Repository-Zuordnung bleibt erhalten'
                Capture (App-Window $script:migrated) "winforms-to-winui-$runtime"
                Wait-Test { -not (Test-Path -LiteralPath $stage) } 'Hilfsprozess und Staging werden nach Neustart entfernt'
            } finally { $ready.Dispose() }
        }
        Scenario "Produktiver Installerstart ohne WinUI-Initialisierung $runtime" {
            $directory = Join-Path $run ('helper-' + $runtime)
            New-Item -ItemType Directory -Path $directory | Out-Null
            $invalid = Join-Path $directory 'invalid-plan.json'
            '{}' | Set-Content -LiteralPath $invalid -Encoding ascii
            $helper = Start-Product (Join-Path $artifacts "publish/$runtime/SVN_Notifier.exe") $directory ('--apply-update "' + $invalid + '"')
            Wait-Test { $null -ne (App-Window $helper) } 'Produktiver Hilfsmodus meldet die ungültige Übergabe'
            Check (@($helper.Modules | Where-Object ModuleName -in @('Microsoft.UI.Xaml.dll','Microsoft.WindowsAppRuntime.dll')).Count -eq 0) 'Hilfsmodus lädt weder XAML noch die Windows-App-Runtime'
            $buttons = (App-Window $helper).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
            Check ($buttons.Count -eq 1) 'Ungültige Installerübergabe zeigt eine konkrete Fehlermeldung'
            $buttons[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Check ($helper.WaitForExit(10000) -and $helper.ExitCode -eq 1) 'Ungültiger Hilfsstart endet mit Fehlercode'
        }
    }
} finally {
    foreach ($process in $processes) {
        try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } } finally { $process.Dispose() }
    }
    Write-Host "Sichtbare Testsitzung endet: $([DateTime]::UtcNow.ToString('o'))"
    $failures | Set-Content -LiteralPath (Join-Path $run 'failures.txt') -Encoding UTF8
    Stop-Transcript | Out-Null
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
