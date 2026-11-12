# Prüft den eingeschränkten CI-Prozess vor Paketbuild und Produktprüfungen.
$ErrorActionPreference = 'Stop'
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Der Testprozess besitzt weiterhin Administratorrechte.'
}
Write-Host 'Windows-Testprozess ohne aktive Administratorgruppe gestartet.'
Write-Host "Testsitzung: $([Diagnostics.Process]::GetCurrentProcess().SessionId)"
