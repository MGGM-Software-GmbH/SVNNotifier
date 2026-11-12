# Prüft den eingeschränkten CI-Prozess vor Paketbuild und Produktprüfungen.
$ErrorActionPreference = 'Stop'
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Der Testprozess besitzt weiterhin Administratorrechte.'
}
Write-Host 'Windows-Testprozess ohne aktive Administratorgruppe gestartet.'
if (-not ([Security.Principal.WindowsIdentity]::GetCurrent()).Groups.Value.Contains('S-1-5-32-545')) {
    throw 'Dem Testkonto fehlt die normale Windows-Benutzergruppe.'
}
Write-Host "Testsitzung: $([Diagnostics.Process]::GetCurrentProcess().SessionId)"
