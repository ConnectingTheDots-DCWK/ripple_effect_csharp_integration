#!/usr/bin/env bash
# The engine packages in a .NET for iOS app: a fresh `dotnet new ios` that
# references RippleFx.Engine and an engine package from a local feed, built for
# the device (ios-arm64) and the simulator (iossimulator-arm64). The engine
# ships no dylib for iOS, so the package links its static library into the app's
# single executable; this proves the package's buildTransitive .targets does
# that — the NativeReference that force-loads the archive, the ReferenceNativeSymbol
# list that keeps and exports every C entry point, and the managed resolver that
# points the P/Invokes at the main image. The device build is the link check (a
# free runner cannot sign, so it is built unsigned and never run, like
# check-android.sh runs nothing on a phone); the simulator build is played
# through to the end of a fixture when a simulator can be booted.
#
#   scripts/check-ios.sh <folder of .nupkg> <bundle folder> [python|no-python]
#
# Needs the .NET ios workload, Xcode and its simulators.
set -euo pipefail

feed="$(cd "$1" && pwd)"
bundle="$(cd "$2" && pwd)"
variant="${3:-python}"
package=RippleFx.Engine.Native
[ "$variant" = no-python ] && package=RippleFx.Engine.Native.NoPython
want_python=True
[ "$variant" = no-python ] && want_python=False
version="$(sed -n 's:.*<RippleVersion>\(.*\)</RippleVersion>.*:\1:p' "$(dirname "$0")/../Directory.Build.props")"
work="$(mktemp -d)"
[ -n "${CHECK_IOS_KEEP:-}" ] || trap 'rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/cache" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
die() { echo "check-ios: $*" >&2; exit 1; }
lib=libripple_effect_engine

app="$work/app"
dotnet new ios -o "$app" --force >/dev/null
cat > "$app/nuget.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat > "$app/Directory.Build.props" <<EOF
<Project>
  <ItemGroup>
    <PackageReference Include="RippleFx.Engine" Version="$version" />
    <PackageReference Include="$package" Version="$version" />
  </ItemGroup>
  <ItemGroup>
    <BundleResource Include="fixture/**/*">
      <Link>fixture/%(RecursiveDir)%(Filename)%(Extension)</Link>
    </BundleResource>
  </ItemGroup>
</Project>
EOF
# The fixture, bundled into the app: a simulator reads it from the app's own
# resources, not from a folder on the host (a sandboxed app has no path to one,
# and a host path under a privacy-protected folder hangs the simulator anyway).
mkdir -p "$app/fixture"
cp -R "$bundle/." "$app/fixture/"
cat > "$app/AppDelegate.cs" <<'EOF'
using System;
using System.IO;
using RippleFx.Engine;

namespace app;

[Register ("AppDelegate")]
public class AppDelegate : UIApplicationDelegate {
	public override bool FinishedLaunching (UIApplication application, NSDictionary? launchOptions)
	{
		var outFile = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.Personal), "ripple-ios.txt");
		try {
			var dir = Path.Combine (NSBundle.MainBundle.BundlePath, "fixture");
			using var campaign = Campaign.OpenDirectory (dir);
			using var session = Session.Create (campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
			var turns = 0;
			for (var batch = session.Start (); ; turns++) {
				var turn = batch[batch.Count - 1];
				if (turn.IsOver)
					break;
				batch = turn switch {
					Effect.Show => session.Choose (0),
					Effect.Hold => session.Elapsed (),
					_ => session.Answer (null),
				};
			}
			var line = $"PLAYED version={RippleEngine.Version} python={RippleEngine.HasPython} turns={turns} outstanding={RippleEngine.Outstanding}";
			File.WriteAllText (outFile, line + "\n");
			Console.WriteLine ("RIPPLE_IOS " + line);
		} catch (Exception e) {
			File.WriteAllText (outFile, "FAILED " + e + "\n");
			Console.WriteLine ("RIPPLE_IOS FAILED " + e);
		}
		return true;
	}

	public override UISceneConfiguration GetConfiguration (UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options)
		=> new UISceneConfiguration ("Default Configuration", connectingSceneSession.Role);

	public override void DidDiscardSceneSessions (UIApplication application, NSSet<UISceneSession> sceneSessions) { }
}
EOF

