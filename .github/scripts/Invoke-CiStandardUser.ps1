param(
    [Parameter(Mandatory)] [string] $ScriptPath,
    [Parameter(Mandatory)] [string] $LogPath,
    [int] $TimeoutSeconds = 1800,
    [switch] $ProbeNativeProcess
)

# Nur die kurzlebige GitHub-VM erhält ein eigenes Standardkonto. Kennwörter bleiben ausschließlich im Prozessspeicher.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Dieser Starter ist ausschließlich für gehostete GitHub-Runner vorgesehen.' }
$workspace = (Resolve-Path -LiteralPath $env:GITHUB_WORKSPACE).Path
if ((Get-Location).Path -ne $workspace) { throw 'Der Prüfstart muss im CI-Arbeitsverzeichnis erfolgen.' }
$testName = 'SvnTest' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$randomBytes = New-Object byte[] 36
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
$password = ConvertTo-SecureString ('Aa1!' + [Convert]::ToBase64String($randomBytes)) -AsPlainText -Force
[Array]::Clear($randomBytes, 0, $randomBytes.Length)
$account = $null
$sessionAccess = $null
$originalAcl = Get-Acl -LiteralPath $workspace
try {
    $account = New-LocalUser -Name $testName -Password $password -AccountNeverExpires -Description 'Isolierte SVNNotifier-CI-Prüfung'
    $users = Get-LocalGroup -SID 'S-1-5-32-545'
    if (-not @(Get-LocalGroupMember -Group $users | Where-Object { $_.SID -eq $account.SID }).Count) {
        Add-LocalGroupMember -Group $users -Member $account
    }
    $testAcl = Get-Acl -LiteralPath $workspace
    $permission = New-Object Security.AccessControl.FileSystemAccessRule($account.SID, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $testAcl.AddAccessRule($permission)
    Set-Acl -LiteralPath $workspace -AclObject $testAcl
    # Der fremde Anmeldetoken benötigt eigene benannte SDK-/CLI-Objekte in der bestehenden Runnersitzung.
    Add-Type -Path (Join-Path $PSScriptRoot 'SessionObjectAccess.cs')
    $sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $sessionAccess = [SessionObjectAccess]::new("\Sessions\$sessionId\BaseNamedObjects", $account.SID)
    Write-Host 'CI-Testkonto erhält vorübergehend Erstellrechte für eigene benannte Sitzungsobjekte.'
    $credential = New-Object PSCredential($testName, $password)
    & "$PSScriptRoot/Invoke-IsolatedDesktop.ps1" -ScriptPath $ScriptPath -LogPath $LogPath -TimeoutSeconds $TimeoutSeconds -ProbeNativeProcess:$ProbeNativeProcess -TestCredential $credential
} finally {
    # Der untergeordnete Starter schließt zuerst seinen Job und damit alle eigenen Testprozesse.
    try {
        if ($sessionAccess) { $sessionAccess.Dispose(); Write-Host 'CI-Sitzungsobjektrechte wiederhergestellt.' }
    } finally {
        try { Set-Acl -LiteralPath $workspace -AclObject $originalAcl } finally {
            if ($account) { Remove-LocalUser -SID $account.SID }
            $credential = $null
            $password.Dispose()
        }
    }
}
