param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-x86')] [string] $RuntimeIdentifier,
    [Parameter(Mandatory)] [string] $ArtifactsDirectory
)

# Startet nur eigene Paketkopien. Die fehlenden Testclients führen vor der Tray-Registrierung in die Einstellungen.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-OwnProcessWindows.ps1')
if (-not ('PackagedRuntimePath' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class PackagedRuntimePath {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct ModuleEntry {
        public uint Size, ModuleId, ProcessId, GlobalCount, ProcessCount;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string Path;
    }
    public sealed class LoadedModule { public string ModuleName; public string FileName; }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool Module32FirstW(IntPtr snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool Module32NextW(IntPtr snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static LoadedModule[] Modules(int processId) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true) {
            try { return Snapshot(processId); }
            catch (System.ComponentModel.Win32Exception exception) {
                // Microsoft verlangt bei ERROR_BAD_LENGTH einen neuen Snapshot während laufender DLL-Ladevorgänge.
                if (exception.NativeErrorCode != 24 || DateTime.UtcNow >= deadline) throw;
                System.Threading.Thread.Sleep(10);
            }
        }
    }
    private static LoadedModule[] Snapshot(int processId) {
        IntPtr snapshot = CreateToolhelp32Snapshot(0x18, (uint)processId);
        if (snapshot == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            var modules = new System.Collections.Generic.List<LoadedModule>();
            var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(ModuleEntry)) };
            if (!Module32FirstW(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            do { modules.Add(new LoadedModule { ModuleName=entry.Name, FileName=entry.Path }); } while (Module32NextW(snapshot, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return modules.ToArray();
        } finally { CloseHandle(snapshot); }
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetLongPathName(string path, StringBuilder result, uint length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    public static void CloseOwnDialog(int processId, IntPtr window) {
        window = GetAncestor(window, 2);
        uint owner; GetWindowThreadProcessId(window, out owner);
        if (window == IntPtr.Zero || owner != processId) throw new InvalidOperationException("Der Fehlerdialog gehört nicht zum eigenen Testprozess.");
        if (!PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static string Expand(string path) {
        var result = new StringBuilder(32768);
        uint length = GetLongPathName(path, result, (uint)result.Capacity);
        if (length == 0 || length >= result.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return result.ToString();
    }
}
'@
}
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$directory = Join-Path $artifacts ('start-modes/' + $RuntimeIdentifier + '/' + [Guid]::NewGuid().ToString('N'))
$invocation = Join-Path $directory 'invocation'
New-Item -ItemType Directory -Path $invocation | Out-Null
$executable = Join-Path $directory 'SVN_Notifier.exe'
Copy-Item -LiteralPath (Join-Path $artifacts "publish/$RuntimeIdentifier/SVN_Notifier.exe") -Destination $executable
$client = Join-Path $directory 'missing-origin-client.exe'
@('[Settings]', 'Version=1.9', 'Language=0', 'CheckForNewVersion=false', "SVN_path=$client", "TortoiseSVN_path=$client", '[Folders]') |
    Set-Content -LiteralPath (Join-Path $directory 'SVN_Notifier.ini') -Encoding UTF8
@('[Settings]', 'Version=1.9', 'Language=0', 'SVN_path=wrong-invocation-client.exe', 'TortoiseSVN_path=wrong-invocation-client.exe') |
    Set-Content -LiteralPath (Join-Path $invocation 'SVN_Notifier.ini') -Encoding UTF8
$processes = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
function Start-Mode([string] $arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $executable; $info.WorkingDirectory = $invocation; $info.Arguments = $arguments
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($info); $processes.Add($process); return $process
}
function Find-Control($process, [string] $property, [string] $value) {
    $searchProperty = if ($property -eq 'Name') { [System.Windows.Automation.AutomationElement]::NameProperty } else { [System.Windows.Automation.AutomationElement]::AutomationIdProperty }
    $search = New-Object System.Windows.Automation.PropertyCondition($searchProperty, $value)
    foreach ($window in @(Get-OwnProcessWindows $process.Id)) {
        $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $search)
        if ($null -ne $found) { return $found }
    }
    return $null
}
function Wait-Control([scriptblock] $condition, [string] $message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { if (& $condition) { Write-Host "OK: $message"; return }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    throw $message
}
try {
    $launcher = Start-Mode 'start'
    if (-not $launcher.WaitForExit(10000) -or $launcher.ExitCode -ne 0) { throw 'Startmodus hat keinen Kindprozess gestartet.' }
    Wait-Control {
        $script:child = Get-Process SVN_Notifier -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited -and $null -ne $_.MainModule -and $_.MainModule.FileName -eq $executable } | Select-Object -First 1
        $null -ne $script:child -and $null -ne (Find-Control $script:child 'Id' 'SettingsViewDialog')
    } 'Startmodus öffnet den tatsächlichen WinUI-Konfigurationsdialog'
    $processes.Add($script:child)
    $value = (Find-Control $script:child 'Id' 'SVNpath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($value -ne $client) { throw 'Startmodus verwendet nicht den Programmordner als Arbeitsverzeichnis.' }
    Write-Host 'OK: Startmodus erhält die vorgesehene Konfigurationszuordnung trotz anderem Aufrufordner'
    $script:child.Refresh()
    $modules = [PackagedRuntimePath]::Modules($script:child.Id)
    @($modules | ForEach-Object { $_.ModuleName + ': ' + $_.FileName }) | Set-Content -LiteralPath (Join-Path $directory 'loaded-modules.txt')
    $extraction = @()
    $extractionRoot = [PackagedRuntimePath]::Expand($env:TEMP).TrimEnd('\') + '\.net\SVN_Notifier\'
    foreach ($name in @('System.Private.CoreLib.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        $module = $modules | Where-Object ModuleName -eq $name | Select-Object -First 1
        if ($null -eq $module) { throw "Erforderliches Laufzeitmodul fehlt: $name" }
        if (-not [PackagedRuntimePath]::Expand($module.FileName).StartsWith($extractionRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Laufzeitmodul stammt nicht aus der Einzel-EXE-Extraktion: $name" }
        $extraction += $module.FileName
    }
    $extraction | Set-Content -LiteralPath (Join-Path $directory 'runtime-modules.txt')
    Write-Host "OK: $RuntimeIdentifier lädt .NET, XAML und Windows-App-Runtime aus der eigenen EXE-Extraktion"
    $script:child.Kill(); $script:child.WaitForExit()
    $plan = Join-Path $invocation 'invalid-plan.json'
    '{}' | Set-Content -LiteralPath $plan -Encoding ascii
    $helper = Start-Mode ('--apply-update "' + $plan + '"')
    Wait-Control { $null -ne (Find-Control $helper 'Name' 'OK') } 'Ungültige Installerübergabe zeigt die konkrete Fehlermeldung'
    if (@([PackagedRuntimePath]::Modules($helper.Id) | Where-Object ModuleName -in @('Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll')).Count -ne 0) { throw 'Hilfsmodus hat XAML oder die Windows-App-Runtime geladen.' }
    $helper.Refresh()
    [PackagedRuntimePath]::CloseOwnDialog($helper.Id, [IntPtr](Find-Control $helper 'Name' 'OK').Current.NativeWindowHandle)
    if (-not $helper.WaitForExit(10000)) { throw 'Ungültiger Hilfsmodus beendet sich nach Schließen des Fehlerdialogs nicht.' }
    if ($helper.ExitCode -ne 1) { throw "Ungültiger Hilfsmodus liefert einen anderen Fehlercode: $($helper.ExitCode)" }
    Write-Host "Startmodi erfolgreich: $RuntimeIdentifier, normaler Kindstart und Hilfsmodus ohne XAML-Initialisierung."
} finally {
    foreach ($process in $processes) {
        try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() } } finally { $process.Dispose() }
    }
}
