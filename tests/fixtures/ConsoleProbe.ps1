param([string] $ResultPath)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ConsoleProbe {
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
$window = [ConsoleProbe]::GetConsoleWindow()
@{ visible = [ConsoleProbe]::IsWindowVisible($window); arguments = @($args) } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
if ($args -contains '--failure') { exit 9 }
exit 0
