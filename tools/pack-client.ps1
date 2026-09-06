<#
.SYNOPSIS
    Builds a branded installer from an already-published client. No source, no compiler.

.DESCRIPTION
    For someone running their own world who wants to hand players an installer carrying their name
    and their icon, rather than this project's.

    Velopack packages a *folder of files* — it does not care where the folder came from — so a
    published client from a GitHub release is all the input needed. Nothing here compiles anything;
    the only tool required is `vpk`, which the script installs on demand.

    What this changes:
      - the name on the installer, the Start Menu and desktop shortcuts, and Add/Remove Programs
      - the icon on Setup.exe and on the shortcuts it creates
      - the install folder under %LocalAppData%
      - the version reported by the installer

    What it does NOT change, without a further step:
      - the icon baked *inside* the client executable, which is a compile-time resource. Explorer
        and the taskbar read that one. Pass -RcEdit with a copy of rcedit.exe to rewrite it in
        place, or rebuild from source with your own assets/icons/client.ico. Everything else about
        the identity is covered without it.

.PARAMETER Source
    Folder holding the published client — the contents of a portable zip, or a folder you have
    customized (swapped graphics, music, data).

.PARAMETER Name
    What players should see. Used for the shortcut, the installer title, and the install folder.

.PARAMETER Version
    Semver, e.g. 1.0.0. Velopack uses this to decide what counts as an update.

.PARAMETER Icon
    A .ico to use for the installer and shortcuts. Defaults to this project's client icon.

.PARAMETER MainExe
    File name (not path) of the executable to launch. Auto-detected when there is exactly one
    candidate in the source folder.

.PARAMETER Runtime
    win-x64 (default) or linux-x64. macOS cannot be packaged from another OS — see docs/building.md.

.PARAMETER RcEdit
    Optional path to rcedit.exe. When given, the icon inside the client executable is rewritten too,
    which is the one piece --icon cannot reach.

.EXAMPLE
    powershell -File tools/pack-client.ps1 -Source ./my-client -Name "Aethermoor" -Version 1.0.0 -Icon ./aethermoor.ico

.EXAMPLE
    # Linux build, from Windows or from Linux
    pwsh tools/pack-client.ps1 -Source ./my-client -Name "Aethermoor" -Version 1.0.0 -Runtime linux-x64

.NOTES
    THIS FILE MUST KEEP ITS UTF-8 BOM.

    Windows PowerShell 5.1 — the one that ships with Windows, and so the one most people will run
    this with — assumes the system ANSI codepage for a script with no BOM. The em-dashes and box
    rules in the comments below then decode as mojibake, and one of the bytes that falls out
    (0x94 -> U+201D) is a character PowerShell accepts as a string delimiter. The parser swallows
    the rest of the file, the script exits 0, and absolutely nothing happens. No error, no output.

    If you edit this in something that strips the BOM, put it back.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Version,
    [string]$Icon,
    [string]$MainExe,
    [ValidateSet('win-x64', 'linux-x64')][string]$Runtime = 'win-x64',
    [string]$Output = './installers',
    [string]$RcEdit
)

$ErrorActionPreference = 'Stop'

# $IsWindows only exists in PowerShell Core; Windows PowerShell 5.1 leaves it undefined and only
# ever runs on Windows, so a null reads as "yes".
$onWindows = $IsWindows -or ($null -eq $IsWindows)

# Progress goes to the output stream and failures to the error stream, rather than both going to
# the host. Write-Host draws straight to the console and vanishes under any redirection, which makes
# a script that uses it for diagnostics fail silently the moment it is run from CI, a pipe, or
# another shell — exactly when you most need to know why.
function Say($message) { Write-Output $message }

function Fail($message) {
    Write-Error $message -ErrorAction Continue
    exit 1
}

# ── Validate the input ────────────────────────────────────────────────────────

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    Fail "Source folder not found: $Source"
}
$Source = (Resolve-Path -LiteralPath $Source).Path

if ($Version -notmatch '^\d+\.\d+\.\d+') {
    Fail "Version must look like 1.0.0 — got '$Version'."
}

# Velopack derives the install folder and every artifact name from the pack id, so it has to be
# filesystem- and URL-safe. Spaces become hyphens, matching how the project's own slug is built.
$packId = ($Name -replace '\s+', '-') -replace '[^A-Za-z0-9\-_.]', ''
if (-not $packId) { Fail "Name '$Name' has no characters usable in an identifier." }

# ── Work out what to launch ───────────────────────────────────────────────────

