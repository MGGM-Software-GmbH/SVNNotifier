Option Explicit

' Startet den äußeren PowerShell-Prozess ohne sichtbares Konsolenfenster.
Dim shell, fileSystem, command, index, scriptPath, exitCode
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
If WScript.Arguments.Count = 0 Then WScript.Quit 2
scriptPath = fileSystem.GetAbsolutePathName(WScript.Arguments(0))
If Not fileSystem.FileExists(scriptPath) Then WScript.Quit 3
command = Quote(shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"))
command = command & " -NoProfile -ExecutionPolicy Bypass -File " & Quote(scriptPath)
For index = 1 To WScript.Arguments.Count - 1
    command = command & " " & Quote(WScript.Arguments(index))
Next
exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode

Function Quote(value)
    ' Scriptparameter mit eingebetteten Anführungszeichen werden ausdrücklich abgewiesen.
    If InStr(value, Chr(34)) > 0 Then WScript.Quit 4
    Quote = Chr(34) & value & Chr(34)
End Function
