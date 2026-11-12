param(
    [Parameter(Mandatory)] [string] $ScriptPath,
    [string[]] $ScriptArguments = @(),
    [string] $LogPath = '',
    [int] $TimeoutSeconds = 1800,
    [switch] $StandardUser,
    [switch] $ProbeNativeProcess
)

# Startet Desktoptests ohne Umschalten des Eingabedesktops und begrenzt ihre Prozesslebensdauer.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptFile = (Resolve-Path -LiteralPath $ScriptPath).Path
if (-not $LogPath) { $LogPath = $scriptFile + '.' + [Guid]::NewGuid().ToString('N') + '.desktop.log' }
$transcriptPath = [IO.Path]::GetFullPath($LogPath)
if (Test-Path -LiteralPath $transcriptPath) { throw 'Das Testprotokoll muss neu sein.' }
if ($TimeoutSeconds -le 0) { throw 'Die Zeitgrenze muss positiv sein.' }

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class IsolatedDesktopTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string Reserved; public string Desktop; public string Title;
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public short Show; public short ReservedSize; public IntPtr ReservedPointer;
        public IntPtr Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Size; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(IntPtr source, uint flags, uint disabledCount, SidAndAttributes[] disabled,
        uint deletedCount, IntPtr deleted, uint restrictedCount, IntPtr restricted, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSidToSid(string text, out IntPtr sid);
    [DllImport("advapi32.dll")] private static extern uint GetLengthSid(IntPtr sid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int informationClass, ref SidAndAttributes information, uint length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);

    // CI läuft erhöht. Der Produktpfad wird mit demselben Benutzer, deaktivierter Administratorgruppe und mittlerer Integrität geprüft.
    private static IntPtr StandardUserToken()
    {
        IntPtr source = IntPtr.Zero, token = IntPtr.Zero, administrators = IntPtr.Zero, medium = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x008b, out source) ||
                !ConvertStringSidToSid("S-1-5-32-544", out administrators) ||
                !ConvertStringSidToSid("S-1-16-8192", out medium)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var disabled = new[] { new SidAndAttributes { Sid = administrators } };
            if (!CreateRestrictedToken(source, 5, 1, disabled, 0, IntPtr.Zero, 0, IntPtr.Zero, out token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var integrity = new SidAndAttributes { Sid = medium, Attributes = 0x20 };
            if (!SetTokenInformation(token, 25, ref integrity, (uint)Marshal.SizeOf(typeof(SidAndAttributes)) + GetLengthSid(medium)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using (var identity = new System.Security.Principal.WindowsIdentity(token))
                Console.WriteLine("Eingeschränkter Token: Benutzer=" + identity.User.Value + "; Besitzer=" + identity.Owner.Value);
            IntPtr result = token; token = IntPtr.Zero; return result;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (source != IntPtr.Zero) CloseHandle(source);
            if (administrators != IntPtr.Zero) LocalFree(administrators);
            if (medium != IntPtr.Zero) LocalFree(medium);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long ProcessTime, JobTime; public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr security, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimit limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    // Die Argumente werden nach den Windows-Regeln für Anführungszeichen und abschließende Backslashes geschützt.
    private static string Quote(string value)
    {
        var result = new StringBuilder("\""); int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            result.Append(character); backslashes = 0;
        }
        result.Append('\\', backslashes * 2); result.Append('"'); return result.ToString();
    }

    public static int Run(string executable, string[] arguments, string directory, int timeoutSeconds, bool standardUser)
    {
        var station = new StringBuilder(256); int needed;
        if (!GetUserObjectInformation(GetProcessWindowStation(), 2, station, 512, out needed)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Console.WriteLine("Testfensterstation: " + station + "; Sitzung=" + System.Diagnostics.Process.GetCurrentProcess().SessionId);
        string name = "SVNNotifierTests-" + Guid.NewGuid().ToString("N");
        // Kein DESKTOP_SWITCHDESKTOP: Der Testdesktop wird niemals zum Eingabedesktop.
        IntPtr descriptor = IntPtr.Zero, security = IntPtr.Zero, desktop = IntPtr.Zero;
        try
        {
            if (standardUser)
            {
                // Erhöhte Runner vererben sonst einen Desktop, den der eingeschränkte Kindprozess nicht initialisieren kann.
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                uint size;
                if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;0x000200CF;;;" + user + ")S:(ML;;NW;;;ME)", 1, out descriptor, out size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var attributes = new SecurityAttributes { Size = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor };
                security = Marshal.AllocHGlobal(attributes.Size); Marshal.StructureToPtr(attributes, security, false);
            }
            desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x00cf, security);
        }
        finally
        {
            if (security != IntPtr.Zero) Marshal.FreeHGlobal(security);
            if (descriptor != IntPtr.Zero) LocalFree(descriptor);
        }
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Testdesktop konnte nicht erstellt werden.");
        IntPtr job = IntPtr.Zero, token = IntPtr.Zero; var process = new ProcessInformation();
        try
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimit(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimit))))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = station.ToString() + "\\" + name };
            var command = new StringBuilder(Quote(executable));
            foreach (string argument in arguments) command.Append(' ').Append(Quote(argument));
            if (standardUser) token = StandardUserToken();
            bool created = standardUser
                ? CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000004, IntPtr.Zero, directory, ref startup, out process)
                : CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000004, IntPtr.Zero, directory, ref startup, out process);
            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Testprozess konnte nicht isoliert gestartet werden.");
            if (!AssignProcessToJobObject(job, process.Process))
            {
                int error = Marshal.GetLastWin32Error(); TerminateProcess(process.Process, 1); throw new Win32Exception(error);
            }
            if (ResumeThread(process.Thread) == UInt32.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            Console.WriteLine("Isolierter Testdesktop: " + name + "; Prozess=" + process.ProcessId);
            uint wait = WaitForSingleObject(process.Process, checked((uint)timeoutSeconds * 1000));
            if (wait == 258) throw new TimeoutException("Zeitgrenze des isolierten Desktoptests erreicht.");
            if (wait != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint exitCode;
            if (!GetExitCodeProcess(process.Process, out exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)exitCode);
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (job != IntPtr.Zero) CloseHandle(job);
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            CloseDesktop(desktop);
        }
    }
}
'@

