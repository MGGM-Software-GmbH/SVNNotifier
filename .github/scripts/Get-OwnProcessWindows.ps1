# Die UI-Automation-Wurzel kann einen anderen Desktop abbilden. Fenster werden zuerst am tatsächlichen Testdesktop nach eigener Prozess-ID zugeordnet.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (-not ('OwnProcessWindows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class OwnProcessWindows
{
    private delegate bool EnumerateWindow(IntPtr window, IntPtr context);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool EnumDesktopWindows(IntPtr desktop, EnumerateWindow callback, IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);

    // Nur sichtbare Fenster des angegebenen eigenen Testprozesses auf dem aktuellen Testdesktop liefern.
    public static IntPtr[] Get(int processId)
    {
        var windows = new List<IntPtr>();
        EnumerateWindow callback = (window, context) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) windows.Add(window);
            return true;
        };
        if (!EnumDesktopWindows(GetThreadDesktop(GetCurrentThreadId()), callback, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return windows.ToArray();
    }

    // Namen ohne Bildschirmaufnahme helfen, einen Prozess auf dem falschen Desktop zu erkennen.
    public static string[] Desktops(int processId)
    {
        var names = new HashSet<string>();
        using (var process = Process.GetProcessById(processId))
            foreach (ProcessThread thread in process.Threads) {
                var name = new StringBuilder(256); int needed;
                if (GetUserObjectInformation(GetThreadDesktop((uint)thread.Id), 2, name, 512, out needed)) names.Add(name.ToString());
            }
        return new List<string>(names).ToArray();
    }
}
'@
}
function Get-OwnProcessWindows([int] $ProcessId) {
    foreach ($window in [OwnProcessWindows]::Get($ProcessId)) {
        [System.Windows.Automation.AutomationElement]::FromHandle($window)
    }
}
