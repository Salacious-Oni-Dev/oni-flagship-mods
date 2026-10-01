#!/usr/bin/env bash
# Build the mods. Needs the .NET SDK (on PATH or in ~/.dotnet), ONI_GAME set to the game's
# install folder, and oni-framework-api checked out and built beside this repository.
#
#   ./build.sh                    # build every mod below
#   ./build.sh mod1-thermo-fluid  # build one
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

mods=("$@")
if [ ${#mods[@]} -eq 0 ]; then
  mods=(mod1-thermo-fluid mod2-matter-environment)
fi

# GUARD: A FLAGSHIP MOD MAY NOT CALL Debug.LogError.
#
# ONI's KCrashReporter treats LogType.Error exactly as it treats an unhandled exception. From
# `KCrashReporter.HandleLog`, `if (!(errorScreen == null) || (type != LogType.Exception && type
# != 0) || ...) return;` -- and LogType.Error IS 0, so an error does NOT return. It falls through
# to `Pause(playSound: true, isCrashed: true)` and `ShowDialog`, which walks the live stack,
# UNCHECKS every mod it finds there, and -- since `terminateOnError` defaults true and a player's
# install has no Dev-distribution mod to relax it -- offers no Continue button at all.
#
# This is a check and not only a comment because a rule stated at one call site does not reach
# the others. Route everything through Mod1Log (mod1-thermo-fluid/Mod1Log.cs)
# instead; when a crash really is a crash, throw, because an exception carries its own stack and
# blames the mod actually at fault.
#
# SCANS EVERY MOD, not just the ones being built. Comment lines are exempt so the rule can be
# explained where it is enforced.
BAD=$(grep -rn 'Debug\.LogError' "$REPO"/mod*/ --include='*.cs' 2>/dev/null \
      | grep -vE ':[0-9]+:[[:space:]]*(//|\*|/\*)' || true)
if [ -n "$BAD" ]; then
  echo "build.sh: FAILED -- a flagship mod may not call Debug.LogError:" >&2
  printf '%s\n' "$BAD" >&2
  echo "build.sh: use Mod1Log.Error (warning level, ERROR in the text), or throw if it really" >&2
  echo "build.sh: is a crash. See mod1-thermo-fluid/Mod1Log.cs." >&2
  exit 1
fi

for mod in "${mods[@]}"; do
  echo "==> $mod"
  csproj=$(find "$REPO/$mod" -maxdepth 1 -iname "*.csproj")
  dotnet build "$csproj" -c Release --nologo -v minimal
done
