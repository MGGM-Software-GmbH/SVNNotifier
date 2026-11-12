using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>Begrenzt eine zusätzliche Objektverzeichnisfreigabe auf die Lebensdauer des CI-Testkontos.</summary>
public sealed class SessionObjectAccess : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeName { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes { public int Length; public IntPtr Root, Name; public uint Flags; public IntPtr Descriptor, Quality; }
    [DllImport("ntdll.dll")] private static extern int NtOpenDirectoryObject(out IntPtr handle, uint access, ref ObjectAttributes attributes);
    [DllImport("ntdll.dll")] private static extern int NtQuerySecurityObject(IntPtr handle, uint information, byte[] descriptor, uint length, out uint needed);
    [DllImport("ntdll.dll")] private static extern int NtSetSecurityObject(IntPtr handle, uint information, byte[] descriptor);
    [DllImport("ntdll.dll")] private static extern int NtClose(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern uint RtlNtStatusToDosError(int status);

    private IntPtr directory;
    private readonly byte[] original;

    /// <summary>Erlaubt nur dem angegebenen Konto das Anlegen und Auflösen eigener benannter Sitzungsobjekte.</summary>
    public SessionObjectAccess(string path, SecurityIdentifier user)
    {
        var name = new UnicodeName { Length = checked((ushort)(path.Length * 2)), MaximumLength = checked((ushort)((path.Length + 1) * 2)), Buffer = Marshal.StringToHGlobalUni(path) };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UnicodeName)));
        try
        {
            Marshal.StructureToPtr(name, memory, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf(typeof(ObjectAttributes)), Name = memory, Flags = 0x40 };
            Check(NtOpenDirectoryObject(out directory, 0x00060000, ref attributes), "Objektverzeichnis öffnen");
            uint size;
            NtQuerySecurityObject(directory, 4, null, 0, out size);
            if (size == 0) throw new InvalidOperationException("Die Objektverzeichnis-DACL konnte nicht gelesen werden.");
            original = new byte[size];
            Check(NtQuerySecurityObject(directory, 4, original, size, out size), "Objektverzeichnis-DACL lesen");
            var descriptor = new RawSecurityDescriptor(original, 0);
            if (descriptor.DiscretionaryAcl == null) throw new InvalidOperationException("Das Objektverzeichnis hat keine begrenzte DACL.");
            // QUERY, TRAVERSE, CREATE_OBJECT und CREATE_SUBDIRECTORY; keine Rechteänderung und keine vererbte Freigabe.
            descriptor.DiscretionaryAcl.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x000f, user, false, null));
            var updated = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(updated, 0);
            Check(NtSetSecurityObject(directory, 4, updated), "CI-Objektverzeichniszugriff setzen");
        }
        catch { if (directory != IntPtr.Zero) NtClose(directory); directory = IntPtr.Zero; throw; }
        finally { Marshal.FreeHGlobal(memory); Marshal.FreeHGlobal(name.Buffer); }
    }

    /// <summary>Stellt nach dem Ende aller eigenen Testprozesse die unveränderte Ausgangs-DACL wieder her.</summary>
    public void Dispose()
    {
        if (directory == IntPtr.Zero) return;
        try { Check(NtSetSecurityObject(directory, 4, original), "Objektverzeichnis-DACL wiederherstellen"); }
        finally { NtClose(directory); directory = IntPtr.Zero; }
    }

    private static void Check(int status, string operation)
    {
        if (status < 0) throw new Win32Exception(unchecked((int)RtlNtStatusToDosError(status)), operation);
    }
}
