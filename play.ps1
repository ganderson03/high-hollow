<#
.SYNOPSIS
    Starts the High Hollow server and a game window for each player, side by side.

.DESCRIPTION
    Builds whatever is missing or out of date (the Rust server always, the Unity player when
    there is none or the client has changed since), starts the server in its own window, then
    opens the players one after another so the first takes leopard 0. Closing every game
    window, or pressing Ctrl+C here, stops the server too.

.EXAMPLE
    .\play.ps1                  # server + two players
.EXAMPLE
    .\play.ps1 -Players 1       # server + one player holding both leopards
.EXAMPLE
    .\play.ps1 -Level hollow    # start the trail on another level
.EXAMPLE
    .\play.ps1 -Build           # rebuild the Unity player even if it looks current
#>
param(
    [ValidateRange(1, 2)] [int] $Players = 2,
    [string] $Level = "foothills-1",
    [int] $Port = 7777,
    [switch] $Build,
    [switch] $NoBuild,
    [string] $Unity = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$playerExe = Join-Path $root "Builds\HighHollow\HighHollow.exe"
$serverDir = Join-Path $root "server"
$serverExe = Join-Path $serverDir "target\release\high-hollow-server.exe"
$logDir = Join-Path $root "Builds\logs"
New-Item -ItemType Directory -Force $logDir | Out-Null

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Find-Unity {
    if ($Unity) { return $Unity }
    $line = Get-Content (Join-Path $root "ProjectSettings\ProjectVersion.txt") | Where-Object { $_ -like "m_EditorVersion:*" }
    $version = ($line -split ":", 2)[1].Trim()
    $path = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (Test-Path $path) { return $path }
    throw "Unity $version is not installed. Install it through Unity Hub, or pass -Unity <path to Unity.exe>."
}

function Test-ClientChanged {
    if (-not (Test-Path $playerExe)) { return $true }
    $built = (Get-Item $playerExe).LastWriteTime
    $newer = Get-ChildItem (Join-Path $root "Assets"), (Join-Path $root "ProjectSettings"), (Join-Path $root "Packages") -Recurse -File |
        Where-Object { $_.Extension -ne ".meta" -and $_.LastWriteTime -gt $built -and $_.FullName -notlike "*\Assets\Crawler\Generated\*" } |
        Select-Object -First 1
    if ($newer) { Write-Host "    $($newer.Name) changed since the last build" }
    return [bool]$newer
}

function Build-Player {
    if (Test-Path (Join-Path $root "Temp\UnityLockfile")) {
        if (Get-Process -Name "Unity" -ErrorAction SilentlyContinue) {
            throw "The Unity editor has this project open, and a project can only be open once. Close the editor, or build from File > Build Profiles to Builds\HighHollow\HighHollow.exe and run with -NoBuild."
        }
    }
    $unityExe = Find-Unity
    $log = Join-Path $logDir "unity-build.log"
    Step "Building the Unity player (a few minutes; log: Builds\logs\unity-build.log)"
    $unityArgs = @("-batchmode", "-quit", "-projectPath", "`"$root`"", "-buildTarget", "Win64",
              "-buildWindows64Player", "`"$playerExe`"", "-logFile", "`"$log`"")
    $p = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path $playerExe)) {
        if (Select-String -Path $log -Pattern "No valid Unity Editor license" -Quiet) {
            throw "Unity has no active license on this machine. Sign in to Unity Hub and activate a license (Personal is free), then run this again."
        }
        throw "The Unity build failed (exit code $($p.ExitCode)). See Builds\logs\unity-build.log."
    }
}

function Wait-ForPort($port, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $c = New-Object System.Net.Sockets.TcpClient
        try { $c.Connect("127.0.0.1", $port); return $true } catch { Start-Sleep -Milliseconds 200 } finally { $c.Close() }
    }
    return $false
}

# ---------------------------------------------------------------- build

if ($NoBuild) {
    if (-not (Test-Path $playerExe)) { throw "No player at Builds\HighHollow\HighHollow.exe; run without -NoBuild." }
} elseif ($Build -or (Test-ClientChanged)) {
    Build-Player
} else {
    Step "Unity player is up to date"
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw "cargo was not found. Install Rust from https://rustup.rs and open a new terminal."
}
Step "Building the server"
Push-Location $serverDir
try {
    cargo build --release --quiet
    if ($LASTEXITCODE -ne 0) { throw "The server build failed." }
} finally { Pop-Location }

# ---------------------------------------------------------------- run

$busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($busy) {
    $owner = Get-Process -Id $busy[0].OwningProcess -ErrorAction SilentlyContinue
    if ($owner -and $owner.ProcessName -eq "high-hollow-server") {
        Step "Stopping a server left running on port $Port"
        Stop-Process -Id $owner.Id -Force
        Start-Sleep -Milliseconds 300
    } else {
        throw "Port $Port is in use by $($owner.ProcessName). Pick another with -Port."
    }
}

$server = $null
$games = @()
try {
    Step "Starting the server on port $Port (its log is in the new window)"
    $server = Start-Process -FilePath $serverExe -ArgumentList "--port $Port --level $Level" -WorkingDirectory $serverDir -PassThru
    if (-not (Wait-ForPort $Port 10)) { throw "The server did not start listening on port $Port." }

    # Side by side on the primary screen.
    Add-Type -AssemblyName System.Windows.Forms
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $w = [int][Math]::Floor($area.Width / $Players)
    $h = [int][Math]::Min($area.Height - 40, [Math]::Floor($w * 10 / 16))
    $names = @("snep", "pine")

    for ($i = 0; $i -lt $Players; $i++) {
        Step "Starting player $($i + 1) ($($names[$i]))"
        $gameArgs = @(
            "-screen-fullscreen", "0", "-screen-width", "$w", "-screen-height", "$h",
            "-window-x", "$($area.X + $i * $w)", "-window-y", "$($area.Y)",
            "-server", "127.0.0.1:$Port", "-name", $names[$i], "-autostart",
            "-logFile", "`"$(Join-Path $logDir "player$($i + 1).log")`""
        )
        $games += Start-Process -FilePath $playerExe -ArgumentList $gameArgs -PassThru
        # Let the first player join before the second, so it takes leopard 0.
        if ($i -lt $Players - 1) { Start-Sleep -Seconds 3 }
    }

    Write-Host ""
    Write-Host "Playing. Click a window to steer its leopard. Close every game window (or press Ctrl+C) to stop." -ForegroundColor Green
    Write-Host "Player logs: Builds\logs\player1.log, player2.log"
    $games | Wait-Process
} finally {
    foreach ($g in $games) { if ($g -and -not $g.HasExited) { Stop-Process -Id $g.Id -Force -ErrorAction SilentlyContinue } }
    if ($server -and -not $server.HasExited) {
        Step "Stopping the server"
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
}
