$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "artifacts\publish\DeployDesk.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "DeployDesk.exe fehlt. Zuerst scripts\publish.ps1 ausführen."
}

$process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "DeployDesk wurde beim Start beendet (Exit $($process.ExitCode))."
        }
        if ($process.MainWindowHandle -ne 0 -and $process.Responding) {
            $ready = $true
            break
        }
    }

    if (-not $ready) {
        throw "DeployDesk hat innerhalb von 10 Sekunden kein reagierendes Hauptfenster erstellt."
    }

    Write-Host "Starttest erfolgreich: $($process.MainWindowTitle)" -ForegroundColor Green
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
