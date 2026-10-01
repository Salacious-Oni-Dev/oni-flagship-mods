# Build the mods on Windows: the PowerShell equivalent of build.sh, beside it.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 -OniGame "C:\...\OxygenNotIncluded"                    # every mod
#   powershell -ExecutionPolicy Bypass -File build.ps1 -OniGame "C:\...\OxygenNotIncluded" mod1-thermo-fluid  # one
#
# Needs the .NET SDK (winget install Microsoft.DotNet.SDK.8), the game's install folder (ONI_GAME
# or -OniGame), and oni-framework-api cloned beside this repository and built first (its build.ps1).
[CmdletBinding(PositionalBinding = $false)]
param(
  # The Oxygen Not Included install folder, the one that contains OxygenNotIncluded_Data.
  # Overrides the ONI_GAME environment variable.
  [string]$OniGame,
  # The mods to build, by folder name. All of them when none is named.
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Mods
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# ---------------------------------------------------------------- helpers (Windows PowerShell 5.1)

function Fail([string]$Message) {
  [Console]::Error.WriteLine("build.ps1: $Message")
  exit 1
}

# Run a native program, passing its stdout through and its stderr to stderr as plain text, and
# stop on a non-zero exit code. ErrorActionPreference is relaxed for the call itself because
# Windows PowerShell 5.1 turns a native program's stderr lines into errors when output is
# redirected, which would stop the build at the first compiler warning.
function Invoke-Native([string]$What, [string]$Exe, [string[]]$Arguments) {
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    & $Exe @Arguments 2>&1 | ForEach-Object {
      if ($_ -is [System.Management.Automation.ErrorRecord]) { [Console]::Error.WriteLine($_.ToString()) }
      else { $_ }
    }
    $code = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $saved
  }
  if ($code -ne 0) { Fail "$What failed (exit code $code)" }
}

# git output, or $null when git is missing or the command fails (not a checkout, a shallow
# clone, ...). The version stamp degrades to literals rather than stopping the build.
function Get-GitOutput([string[]]$GitArgs) {
  if (-not (Get-Command git -CommandType Application -ErrorAction SilentlyContinue)) { return $null }
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $out = & git @GitArgs 2>$null
    $code = $LASTEXITCODE
  } catch {
    $code = 1
  } finally {
    $ErrorActionPreference = $saved
  }
  if ($code -ne 0) { return $null }
  return ((@($out) | ForEach-Object { "$_" }) -join "`n").Trim()
}

# MAJOR.MINOR.REV+SHA[*]: every non-comment line of the VERSION file with all whitespace removed,
# the commit count and short commit of HEAD, and '*' if the working tree has any change.
function Get-VersionParts([string]$VersionFile) {
  $base = ((Get-Content -LiteralPath $VersionFile | Where-Object { $_ -notmatch '^#' }) -join '') -replace '\s', ''
  $rev = Get-GitOutput @('rev-list', '--count', 'HEAD')
  if (-not $rev) { $rev = '0' }
  $sha = Get-GitOutput @('rev-parse', '--short=7', 'HEAD')
  if (-not $sha) { $sha = 'unknown' }
  $dirty = ''
  if (Get-GitOutput @('status', '--porcelain')) { $dirty = '*' }
  return @{ Base = $base; Rev = $rev; Sha = $sha; Dirty = $dirty }
}

# Write text exactly: UTF-8 without a byte order mark, and only the line endings in $Text.
function Write-Exact([string]$Path, [string]$Text) {
  $full = Join-Path (Get-Location).ProviderPath $Path
  [System.IO.File]::WriteAllText($full, $Text, (New-Object System.Text.UTF8Encoding $false))
}
# The game folder from -OniGame or ONI_GAME, checked to be the one holding OxygenNotIncluded_Data.
function Get-OniGame([string]$Given) {
  $game = $Given
  if (-not $game) { $game = $env:ONI_GAME }
  if (-not $game) {
    Fail ("ONI_GAME is not set. Pass -OniGame or set `$env:ONI_GAME to your Oxygen Not Included " +
          "install folder, the one that contains OxygenNotIncluded_Data (for Steam usually " +
          "C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded).")
  }
  $game = $game.TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $game 'OxygenNotIncluded_Data') -PathType Container)) {
    Fail ("'$game' has no OxygenNotIncluded_Data folder. ONI_GAME must be the Oxygen Not Included " +
          "install folder, the one that contains OxygenNotIncluded_Data.")
  }
  return $game
}

