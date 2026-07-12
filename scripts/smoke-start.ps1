$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "artifacts\publish\DeployDesk.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "DeployDesk.exe is missing. Run scripts\publish.ps1 first."
}

$process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "DeployDesk exited during startup (exit $($process.ExitCode))."
        }
        if ($process.MainWindowHandle -ne 0 -and $process.Responding) {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        throw "DeployDesk did not create a responsive main window within 10 seconds."
    }

    Write-Host "Startup smoke test passed: $($process.MainWindowTitle)" -ForegroundColor Green
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
