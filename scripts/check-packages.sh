#!/usr/bin/env bash
# The packages as a consumer meets them: a fresh console that references
# RippleFx.Engine and one engine package from a local feed, with a package
# cache of its own, plays a fixture to its end and checks it loaded the
# variant it asked for — for each variant. A project that references no
# engine must fail at its first call by naming the two packages, and one that
# references both must fail to build, saying why. The release path is
# otherwise only exercised at release time.
#
#   scripts/check-packages.sh <folder of .nupkg> <bundle folder>
set -euo pipefail

feed="$(cd "$1" && pwd)"
bundle="$(cd "$2" && pwd)"
version="$(sed -n 's:.*<RippleVersion>\(.*\)</RippleVersion>.*:\1:p' "$(dirname "$0")/../Directory.Build.props")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/cache" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
die() { echo "check-packages: $*" >&2; exit 1; }

cat > "$work/nuget.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

consumer() { # <name> <engine package>...
  local dir="$work/$1"; shift
  mkdir -p "$dir"
  cp "$work/nuget.config" "$dir/"
  local refs=""
  for package in RippleFx.Engine "$@"; do
    refs+="    <PackageReference Include=\"$package\" Version=\"$version\" />"$'\n'
  done
  cat > "$dir/consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
$refs  </ItemGroup>
</Project>
EOF
  cat > "$dir/Program.cs" <<'EOF'
using System;
using RippleFx.Engine;

using var campaign = Campaign.OpenDirectory(args[0]);
using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
var turns = 0;
for (var batch = session.Start(); ; turns++)
{
    var turn = batch[batch.Count - 1];
    if (turn.IsOver) break;
    batch = turn switch
    {
        Effect.Show => session.Choose(0),
        Effect.Hold => session.Elapsed(),
        _ => session.Answer(null),
    };
}
Console.WriteLine($"{RippleEngine.Version} python={RippleEngine.HasPython} turns={turns}");
return RippleEngine.Outstanding == 0 && turns > 1 ? 0 : 1;
EOF
  echo "$dir"
}

for variant in python no-python; do
  package=RippleFx.Engine.Native
  [ "$variant" = no-python ] && package=RippleFx.Engine.Native.NoPython
  dir="$(consumer "$variant" "$package")"
  said="$(dotnet run --project "$dir" -- "$bundle")" || die "$package: the consumer failed: $said"
  want=True
  [ "$variant" = no-python ] && want=False
  case "$said" in
    *"python=$want "*) echo "check-packages: $package: $said" ;;
    *) die "$package loaded the wrong engine: $said" ;;
  esac
done

dir="$(consumer neither)"
if said="$(dotnet run --project "$dir" -- "$bundle" 2>&1)"; then
  die "a project referencing no engine ran: $said"
fi
grep -q "EngineNotFoundException" <<< "$said" || die "a project referencing no engine failed, but not by naming the packages:
$said"
echo "check-packages: referencing no engine says which to reference"

dir="$(consumer both RippleFx.Engine.Native RippleFx.Engine.Native.NoPython)"
if built="$(dotnet build "$dir" 2>&1)"; then
  die "a project referencing both engines built"
fi
grep -q "are both referenced" <<< "$built" || die "a project referencing both engines failed, but not for that reason:
$built"
echo "check-packages: referencing both engines is refused"