if (-not $MainExe) {
    $candidates = if ($Runtime -eq 'win-x64') {
        Get-ChildItem -LiteralPath $Source -Filter *.exe -File |
            Where-Object { $_.Name -notmatch '^(createdump|vpk|Setup|Update)\.exe$' }
    } else {
        # On Linux the entry point has no extension and no execute bit to rely on after a zip
        # round-trip, so match files with no dot at all.
        Get-ChildItem -LiteralPath $Source -File | Where-Object { $_.Name -notmatch '\.' }
    }

    if ($candidates.Count -eq 1) {
        $MainExe = $candidates[0].Name
        Say "  main executable : $MainExe (detected)"
    } elseif ($candidates.Count -eq 0) {
        Fail "No executable found in $Source. Pass -MainExe with its file name."
    } else {
        $list = ($candidates | ForEach-Object { $_.Name }) -join ', '
        Fail "Several executables found ($list). Pass -MainExe to say which one to launch."
    }
} else {
    if (-not (Test-Path -LiteralPath (Join-Path $Source $MainExe))) {
        Fail "-MainExe '$MainExe' is not in $Source."
    }
    Say "  main executable : $MainExe"
}

# ── Icon ──────────────────────────────────────────────────────────────────────

if (-not $Icon) {
    # Two-argument Join-Path only: the multi-argument form is PowerShell 6+, and this has to run
    # under the Windows PowerShell 5.1 that ships with Windows.
    $Icon = Join-Path (Join-Path $PSScriptRoot '..') 'assets/icons/client.ico'
    Say "  icon            : project default (pass -Icon to use your own)"
}
if (-not (Test-Path -LiteralPath $Icon)) { Fail "Icon not found: $Icon" }
$Icon = (Resolve-Path -LiteralPath $Icon).Path

# ── vpk ───────────────────────────────────────────────────────────────────────

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Fail "Neither vpk nor dotnet is on PATH. Install the .NET SDK from https://dotnet.microsoft.com/download, then re-run."
    }
    Say "  vpk             : installing as a global tool..."
    dotnet tool install -g vpk --version 1.2.0 | Out-Null
    $shims = if ($onWindows) { Join-Path $env:USERPROFILE '.dotnet\tools' } else { Join-Path $HOME '.dotnet/tools' }
    $env:PATH = "$shims$([IO.Path]::PathSeparator)$env:PATH"
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        Fail "vpk installed but is not on PATH. Add $shims to PATH and re-run."
    }
}

# ── The executable's own icon, if rcedit was supplied ─────────────────────────

if ($RcEdit) {
    if ($Runtime -ne 'win-x64') { Fail "-RcEdit only applies to a win-x64 package." }
    if (-not (Test-Path -LiteralPath $RcEdit)) { Fail "rcedit not found: $RcEdit" }
    Say "  embedding icon into $MainExe"
    & $RcEdit (Join-Path $Source $MainExe) '--set-icon' $Icon
    if ($LASTEXITCODE -ne 0) { Fail "rcedit failed with exit code $LASTEXITCODE." }
}

# ── Pack ──────────────────────────────────────────────────────────────────────

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path -LiteralPath $Output).Path

$channel = if ($Runtime -eq 'win-x64') { 'win' } else { 'linux' }

# A build directive is only needed when the target OS differs from the host; packaging Linux on
# Linux uses a bare `vpk pack`, exactly as the project's own publish targets do.
$directive = @()
if ($Runtime -eq 'linux-x64' -and $onWindows) { $directive = @('[linux]') }

Say "`n  packing $packId $Version for $Runtime`n"
$vpkArgs = $directive + @(
    'pack',
    '--packId', $packId,
    '--packVersion', $Version,
    '--packTitle', $Name,
    '--packDir', $Source,
    '--mainExe', $MainExe,
    '--runtime', $Runtime,
    '--channel', $channel,
    '--icon', $Icon,
    '--outputDir', $Output
)

& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { Fail "vpk failed with exit code $LASTEXITCODE." }

# Velopack also writes its update feed (a .nupkg and a releases manifest) beside the artifacts.
# Those are only needed if you intend to serve automatic updates; delete them for a one-off build.
Say "`n  done — in $Output`n"
# Written out longhand rather than as a pipeline with an inline format string: Windows PowerShell
# 5.1's parser rejects a `-f` whose format specifier contains a comma inside a pipeline element,
# and 5.1 is what ships with Windows.
$wanted = @('.exe', '.zip', '.AppImage')
$artifacts = Get-ChildItem -LiteralPath $Output -File | Where-Object { $wanted -contains $_.Extension }
foreach ($artifact in $artifacts) {
    $megabytes = [math]::Round($artifact.Length / 1MB, 1)
    Say ('    ' + $artifact.Name.PadRight(52) + ' ' + $megabytes + ' MB')
}

Say ""
if (-not $RcEdit -and $Runtime -eq 'win-x64') {
    Say "  Note: the icon inside $MainExe is unchanged — Explorer and the taskbar still show"
    Say "  the original. Pass -RcEdit <path to rcedit.exe> to rewrite it, or rebuild from source."
    Say ""
}
