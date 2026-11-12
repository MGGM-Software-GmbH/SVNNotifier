param(
    [Parameter(Mandatory)] [string] $ArtifactsDirectory,
    [Parameter(Mandatory)] [switch] $UseUserDesktop,
    [Parameter(Mandatory)] [DateTimeOffset] $VisibleFrom,
    [Parameter(Mandatory)] [DateTimeOffset] $VisibleUntil,
    [ValidateSet('win-x64', 'win-x86')] [string] $RuntimeIdentifier = 'win-x64',
    [switch] $IncludeInstallerChecks,
    [switch] $Diagnostics,
    [switch] $Quick,
    [string] $BaselinePackagesDirectory = ''
)

# Dieser Lauf benötigt eine ausdrücklich erlaubte sichtbare Windows-Testsitzung.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $UseUserDesktop) { throw 'Für diesen Test ist die Freigabe einer sichtbaren Testsitzung erforderlich.' }
$minimumMinutes = if ($Quick) { 2 } else { 5 }
if ([DateTimeOffset]::Now -lt $VisibleFrom -or [DateTimeOffset]::Now -ge $VisibleUntil.AddMinutes(-$minimumMinutes)) {
    throw 'Der sichtbare Lauf liegt außerhalb des freigegebenen Zeitfensters oder hat nicht genug Zeit für Lauf und Bereinigung.'
}
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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError=true)] public static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maximum);
    public static uint WindowThread(IntPtr window) { uint process; return GetWindowThreadProcessId(window, out process); }
    public static string WindowClass(IntPtr window) { var name = new System.Text.StringBuilder(256); GetClassName(window, name, name.Capacity); return name.ToString(); }
    public static uint WindowProcess(IntPtr window) { uint process; GetWindowThreadProcessId(window, out process); return process; }
    [StructLayout(LayoutKind.Sequential)] public struct TrayRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct TrayIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref TrayIdentifier identifier, out TrayRectangle rectangle);
    public static bool TryGetTrayBounds(IntPtr window, uint id, out TrayRectangle rectangle) {
        var identifier = new TrayIdentifier { Size = (uint)Marshal.SizeOf(typeof(TrayIdentifier)), Window = window, Id = id, Guid = Guid.Empty };
        return Shell_NotifyIconGetRect(ref identifier, out rectangle) == 0;
    }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    private static void EnsureInteractiveSession() {
        uint processId = WindowProcess(GetForegroundWindow());
        using (var process = System.Diagnostics.Process.GetProcessById((int)processId))
            if (process.ProcessName == "LockApp") throw new InvalidOperationException("Windows-Sitzung ist gesperrt; keine Testeingabe gesendet.");
    }
    [StructLayout(LayoutKind.Sequential)] public struct CursorPoint { public int X, Y; }
    public static void Click(int x, int y) {
        EnsureInteractiveSession();
        CursorPoint saved; GetCursorPos(out saved); SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(saved.X, saved.Y);
    }
    public static void RightClick(int x, int y) {
        EnsureInteractiveSession();
        CursorPoint saved; GetCursorPos(out saved); SetCursorPos(x, y);
        mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(saved.X, saved.Y);
    }
    public static void Drag(int fromX, int fromY, int toX, int toY) {
        EnsureInteractiveSession();
        CursorPoint saved; GetCursorPos(out saved); SetCursorPos(fromX, fromY);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        try {
            System.Threading.Thread.Sleep(150);
            for (int step = 1; step <= 20; step++) {
                EnsureInteractiveSession();
                SetCursorPos(fromX + (toX - fromX) * step / 20, fromY + (toY - fromY) * step / 20);
                System.Threading.Thread.Sleep(25);
            }
        } finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos(saved.X, saved.Y); }
    }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void NotificationCenter() {
        EnsureInteractiveSession();
        keybd_event(0x5b, 0, 0, UIntPtr.Zero); keybd_event(0x4e, 0, 0, UIntPtr.Zero);
        keybd_event(0x4e, 0, 2, UIntPtr.Zero); keybd_event(0x5b, 0, 2, UIntPtr.Zero);
    }
    public static void Escape() { Key(0x1b); }
    public static void Key(byte key) { EnsureInteractiveSession(); keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
    public static void AltKey(byte key) { EnsureInteractiveSession(); keybd_event(0x12, 0, 0, UIntPtr.Zero); try { Key(key); } finally { keybd_event(0x12, 0, 2, UIntPtr.Zero); } }
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    public static bool IsOwnedBy(IntPtr window, IntPtr owner) { return GetAncestor(window, 3) == owner; }
}
'@
$foregroundProcess = Get-Process -Id ([NativeShellInput]::WindowProcess([NativeShellInput]::GetForegroundWindow())) -ErrorAction SilentlyContinue
if ($null -ne $foregroundProcess -and $foregroundProcess.ProcessName -eq 'LockApp') {
    throw 'Die Windows-Sitzung ist gesperrt. Sichtbare Tests benötigen eine vom Benutzer entsperrte Sitzung.'
}
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

