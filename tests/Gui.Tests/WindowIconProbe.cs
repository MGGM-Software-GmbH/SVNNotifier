using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>Liest das tatsächlich am HWND installierte Symbol unabhängig von der Statusberechnung der Anwendung.</summary>
internal static class WindowIconProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int HorizontalResolution, VerticalResolution; public uint ColorsUsed, ImportantColors;
    }
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr device);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr device, ref BitmapHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr device, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr device);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DrawIconEx(IntPtr device, int left, int top, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);

    /// <summary>Rendert ein HICON auf dieselbe weiße 32-Pixel-Fläche für Ressourcenvergleich und Sichtnachweis.</summary>
    internal static byte[] Pixels(IntPtr icon)
    {
        if (icon == IntPtr.Zero) throw new InvalidOperationException("Das native Fenstersymbol fehlt.");
        var header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = 32, Height = -32, Planes = 1, Bits = 32 };
        IntPtr bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr device = CreateCompatibleDC(IntPtr.Zero);
        if (bitmap == IntPtr.Zero || device == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (device != IntPtr.Zero) DeleteDC(device);
            throw new InvalidOperationException("Die Symbolaufnahme konnte nicht angelegt werden.");
        }
        IntPtr previous = SelectObject(device, bitmap);
        try
        {
            var pixels = Enumerable.Repeat((byte)255, 32 * 32 * 4).ToArray();
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            if (!DrawIconEx(device, 0, 0, icon, 32, 32, 0, IntPtr.Zero, 3)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { SelectObject(device, previous); DeleteObject(bitmap); DeleteDC(device); }
    }
}