# Asserts the engine is linked statically into the app's executable: its C ABI
# is present, and there is no dylib load command for it (there is no dylib). The
# tool output is captured whole, never piped into `grep -q`: a short-circuiting
# grep closes the pipe, the producer dies with SIGPIPE, and `pipefail` turns
# that into a failed build.
assert_static() { # <app bundle> <rid>
  local exe="$1/app"
  [ -f "$exe" ] || die "$2: no executable at $exe"
  case "$(lipo -info "$exe" 2>/dev/null)" in *arm64*) ;; *) die "$2: the executable is not arm64" ;; esac
  case "$(otool -L "$exe" 2>/dev/null)" in *"$lib"*) die "$2: the app has a dylib load command for $lib; it must be static" ;; esac
  local syms; syms="$(nm "$exe" 2>/dev/null || true)"
  case "$syms" in
    *" T _ripple_engine_contract"*) ;;
    *) die "$2: the engine's C ABI (_ripple_engine_contract) is not linked into the app" ;;
  esac
  echo "check-ios: $package: $2 links the engine statically"
}

for rid in ios-arm64 iossimulator-arm64; do
  echo "check-ios: building $package for $rid"
  # The device cannot be signed on a free runner; the link is what this proves.
  sign=()
  [ "$rid" = ios-arm64 ] && sign=(-p:EnableCodeSigning=false)
  dotnet build "$app" -c Release -r "$rid" ${sign[@]+"${sign[@]}"} >"$work/build-$rid.log" 2>&1 \
    || { tail -40 "$work/build-$rid.log"; die "$package: the $rid build failed"; }
  assert_static "$app/bin/Release/net"*"-ios/$rid/app.app" "$rid"
done

# Play the fixture on a simulator, when one can be booted.
udid="$(xcrun simctl list devices available 2>/dev/null | grep -oE 'iPhone[^(]*\(([-0-9A-F]{36})\)' | grep -oE '[-0-9A-F]{36}' | head -1 || true)"
if [ -z "$udid" ]; then
  echo "check-ios: no simulator available; built device and simulator, ran neither"
  exit 0
fi
echo "check-ios: booting simulator $udid"
xcrun simctl boot "$udid" >/dev/null 2>&1 || true
xcrun simctl bootstatus "$udid" -b >/dev/null 2>&1 || true
appbundle="$(echo "$app/bin/Release/net"*"-ios/iossimulator-arm64/app.app")"
bundleid="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$appbundle/Info.plist")"
xcrun simctl terminate "$udid" "$bundleid" >/dev/null 2>&1 || true
xcrun simctl uninstall "$udid" "$bundleid" >/dev/null 2>&1 || true
xcrun simctl install "$udid" "$appbundle"
xcrun simctl launch "$udid" "$bundleid" >/dev/null 2>&1
container=""
for _ in $(seq 30); do
  container="$(xcrun simctl get_app_container "$udid" "$bundleid" data 2>/dev/null || true)"
  [ -n "$container" ] && [ -f "$container/Documents/ripple-ios.txt" ] && break
  sleep 1
done
result="$(cat "$container/Documents/ripple-ios.txt" 2>/dev/null || true)"
[ -n "$result" ] || die "$package: the app wrote no result on the simulator"
case "$result" in
  "PLAYED "*"python=$want_python "*) echo "check-ios: $package: simulator $result" ;;
  FAILED*) die "$package: the app failed on the simulator: $result" ;;
  *) die "$package: the app ran but reported the wrong engine: $result" ;;
esac
grep -q "outstanding=0" <<< "$result" || die "$package: the engine left handles outstanding: $result"
