# Jeder Konsolenprozess erhält CREATE_NO_WINDOW, auch wenn der aufrufende Testhost keine Konsole besitzt.
function Invoke-QuietConsole {
    # Einfache Funktion: MSBuild-Argumente wie -p dürfen keine PowerShell-CommonParameter binden.
    if ($args.Count -eq 0) { throw 'Ein Programmname ist erforderlich.' }
    $FilePath = [string] $args[0]
    $Arguments = @($args | Select-Object -Skip 1)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $quoted = foreach ($argument in $Arguments) {
        '"' + ([regex]::Replace([regex]::Replace($argument, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1')) + '"'
    }
    $start.Arguments = $quoted -join ' '
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Konsolenprozess konnte nicht gestartet werden: $FilePath" }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errors = $errorTask.GetAwaiter().GetResult()
        if ($output) { Write-Output $output }
        if ($errors) { Write-Output $errors }
        $global:LASTEXITCODE = $process.ExitCode
    }
    finally { $process.Dispose() }
}
