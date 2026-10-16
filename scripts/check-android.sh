#!/usr/bin/env bash
# The engine packages in an Android app: a fresh `dotnet new android` that
# references RippleFx.Engine and an engine package from a local feed, built
# for both ABIs the engine ships, and its APK opened to find the library under
# each. Nothing is run on a device — the engine's own release runs its suites
# on an emulator — so what this proves is NuGet's runtimes/android-*/native/
# path reaching the APK, the one step that is .NET's and not the engine's.
#
#   scripts/check-android.sh <folder of .nupkg> [python|no-python]
#
# Needs the .NET android workload, an Android SDK (ANDROID_HOME) and a JDK.
set -euo pipefail

feed="$(cd "$1" && pwd)"
variant="${2:-python}"
package=RippleFx.Engine.Native
[ "$variant" = no-python ] && package=RippleFx.Engine.Native.NoPython
version="$(sed -n 's:.*<RippleVersion>\(.*\)</RippleVersion>.*:\1:p' "$(dirname "$0")/../Directory.Build.props")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/cache" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
die() { echo "check-android: $*" >&2; exit 1; }

dotnet new android -o "$work/app" --force >/dev/null
cat > "$work/app/nuget.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat > "$work/app/Directory.Build.props" <<EOF
<Project>
  <PropertyGroup>
    <RuntimeIdentifiers>android-arm64;android-x64</RuntimeIdentifiers>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RippleFx.Engine" Version="$version" />
    <PackageReference Include="$package" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat > "$work/app/MainActivity.cs" <<'EOF'
namespace app;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Title = "Ripple Effect " + RippleFx.Engine.RippleEngine.Version;
    }
}
EOF

dotnet build "$work/app" -c Release 2>&1 | tail -3
apk="$(find "$work/app/bin/Release" -name '*-Signed.apk' | head -1)"
[ -n "$apk" ] || die "no APK was built"
# Byte for byte the package's Android build, not merely a file of that name:
# NuGet's RID graph falls back from android-arm64 to linux-arm64, so a package
# that lost its Android libraries would put the glibc build in the APK, and it
# would fail only when a phone loaded it.
nupkg="$feed/$package.$version.nupkg"
digest() { sha256sum | cut -d' ' -f1; }
for pair in arm64-v8a:android-arm64 x86_64:android-x64; do
  abi="${pair%%:*}" rid="${pair##*:}"
  shipped="$( (unzip -p "$nupkg" "runtimes/$rid/native/libripple_effect_engine.so" 2>/dev/null || true) | digest)"
  [ "$shipped" != "$(digest < /dev/null)" ] || die "$package has no runtimes/$rid/native/libripple_effect_engine.so"
  packed="$( (unzip -p "$apk" "lib/$abi/libripple_effect_engine.so" 2>/dev/null || true) | digest)"
  [ "$packed" = "$shipped" ] || die "$package: the APK's lib/$abi/libripple_effect_engine.so is not the package's $rid build"
  echo "check-android: $package: lib/$abi/libripple_effect_engine.so is the $rid build"
done
