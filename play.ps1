<#
.SYNOPSIS
    Starts the High Hollow server and a game window for each player, side by side.

.DESCRIPTION
    Builds whatever is out of date, starts the server with its log in a window of its own,
    and opens the players so the first takes leopard 0. Closing every game window, or
    pressing Ctrl+C here, stops the server too.

    The Unity player is rebuilt only when the client's contents change (a stamp of them is
    kept next to the build); level files are copied straight into the build, so editing a
    level never needs a rebuild. The server's log is kept in Builds\logs\server.log.

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
    [switch] $NoLogWindow,
    [string] $Unity = ""
)

$ErrorActionPreference = "Stop"
$clock = [Diagnostics.Stopwatch]::StartNew()
$root = $PSScriptRoot
$playerDir = Join-Path $root "Builds\HighHollow"
$playerExe = Join-Path $playerDir "HighHollow.exe"
$stampFile = Join-Path $playerDir "client.stamp"
$serverDir = Join-Path $root "server"
$serverExe = Join-Path $serverDir "target\release\high-hollow-server.exe"
$logDir = Join-Path $root "Builds\logs"
$serverLog = Join-Path $logDir "server.log"
New-Item -ItemType Directory -Force $logDir | Out-Null

# One progress line, with the time since the script started (so slow steps show).
function Step($text) { Write-Host ("==> {0}  ({1:N1}s)" -f $text, $clock.Elapsed.TotalSeconds) -ForegroundColor Cyan }

# The Unity editor matching ProjectSettings\ProjectVersion.txt, from the Hub's install folder.
function Find-Unity {
    if ($Unity) { return $Unity }
    $line = Get-Content (Join-Path $root "ProjectSettings\ProjectVersion.txt") | Where-Object { $_ -like "m_EditorVersion:*" }
    $version = ($line -split ":", 2)[1].Trim()
    $path = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (Test-Path $path) { return $path }
    throw "Unity $version is not installed. Install it through Unity Hub, or pass -Unity <path to Unity.exe>."
}

# A fingerprint of everything that goes into the player except the level files, which are
# copied in directly. Contents, not file times, so a file Unity re-saves unchanged does not
# force a rebuild.
function Get-ClientStamp {
    $files = @(Get-ChildItem (Join-Path $root "Assets"), (Join-Path $root "ProjectSettings") -Recurse -File |
        Where-Object { $_.FullName -notlike "*\Assets\Crawler\Generated\*" -and $_.FullName -notlike "*\Assets\StreamingAssets\*" })
    $files += Get-Item (Join-Path $root "Packages\manifest.json")
    $sha = [Security.Cryptography.SHA256]::Create()
    $lines = foreach ($f in ($files | Sort-Object FullName)) {
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        $f.FullName.Substring($root.Length) + " " + [BitConverter]::ToString($sha.ComputeHash($bytes))
    }
    [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace("-", "")
}

# Builds the Windows player headlessly (the editor must be closed), then records the stamp.
function Build-Player {
    if ((Test-Path (Join-Path $root "Temp\UnityLockfile")) -and (Get-Process -Name "Unity" -ErrorAction SilentlyContinue)) {
        throw "The Unity editor has this project open, and a project can only be open once. Close the editor, or build from File > Build Profiles to Builds\HighHollow\HighHollow.exe and run with -NoBuild."
    }
    $unityExe = Find-Unity
    $log = Join-Path $logDir "unity-build.log"
    Step "Building the Unity player (1 to 2 minutes; log: Builds\logs\unity-build.log)"
    $unityArgs = @("-batchmode", "-quit", "-projectPath", "`"$root`"", "-buildTarget", "Win64",
                   "-buildWindows64Player", "`"$playerExe`"", "-logFile", "`"$log`"")
    $p = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path $playerExe)) {
        if (Select-String -Path $log -Pattern "No valid Unity Editor license" -Quiet) {
            throw "Unity has no active license on this machine. Sign in to Unity Hub and activate a license (Personal is free), then run this again."
        }
        throw "The Unity build failed (exit code $($p.ExitCode)). See Builds\logs\unity-build.log."
    }
    # Taken after the build: Unity may re-save project files while it builds.
    Set-Content -Path $stampFile -Value (Get-ClientStamp) -NoNewline
}