// Diese isolierte OLE-Quelle gehört ausschließlich zum Test, nicht zur Produktoberfläche.
public sealed class NativeDragSource : IDisposable {
    private Form form;
    private readonly Thread thread;
    private readonly ManualResetEvent ready = new ManualResetEvent(false);
    private Exception failure;
    public Point SourcePoint;
    public DragDropEffects Result;
    public NativeDragSource(string[] files) {
        thread = new Thread(() => {
            try {
                form = new Form { Text = "SVN Notifier Drag-Test", Width = 260, Height = 120,
                    ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(10, 120) };
                var source = new Label { Text = files.Length > 0 ? "Isolierte Dateien und Ordner" : "Ungeeigneter Text",
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                source.MouseDown += (sender, args) => {
                    var data = new DataObject();
                    if (files.Length > 0) data.SetData(DataFormats.FileDrop, files);
                    else data.SetText("Isolierter Text, kein Dateisystemziel.");
                    Result = source.DoDragDrop(data, DragDropEffects.Copy);
                };
                form.Controls.Add(source);
                form.Shown += (sender, args) => { SourcePoint = source.PointToScreen(new Point(100, 40)); ready.Set(); };
                Application.Run(form);
            } catch (Exception exception) { failure = exception; ready.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        if (!ready.WaitOne(5000)) throw new TimeoutException("OLE-Testquelle konnte nicht geöffnet werden.");
        if (failure != null) throw failure;
    }
    public void Dispose() {
        if (form != null && !form.IsDisposed) form.BeginInvoke((Action)(() => form.Close()));
        if (!thread.Join(5000)) throw new TimeoutException("OLE-Testquelle wurde nicht beendet.");
        ready.Dispose();
    }
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
    do {
        if ([DateTimeOffset]::Now -ge $VisibleUntil.AddSeconds(-30)) { throw 'Das sichtbare Zeitfenster endet; Testlauf wird bereinigt.' }
        $foregroundProcess = Get-Process -Id ([NativeShellInput]::WindowProcess([NativeShellInput]::GetForegroundWindow())) -ErrorAction SilentlyContinue
        if ($null -ne $foregroundProcess -and $foregroundProcess.ProcessName -eq 'LockApp') { throw 'Die Windows-Sitzung wurde gesperrt; Testeingaben werden abgebrochen.' }
        if (& $condition) { Write-Host "OK: $message"; return }; Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $message
}
function Scenario([string] $name, [scriptblock] $test) {
    # Ein kurzer Desktoplauf verändert keine systemweiten Anzeige- oder Benachrichtigungseinstellungen.
    if ($Quick -and $name -in @('Nicht stören und deaktivierte Windows-Benachrichtigungen mit Wiederherstellung', 'Systemskalierung 100, 150 und 200 Prozent mit Wiederherstellung', 'Windows-Kontrastmodus mit Wiederherstellung')) {
        Write-Host "AUSGELASSEN im kurzen Lauf: $name"
        return
    }
    if ([DateTimeOffset]::Now -ge $VisibleUntil.AddMinutes(-2)) { throw 'Kein weiterer Test vor Ende des sichtbaren Zeitfensters; Testlauf wird bereinigt.' }
    $foregroundProcess = Get-Process -Id ([NativeShellInput]::WindowProcess([NativeShellInput]::GetForegroundWindow())) -ErrorAction SilentlyContinue
    if ($null -ne $foregroundProcess -and $foregroundProcess.ProcessName -eq 'LockApp') { throw 'Die Windows-Sitzung wurde gesperrt; eigene Testinstanzen werden beendet.' }
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
function Stop-OwnProduct($process) {
    if ($process.HasExited) { return }
    $window = App-Window $process
    if ($null -ne $window) {
        $handle = [IntPtr]$window.Current.NativeWindowHandle
        # Die Sitzungsnachrichten betreffen ausschließlich das eigene HWND und erlauben dessen reguläres Schließen.
        [NativeShellInput]::PostMessage($handle, 0x11, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        [NativeShellInput]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if ($process.WaitForExit(10000)) { Write-Host "Eigene Testinstanz regulär beendet: PID=$($process.Id)"; return }
    }
    $failures.Add("Eigene Testinstanz benötigte eine erzwungene Bereinigung: PID=$($process.Id)")
    $process.Kill()
    Check ($process.WaitForExit(10000)) 'Eigene Testinstanz ist nach erzwungener Bereinigung beendet'
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
        if ($windowHandles.ContainsKey($element.Current.ProcessId) -and $windowHandles[$element.Current.ProcessId] -eq $window) {
            # Ein per Escape ausgeblendetes WinUI-Fenster über seinen eigenen Anzeigen-Befehl wiederherstellen.
            [NativeShellInput]::PostMessage($window, 0x8001, [IntPtr]::Zero, [IntPtr]0x202) | Out-Null
            Start-Sleep -Milliseconds 150
        }
        [NativeShellInput]::ShowWindow($window, 9) | Out-Null
        # Alt gibt während der erlaubten sichtbaren Sitzung die Windows-Vordergrundsperre frei.
        [NativeShellInput]::Key(0x12)
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
    # HWND und Symbol-ID identifizieren ausschließlich das eigene Symbol, auch bei abgeschnittenem Tooltip.
    $bounds = New-Object NativeShellInput+TrayRectangle
    if (-not [NativeShellInput]::TryGetTrayBounds($windowHandles[$applicationA.Id], 1, [ref]$bounds)) { return $null }
    $centerX = ($bounds.Left + $bounds.Right) / 2
    $centerY = ($bounds.Top + $bounds.Bottom) / 2
    $shellIds = @(Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object Id)
    foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($window.Current.ProcessId -notin $shellIds) { continue }
        foreach ($element in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $candidate = $element.Current.BoundingRectangle
            if (-not $element.Current.IsOffscreen -and $element.Current.Name.Contains('SVN Notifier') -and
                $candidate.Width -gt 0 -and $candidate.Height -gt 0 -and
                $candidate.Left -le $centerX -and $candidate.Right -ge $centerX -and $candidate.Top -le $centerY -and $candidate.Bottom -ge $centerY) { return $element }
        }
    }
    return $null
}
function Display-ScaleCombo($settings) {
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    foreach ($combo in $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)) {
        $selection = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
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
    $selected = (Display-ScaleCombo $settings).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
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
    $driver = Join-Path $artifacts "svn-driver/$RuntimeIdentifier/Svn.Tests.exe"
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
    $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].SetFocus()
    [NativeShellInput]::Key(0x0d)
}
try {
    $productDirectory = Join-Path $run 'product'
    New-Item -ItemType Directory -Path $productDirectory | Out-Null
    $exe = Join-Path $productDirectory 'SVN_Notifier.exe'
    Copy-Item -LiteralPath (Join-Path $artifacts "publish/$RuntimeIdentifier/SVN_Notifier.exe") -Destination $exe
    $fixtureA = Make-Fixture 'A' $false $false
    $fixtureB = Make-Fixture 'B' $true $true
    $applicationA = Start-Product $exe $fixtureA.Directory
    if ($Diagnostics) {
        Wait-Test { $null -ne (By-Id (App-Window $applicationA) 'Repositories') } 'Diagnoseinstanz zeigt ihre Repository-Liste'
        Wait-Test { @(Contexts $exe $fixtureA.Configuration).Count -gt 0 } 'Diagnoseinstanz hat eine Meldung erzeugt'
        $handle = [IntPtr](App-Window $applicationA).Current.NativeWindowHandle
        foreach ($attempt in 1..3) {
            $foreground = [NativeShellInput]::GetForegroundWindow()
            Write-Host "Vordergrunddiagnose: own=$handle; foreground=$foreground; foregroundPID=$([NativeShellInput]::WindowProcess($foreground)); foregroundClass=$([NativeShellInput]::WindowClass($foreground)); ownThread=$([NativeShellInput]::WindowThread($handle)); inputThread=$([NativeShellInput]::WindowThread($foreground))"
            [NativeShellInput]::ShowWindow($handle, 9) | Out-Null
            $attached = [NativeShellInput]::AttachThreadInput([NativeShellInput]::GetCurrentThreadId(), [NativeShellInput]::WindowThread($foreground), $true)
            try { Write-Host "SetForegroundWindow=$([NativeShellInput]::SetForegroundWindow($handle)); AttachThreadInput=$attached" }
            finally { if ($attached) { [NativeShellInput]::AttachThreadInput([NativeShellInput]::GetCurrentThreadId(), [NativeShellInput]::WindowThread($foreground), $false) | Out-Null } }
            Start-Sleep -Milliseconds 500
        }
        foreach ($center in @($false,$true)) {
            if ($center) { [NativeShellInput]::NotificationCenter(); Start-Sleep -Seconds 1 }
            foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
                $process = Get-Process -Id $window.Current.ProcessId -ErrorAction SilentlyContinue
                if ($null -eq $process -or $process.ProcessName -notin @('explorer','ShellHost','ShellExperienceHost','StartMenuExperienceHost','SVN_Notifier')) { continue }
                Write-Host "Shellstruktur: center=$center; process=$($process.ProcessName); class=$($window.Current.ClassName); id=$($window.Current.AutomationId); type=$($window.Current.ControlType.ProgrammaticName); offscreen=$($window.Current.IsOffscreen)"
                foreach ($element in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                    if ($element.Current.Name.Contains('PRP-Systemtest A')) { Write-Host "Eigener Meldungstreffer: type=$($element.Current.ControlType.ProgrammaticName); class=$($element.Current.ClassName); offscreen=$($element.Current.IsOffscreen); name=$($element.Current.Name)" }
                }
            }
        }
        [NativeShellInput]::Escape()
        return
    }
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
        $selection = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
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
        $selected = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0]
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
    Scenario 'Echtes Drag-and-drop gemischter Ziele und Ablehnung von Text' {
        $window = App-Window $applicationA
        Capture $window 'before-drag-drop'
        $directory = Join-Path $run 'Drag Ordner ä'
        $file = Join-Path $run 'Drag Datei ß.txt'
        New-Item -ItemType Directory -Path $directory | Out-Null
        'Isolierte Drag-and-drop-Fixture.' | Set-Content -LiteralPath $file
        $list = By-Id $window 'Repositories'
        $bounds = $list.Current.BoundingRectangle
        $source = New-Object NativeDragSource -ArgumentList (,[string[]]@($directory, $file))
        try {
            [NativeShellInput]::Drag($source.SourcePoint.X, $source.SourcePoint.Y, [int]($bounds.X + $bounds.Width * 0.7), [int]($bounds.Y + $bounds.Height * 0.7))
            Wait-Test {
                $content = Get-Content -LiteralPath $fixtureA.Configuration -Raw
                $content.Contains($directory) -and $content.Contains($file)
            } 'Echter OLE-Drop übernimmt sowohl Ordner als auch Datei in die gespeicherte Repository-Liste'
            Check ($source.Result -eq [System.Windows.Forms.DragDropEffects]::Copy) 'Dragquelle erhält die tatsächlich akzeptierte Kopieroperation'
        } finally { $source.Dispose() }
        $before = (Get-FileHash -LiteralPath $fixtureA.Configuration -Algorithm SHA256).Hash
        $source = New-Object NativeDragSource -ArgumentList (,[string[]]@())
        try {
            [NativeShellInput]::Drag($source.SourcePoint.X, $source.SourcePoint.Y, [int]($bounds.X + $bounds.Width * 0.7), [int]($bounds.Y + $bounds.Height * 0.7))
            Check ($source.Result -eq [System.Windows.Forms.DragDropEffects]::None) 'Ungeeigneter Text-Drop wird als Operation abgewiesen'
            Check ((Get-FileHash -LiteralPath $fixtureA.Configuration -Algorithm SHA256).Hash -eq $before) 'Abgewiesener Text verändert die Repository-Konfiguration nicht'
        } finally { $source.Dispose() }
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
            Wait-Test { $null -ne (Own-TrayIcon) } 'Eigenes Tray-Symbol ist dem eigenen Fenster und seiner Symbol-ID zugeordnet' 5
            $icon = Own-TrayIcon
            Check ($icon.Current.Name.Contains('Geändert')) 'Eigenes Tray-Symbol zeigt den aktuellen Änderungsstatus'
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
            $original = (Display-ScaleCombo $settingsWindow).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name
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
        Force-StatusA
        Wait-Test { $null -ne (By-Name (App-Window $applicationA) 'Synchronisiert') } 'Statuswechsel entfernt die ausstehende Änderungsmeldung'
        [IO.File]::Delete($completed)
        Force-StatusA
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
    Scenario 'Abgebrochene und bestätigte Sitzungsbeendigung am eigenen Testfenster' {
        $fixture = Make-Fixture 'Session' $false $false
        $session = Start-Product $exe $fixture.Directory
        Wait-Test { $null -ne (By-Id (App-Window $session) 'Repositories') } 'Eigenes Fenster für die abgegrenzte Sitzungssimulation ist geöffnet'
        $handle = [IntPtr](App-Window $session).Current.NativeWindowHandle
        # Nur das eigene HWND erhält Nachrichten; keine Windows-Abmeldung und kein Broadcast.
        [NativeShellInput]::PostMessage($handle, 0x11, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        [NativeShellInput]::PostMessage($handle, 0x16, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        [NativeShellInput]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Wait-Test { -not $session.HasExited -and -not [NativeShellInput]::IsWindowVisible($handle) } 'Abgebrochene Sitzungsbeendigung stellt das gewöhnliche Ausblenden wieder her'
        [NativeShellInput]::ShowWindow($handle, 9) | Out-Null
        [NativeShellInput]::PostMessage($handle, 0x11, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        [NativeShellInput]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Check ($session.WaitForExit(15000) -and $session.ExitCode -eq 0) 'Bei laufender Sitzungsbeendigung darf das eigene Fenster den Prozess regulär beenden'
    }
    Scenario 'Neuer portabler Pfad und native Aktivierung nach Ordnerwechsel' {
        $fixture = Make-Fixture 'Portable' $false $false
        $origin = Join-Path $run 'portable-origin'
        $destination = Join-Path $run 'portable-moved'
        New-Item -ItemType Directory -Path $origin | Out-Null
        $oldExe = Join-Path $origin 'SVN_Notifier.exe'
        Copy-Item -LiteralPath $exe -Destination $oldExe
        Copy-Item -LiteralPath $fixture.Configuration -Destination (Join-Path $origin 'SVN_Notifier.ini')
        $old = Start-Product $oldExe $origin
        Wait-Test { @(Contexts $oldExe (Join-Path $origin 'SVN_Notifier.ini') | Where-Object Kind -eq 0).Count -gt 0 } 'Regulärer Start erzeugt einen Kontext am ursprünglichen portablen Ort'
        $old.Kill(); Check ($old.WaitForExit(10000)) 'Ursprüngliche portable Testinstanz ist vor dem Verschieben beendet'
        [IO.Directory]::Move($origin, $destination)
        $movedExe = Join-Path $destination 'SVN_Notifier.exe'
        $movedConfig = Join-Path $destination 'SVN_Notifier.ini'
        $moved = Start-Product $movedExe $destination
        Wait-Test { @(Contexts $movedExe $movedConfig | Where-Object Kind -eq 0).Count -gt 0 } 'Regulärer Start am neuen Ort erzeugt eine Meldung mit neuer EXE und portabler INI'
        Check (-not (Test-Path -LiteralPath $oldExe)) 'Der alte EXE-Pfad ist nach dem Verschieben ungültig'
        Wait-Test { $null -ne (Own-Notification 'PRP-Systemtest Portable') } 'Neue Meldung wird nach dem Ordnerwechsel als Windows-Banner angezeigt' 10
        $moved.Kill(); Check ($moved.WaitForExit(10000)) 'Verschobene Testinstanz ist vor dem nativen Meldungsklick beendet'
        Click-OwnNotification (Own-Notification 'PRP-Systemtest Portable')
        Wait-Test {
            $script:movedActivation = @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object {
                -not $_.HasExited -and $_.MainModule.FileName -eq $movedExe
            }) | Select-Object -First 1
            $null -ne $script:movedActivation -and $null -ne (By-Id (App-Window $script:movedActivation) 'Repositories')
        } 'Neue Windows-Meldung startet nach Ordnerwechsel die EXE am neuen Ort'
        $processes.Add($script:movedActivation)
        Wait-Test { @(Contexts $movedExe $movedConfig | Where-Object ProcessId -eq $script:movedActivation.Id).Count -gt 0 } 'Aktivierter neuer Prüfer verwendet nachweislich den neuen portablen Konfigurationskontext'
    }
    if ($IncludeInstallerChecks) {
    if (-not $BaselinePackagesDirectory) { throw 'Für sichtbare Alt-zu-Neu-Prüfungen ist ein ausdrücklich geprüftes Baseline-Paketverzeichnis erforderlich.' }
    foreach ($runtime in @('win-x64', 'win-x86')) {
        Scenario "Installeraustausch, Neustart und Gegenproben $runtime" {
            & (Join-Path $PSScriptRoot 'Test-ApplicationUpdater.ps1') -RuntimeIdentifier $runtime -ArtifactsDirectory $artifacts | Out-Host
        }
        Scenario "WinForms-Ausgangspaket auf WinUI aktualisieren $runtime" {
            $fixture = Make-Fixture ('Baseline-' + $runtime) $false $false
            $baseline = Join-Path $BaselinePackagesDirectory "SVNNotifier-3.0.0.13-$runtime.zip"
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
                    ParentStartTicks=$original.StartTime.ToUniversalTime().Ticks; TargetHash=(Get-FileHash $target -Algorithm SHA256).Hash;
                    PayloadHash=(Get-FileHash $payload -Algorithm SHA256).Hash; Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($payload).FileVersion;
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
            $confirm = By-Name (App-Window $helper) 'OK'
            Check ($null -ne $confirm) 'Ungültige Installerübergabe zeigt eine konkrete Fehlermeldung'
            $confirm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Check ($helper.WaitForExit(10000) -and $helper.ExitCode -eq 1) 'Ungültiger Hilfsstart endet mit Fehlercode'
        }
    }
    }
} finally {
    # Auch ein durch Windows gestarteter Aktivierungsprozess gehört zum Lauf, falls seine Erkennung zuvor scheiterte.
    $ownedRoot = [IO.Path]::GetFullPath($run).TrimEnd('\') + '\'
    foreach ($candidate in @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue)) {
        try {
            if (-not $candidate.HasExited -and $candidate.MainModule.FileName.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-OwnProduct $candidate
            }
        } catch {
            $failures.Add("Prozessprüfung oder Bereinigung fehlgeschlagen: PID=$($candidate.Id); $($_.Exception.Message)")
        } finally { $candidate.Dispose() }
    }
    foreach ($process in $processes) {
        try { if (-not $process.HasExited) { Stop-OwnProduct $process } }
        catch { $failures.Add("Eigene Instanz konnte nicht bereinigt werden: $($_.Exception.Message)") }
        finally { $process.Dispose() }
    }
    $remaining = @(Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited -and $_.MainModule.FileName.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase) })
    Check ($remaining.Count -eq 0) 'Nach dem Testlauf ist keine eigene Notifier-Instanz mehr aktiv'
    Write-Host "Sichtbare Testsitzung endet: $([DateTime]::UtcNow.ToString('o'))"
    $failures | Set-Content -LiteralPath (Join-Path $run 'failures.txt') -Encoding UTF8
    Stop-Transcript | Out-Null
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
