using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace CHD.SVN_Notifier;

/// <summary>Hält die vorhandenen Statussymbole für Fenster, Taskleiste und Tray gemeinsam bis zum Fensterende.</summary>
internal sealed class StatusIcons : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private readonly Dictionary<string, IntPtr> icons = new();
    private string? state;
    internal IntPtr Current { get; private set; }

    internal StatusIcons()
    {
        try
        {
            foreach (string name in new[] { "Unknown", "UpToDate", "NeedUpdate", "Error" }) icons.Add(name, LoadIcon(name));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Bewahrt die Priorität der WinForms-Ausgabe, einschließlich deaktivierter Einträge und kombinierter Zustände.</summary>
    internal void Update(AppWindow window, IEnumerable<SvnItem> folders)
    {
        var items = folders.ToArray();
        string next = items.Any(item => item.Status == SvnStatus.Error) ? "Error"
            : items.Any(ApplicationCoordinator.NeedsUpdate) ? "NeedUpdate"
            : items.Any(item => item.Status == SvnStatus.Unknown) ? "Unknown"
            : items.Any(item => item.Status is SvnStatus.UpToDate or SvnStatus.UpToDate_Modified) ? "UpToDate" : "Unknown";
        if (state == next) return;
        window.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icons[next]));
        Current = icons[next];
        state = next;
    }

    /// <summary>Lädt die vorhandene 32-Pixel-Ressource ohne externe ICO-Dateien neben der portablen EXE.</summary>
    private static IntPtr LoadIcon(string state)
    {
        var assembly = typeof(StatusIcons).Assembly;
        string name = assembly.GetManifestResourceNames().Single(name => name.EndsWith("TrayIcon_" + state + ".ico", StringComparison.Ordinal));
        using var input = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream(); input.CopyTo(buffer); byte[] ico = buffer.ToArray();
        int count = BitConverter.ToUInt16(ico, 4);
        int entry = Enumerable.Range(0, count).OrderBy(index => Math.Abs((ico[6 + index * 16] == 0 ? 256 : ico[6 + index * 16]) - 32)).First() * 16 + 6;
        int length = BitConverter.ToInt32(ico, entry + 8), offset = BitConverter.ToInt32(ico, entry + 12);
        IntPtr icon = CreateIconFromResourceEx(ico.AsSpan(offset, length).ToArray(), (uint)length, true, 0x30000, 0, 0, 0);
        if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Statussymbol konnte nicht geladen werden.");
        return icon;
    }

    public void Dispose()
    {
        foreach (IntPtr icon in icons.Values) DestroyIcon(icon);
        icons.Clear();
    }
}