# Der Kindprozess prüft die Isolation vor jeder Ausführung des Testskripts.
$guard = @'
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; using System.Text; public static class DesktopGuard {
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint id);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
[DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
[DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
public static string Name(IntPtr desktop) { var name = new StringBuilder(256); int needed; if (!GetUserObjectInformation(desktop, 2, name, 512, out needed)) throw new System.ComponentModel.Win32Exception(); return name.ToString(); }
}'
$currentName = [DesktopGuard]::Name([DesktopGuard]::GetThreadDesktop([DesktopGuard]::GetCurrentThreadId()))
$inputDesktop = [DesktopGuard]::OpenInputDesktop(0, $false, 1)
if ($inputDesktop -eq [IntPtr]::Zero) { throw 'Eingabedesktop konnte nicht geprüft werden.' }
try { $inputName = [DesktopGuard]::Name($inputDesktop) } finally { [void][DesktopGuard]::CloseDesktop($inputDesktop) }
if ($currentName -eq $inputName -or -not $currentName.StartsWith('SVNNotifierTests-')) { throw 'GUI-Test auf dem Benutzerdesktop verweigert.' }
'@
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @{ path = $scriptFile; arguments = @($ScriptArguments); log = $transcriptPath } -Compress)))
$bootstrap = "`$request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$payload')) | ConvertFrom-Json; Start-Transcript -LiteralPath `$request.log | Out-Null;"
$invocation = $bootstrap + "`ntry {`n" + $guard + "`n`$testArguments = @(`$request.arguments | Where-Object { `$null -ne `$_ }); & `$request.path @testArguments; if (`$LASTEXITCODE) { exit `$LASTEXITCODE } } catch { Write-Error `$_; exit 1 } finally { Stop-Transcript | Out-Null }"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($invocation))
$executable = Join-Path $PSHOME 'powershell.exe'
if ($ProbeNativeProcess) {
    $nativeResult = [IsolatedDesktopTest]::Run((Join-Path $env:SystemRoot 'System32/cmd.exe'), @('/d', '/c', 'exit', '0'), (Get-Location).Path, 15, $StandardUser.IsPresent)
    if ($nativeResult -ne 0) { throw "Nativer CI-Prozessstart fehlgeschlagen: Exit-Code $nativeResult" }
    Write-Host 'Nativer CI-Prozessstart erfolgreich; PowerShell wird gesondert geprüft.'
}
$result = [IsolatedDesktopTest]::Run($executable, @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded), (Get-Location).Path, $TimeoutSeconds, $StandardUser.IsPresent)
Write-Host "Testprotokoll: $transcriptPath"
if ($result -ne 0) { throw "Isolierter Desktoptest fehlgeschlagen: Exit-Code $result" }