# The .NET SDK, from PATH or %USERPROFILE%\.dotnet as build.sh allows ~/.dotnet.
function Find-DotnetSdk {
  $env:PATH = (Join-Path $HOME '.dotnet') + ';' + $env:PATH
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  $d = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $d) { Fail "dotnet was not found. Install the .NET SDK:  winget install Microsoft.DotNet.SDK.8  and open a new PowerShell window." }
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  $sdks = $null
  try { $sdks = & $d.Source --list-sdks 2>$null } finally { $ErrorActionPreference = $saved }
  if (-not $sdks) { Fail "$($d.Source) has no SDK installed (only a runtime). Install the .NET SDK:  winget install Microsoft.DotNet.SDK.8" }
  return $d.Source
}

# Lines of *.cs files that call Debug.LogError, skipping comment lines, as grep -n prints them.
function Find-LogErrorCalls([System.IO.FileInfo[]]$Files) {
  if (-not $Files) { return @() }
  return @($Files | Select-String -Pattern 'Debug\.LogError' -CaseSensitive |
           Where-Object { $_.Line -notmatch '^\s*(//|\*|/\*)' } |
           ForEach-Object { "$($_.Path):$($_.LineNumber):$($_.Line)" })
}

# ---------------------------------------------------------------- build

$savedPath = $env:PATH
$savedGame = $env:ONI_GAME
$savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$repo = $PSScriptRoot
try {
  $env:ONI_GAME = Get-OniGame $OniGame
  $dotnet = Find-DotnetSdk

  if (-not $Mods -or $Mods.Count -eq 0) { $Mods = @('mod1-thermo-fluid', 'mod2-matter-environment') }

  # The mods reference the framework's project from a checkout beside this one (see
  # Directory.Build.props), and that project only compiles after its own build script has
  # written Version.generated.cs.
  $fw = Join-Path $repo '..\oni-framework-api\OniFramework'
  if (-not (Test-Path -LiteralPath (Join-Path $fw 'OniFramework.csproj') -PathType Leaf)) {
    Fail "oni-framework-api was not found beside this repository ($fw). Clone it next to this one."
  }
  if (-not (Test-Path -LiteralPath (Join-Path $fw 'Version.generated.cs') -PathType Leaf)) {
    Fail "oni-framework-api has not been built yet. Run its build.ps1 first, then this one."
  }

  # GUARD: A FLAGSHIP MOD MAY NOT CALL Debug.LogError (see build.sh for why). Scans every mod,
  # not just the ones being built. Comment lines are exempt.
  $files = @(Get-ChildItem -Path $repo -Directory -Filter 'mod*' | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter '*.cs' -File })
  $bad = @(Find-LogErrorCalls $files)
  if ($bad.Count -gt 0) {
    [Console]::Error.WriteLine('build.ps1: FAILED -- a flagship mod may not call Debug.LogError:')
    $bad | ForEach-Object { [Console]::Error.WriteLine($_) }
    [Console]::Error.WriteLine('build.ps1: use Mod1Log.Error (warning level, ERROR in the text), or throw if it really')
    [Console]::Error.WriteLine('build.ps1: is a crash. See mod1-thermo-fluid/Mod1Log.cs.')
    exit 1
  }

  foreach ($mod in $Mods) {
    Write-Output "==> $mod"
    $dir = Join-Path $repo $mod
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { Fail "no mod folder '$mod' in $repo" }
    $csproj = @(Get-ChildItem -LiteralPath $dir -Filter '*.csproj' -File)
    if ($csproj.Count -ne 1) { Fail "expected one .csproj in $dir, found $($csproj.Count)" }
    Invoke-Native "dotnet build ($mod)" $dotnet @('build', $csproj[0].FullName, '-c', 'Release', '--nologo', '-v', 'minimal')
  }
} finally {
  $env:PATH = $savedPath
  $env:ONI_GAME = $savedGame
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = $savedTelemetry
}