# Level files go straight into the build; the client reads them at runtime.
function Sync-Levels {
    $src = Join-Path $root "Assets\StreamingAssets\levels"
    $dst = Join-Path $playerDir "HighHollow_Data\StreamingAssets\levels"
    New-Item -ItemType Directory -Force $dst | Out-Null
    $want = Get-ChildItem $src -Filter *.json
    foreach ($f in $want) { Copy-Item $f.FullName (Join-Path $dst $f.Name) -Force }
    Get-ChildItem $dst -Filter *.json | Where-Object { $want.Name -notcontains $_.Name } | Remove-Item -Force
}

# Waits for a line in the server log; fails early if the server has exited.
function Wait-ForLog($pattern, $count, $seconds, $proc) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw "The server stopped:`n$(Get-Content $serverLog -Raw -ErrorAction SilentlyContinue)" }
        $hits = @(Select-String -Path $serverLog -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue).Count
        if ($hits -ge $count) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

# ---------------------------------------------------------------- build

if ($NoBuild) {
    if (-not (Test-Path $playerExe)) { throw "No player at Builds\HighHollow\HighHollow.exe; run without -NoBuild." }
} else {
    $stamp = Get-ClientStamp
    $built = if (Test-Path $stampFile) { Get-Content $stampFile -Raw } else { "" }
    if ($Build -or -not (Test-Path $playerExe) -or $built -ne $stamp) {
        if ($built -and $built -ne $stamp) { Write-Host "    the client has changed since the last build" }
        Build-Player
    } else {
        Step "Unity player is up to date"
    }
}
Sync-Levels

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
$logWindow = $null
$games = @()
try {
    Step "Starting the server on port $Port (log: Builds\logs\server.log)"
    Remove-Item $serverLog -ErrorAction SilentlyContinue
    $server = Start-Process -FilePath $serverExe -ArgumentList "--port $Port --level $Level" -WorkingDirectory $serverDir `
        -RedirectStandardError $serverLog -RedirectStandardOutput (Join-Path $logDir "server.out.log") -WindowStyle Hidden -PassThru
    if (-not (Wait-ForLog "listening on" 1 10 $server)) { throw "The server did not start listening on port $Port." }
    if (-not $NoLogWindow) {
        $follow = "`$host.UI.RawUI.WindowTitle = 'High Hollow server'; Get-Content -Path '$serverLog' -Wait -Tail 200"
        $logWindow = Start-Process powershell -ArgumentList "-NoProfile", "-Command", $follow -PassThru
    }

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
        # The next player starts once this one has joined, so the first takes leopard 0.
        if (-not (Wait-ForLog "joined as client" ($i + 1) 30 $server)) {
            Write-Warning "player $($i + 1) has not joined after 30 s; see Builds\logs\player$($i + 1).log"
        }
    }
    Step "Everyone is on the trail"

    Write-Host ""
    Write-Host "Playing. Click a window to steer its leopard. Close every game window (or press Ctrl+C) to stop." -ForegroundColor Green
    Write-Host "Logs: Builds\logs\server.log, player1.log, player2.log"
    $games | Wait-Process
} finally {
    foreach ($g in $games) { if ($g -and -not $g.HasExited) { Stop-Process -Id $g.Id -Force -ErrorAction SilentlyContinue } }
    if ($server -and -not $server.HasExited) {
        Step "Stopping the server"
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    if ($logWindow -and -not $logWindow.HasExited) { Stop-Process -Id $logWindow.Id -Force -ErrorAction SilentlyContinue }
}
