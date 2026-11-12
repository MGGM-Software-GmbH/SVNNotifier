using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CHD.SVN_Notifier;

/// <summary>Verwendet Shell-Tray und native Menüs ohne Windows-Forms-Nachrichtenloop.</summary>
internal sealed class TrayService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate IntPtr WindowCallback(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Shell_NotifyIcon(uint action, ref NotifyData data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr window, WindowCallback callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, WindowCallback callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    private readonly IntPtr window;
    private readonly WindowCallback callback;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Dictionary<string, IntPtr> icons = new();
    private NotifyData data;
    private bool disposed;
    internal bool CanUpdateAll { get; set; }
    internal event Action? ShowRequested;
    internal event Action? UpdateAllRequested;
    internal event Action? ExitRequested;
    internal event Action<bool>? SessionEnding;
    internal event Action<bool>? Suspended;

    internal TrayService(IntPtr window)
    {
        this.window = window;
        callback = OnMessage;
        try
        {
            foreach (string name in new[] { "Unknown", "UpToDate", "NeedUpdate", "Error" }) icons.Add(name, LoadIcon(name));
            if (!SetWindowSubclass(window, callback, new UIntPtr(1), UIntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Callback = 0x8001,
                Flags = 1 | 2 | 4, Icon = icons["Unknown"], Tip = "SVN Notifier", Info = "", Title = "" };
            if (!Shell_NotifyIcon(0, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Tray-Symbol konnte nicht registriert werden.");
        }
        catch
        {
            // Ein fehlgeschlagener Konstruktor darf keinen nativen Callback auf ein freigegebenes Delegate hinterlassen.
            RemoveWindowSubclass(window, callback, new UIntPtr(1));
            foreach (var icon in icons.Values) DestroyIcon(icon);
            throw;
        }
    }

    internal void Update(IEnumerable<SvnItem> folders)
    {
        var items = folders.ToArray();
        string state = items.Any(item => item.Status == SvnStatus.Error) ? "Error" : items.Any(ApplicationCoordinator.NeedsUpdate) ? "NeedUpdate"
            : items.Any(item => item.Status == SvnStatus.Unknown) || items.Length == 0 ? "Unknown" : "UpToDate";
        data.Icon = icons[state];
        string text = "SVN Notifier" + string.Concat(items.Where(item => item.Enabled && (item.Status == SvnStatus.Error || ApplicationCoordinator.NeedsUpdate(item)))
            .Select(item => "\n" + StatusTextConverter.Text(item.Status) + ": " + item.VisiblePath));
        data.Tip = text.Length > 127 ? text[..123] + "…" : text;
        if (!Shell_NotifyIcon(1, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Tray-Symbol konnte nicht aktualisiert werden.");
        CanUpdateAll = !Config.ChangeLogBeforeUpdate && items.Any(ApplicationCoordinator.NeedsUpdate);
    }

    private IntPtr OnMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr reference)
    {
        if (message == taskbarCreated && !disposed) Shell_NotifyIcon(0, ref data);
        if (message == 0x11) SessionEnding?.Invoke(true);
        if (message == 0x16) SessionEnding?.Invoke(wParam != IntPtr.Zero);
        if (message == 0x218 && wParam.ToInt64() == 4) Suspended?.Invoke(true);
        if (message == 0x218 && wParam.ToInt64() is 7 or 18) Suspended?.Invoke(false);
        if (message == 0x8001)
        {
            uint action = (uint)lParam.ToInt64() & 0xffff;
            if (action is 0x202 or 0x203) ShowRequested?.Invoke();
            if (action == 0x205) ShowMenu();
        }
        return DefSubclassProc(handle, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, new UIntPtr(1), Loc.Tray_ShowProgram);
            AppendMenu(menu, CanUpdateAll ? 0u : 1u, new UIntPtr(2), Loc.Tray_UpdateAll);
            AppendMenu(menu, 0x800, UIntPtr.Zero, "");
            AppendMenu(menu, 0, new UIntPtr(3), Loc.Tray_Exit);
            GetCursorPos(out var point);
            SetForegroundWindow(window);
            uint selected = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero);
            if (selected == 1) ShowRequested?.Invoke();
            if (selected == 2 && CanUpdateAll) UpdateAllRequested?.Invoke();
            if (selected == 3) ExitRequested?.Invoke();
            PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
    }

    private static IntPtr LoadIcon(string state)
    {
        var assembly = typeof(TrayService).Assembly;
        string name = assembly.GetManifestResourceNames().Single(name => name.EndsWith("TrayIcon_" + state + ".ico", StringComparison.Ordinal));
        using var input = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream(); input.CopyTo(buffer); byte[] ico = buffer.ToArray();
        int count = BitConverter.ToUInt16(ico, 4);
        int entry = Enumerable.Range(0, count).OrderBy(index => Math.Abs((ico[6 + index * 16] == 0 ? 256 : ico[6 + index * 16]) - 32)).First() * 16 + 6;
        int length = BitConverter.ToInt32(ico, entry + 8), offset = BitConverter.ToInt32(ico, entry + 12);
        IntPtr icon = CreateIconFromResourceEx(ico.AsSpan(offset, length).ToArray(), (uint)length, true, 0x30000, 0, 0, 0);
        if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Tray-Symbol konnte nicht geladen werden.");
        return icon;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Shell_NotifyIcon(2, ref data);
        RemoveWindowSubclass(window, callback, new UIntPtr(1));
        foreach (IntPtr icon in icons.Values) DestroyIcon(icon);
    }
}
