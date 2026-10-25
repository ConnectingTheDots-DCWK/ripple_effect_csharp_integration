# ripple_effect_csharp_integration

The C# bindings of the Ripple Effect engine: P/Invoke over the engine's C ABI,
generated from its header, and a C# layer over it, published to nuget.org
with the engine itself in two native packages. **The engine is not built
here.** It is released from the private `ripple_effect` repository as
`engine-v<version>` in the public `ripple_engine_releases`, and this
repository pins one (`engine.json`). Why the C ABI is shaped as it is lives
in that repository, as ADR *the-engine-speaks-c* and
`docs/internals/integrating-a-host.md` §11; neither is repeated here. The C/C++
binding (`ripple_effect_c_cpp_integration`) is the sibling this one follows,
and a Unity package will be built on this one.

| | |
| --- | --- |
| `src/RippleFx.Engine/` | The bindings, `netstandard2.1;net8.0`, C# 9: `Campaign`, `Session`, `Effect`, `Value`, the records, the exceptions, `Mixer`, `ISpeaker`/`IVoice`, `SoundscapeDriver`, `Player`. `Internal/` reads the engine's structs into records (`From`) and lays arguments out for it (`Scratch`, `Arena`). |
| `src/RippleFx.Engine/Interop/Native.g.cs` | **Generated**: the header as P/Invoke, by ClangSharp, C names kept. Never edited; `scripts/generate.sh` rewrites it. |
| `packaging/` | `RippleFx.Engine.Native` and `.NoPython` — one project, `RippleVariant` picking the package — and the `buildTransitive` guard against referencing both. |
| `tool/Engine/` | Fetches and verifies the pinned engine (`fetch`), or lays every target out for the native packages (`stage`); builds `RIPPLE_ENGINE_TREE` with cargo. The twin of the C/C++ binding's `RippleEngine.cmake`. |
| `build/Engine.targets` | The engine for a project here that runs it: fetched before the build, the library copied beside the program. Imported when a project sets `RippleNeedsEngine`. |
| `engine.json`, `engine-signing.pub`, `scripts/pin-engine.sh` | The pin and its trust anchor, the same files as the C/C++ binding's. |
| `scripts/generate.sh` | The interop and `tests/RippleFx.Engine.Tests/layout.json`, from the pinned header. |
| `scripts/check-packages.sh`, `scripts/check-android.sh`, `scripts/check-ios.sh` | The packages as a consumer meets them, from a local feed: a console, an Android APK, and an iOS app (device and simulator, the static engine linked in). |
| `tests/RippleFx.Engine.Tests/` | NUnit: conformance through `Session` and `Player`, the driver, a countdown on the player's clock, what a story reads of the world, values, the layout. |
| `examples/ConsolePlayer/` | A terminal player over any bundle folder. |
| `docs/speaker.md`, `docs/consuming.md` | The speaker contract, and how a project takes the packages. |

## Environment

The .NET 10 SDK (`global.json`), with the .NET 8 runtime for the tests. On
the maintainer's Linux machine it is user-local, under `~/.dotnet` from
`dotnet-install.sh`, and not on the system `PATH`: `export
DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`. `scripts/generate.sh` also needs
`clang` and `python3`; `check-android.sh` the `android` workload, an Android
SDK (`ANDROID_HOME`) and a JDK. `cargo`, only with `RIPPLE_ENGINE_TREE`.

Each of those keeps its own pin: the SDK is `global.json`'s, which CI's
`setup-dotnet` reads too, and ClangSharp is `.config/dotnet-tools.json`'s.
What has no such home — TruffleHog, pre-commit, jq, and the Python they and
`generate.sh` run on — is pinned in `mise.toml`; `mise install` puts it on a
machine, and `mise run doctor` (`scripts/doctor.sh`) checks the whole list,
SDK and workloads included, and says how to fix what is missing.

The hooks: `pre-commit install --hook-type pre-commit --hook-type pre-push`.
A commit pays for the secret scan (TruffleHog; a clone needs the binary),
codespell and a whitespace check; a push for `dotnet format` with the
analyzers and the suite in both variants.

## Verifying a change

```sh
dotnet build src/RippleFx.Engine -c Release                 # both frameworks
dotnet test                                                 # python
dotnet test -p:RippleVariant=no-python
RIPPLE_ENGINE_TREE=<a ripple_effect checkout> dotnet test   # an unreleased engine
dotnet format RippleFx.sln --verify-no-changes --exclude src/RippleFx.Engine/Interop/
# the packages, as CI's `packages` job makes and checks them:
for v in python no-python; do
  dotnet run --project tool/Engine -c Release -- stage --variant $v --out out/stage/$v
  dotnet pack packaging/RippleFx.Engine.Native -c Release -p:RippleVariant=$v -o out/packages
done
dotnet pack src/RippleFx.Engine -c Release -o out/packages
bash scripts/check-packages.sh out/packages out/engine/conformance/fixtures/the-choir/bundle
bash scripts/check-android.sh out/packages python
# an iOS app, device and simulator, from a local feed (needs Xcode and its
# simulators; plays the fixture on a booted simulator when one can be had):
bash scripts/check-ios.sh out/packages out/engine/conformance/fixtures/the-choir/bundle python
bash scripts/check-ios.sh out/packages out/engine/conformance/fixtures/the-choir/bundle no-python
```

The suite plays every conformance fixture of the pinned release through
`Session` and through `Player`, in the configured variant: 33 with Python,
27 without, skipping 6 — counts asserted, and the engine asked, not the
configuration. The driver does what the engine's own reference driver
(`ripple-runtime/src/conformance/runner.rs`) does with what `FORMAT.md` has
not caught up with yet: the input's `now`, `permit` and `standIns`, told
before `Start` and again after a resume; the `countdown`, `expire` and
`permit` moves; a passage's `timer`, and the `permit` and `notice` events.
It checks nothing the engine handed out is left over, holds the driver to
the scheduler on a recording speaker and a numeric clock, runs a countdown on
the player's clock (`Countdowns`), carries a whole position fix, a
stand-in, the refusals and a form's labels in two languages (`Senses`),
compares .NET's layout of all 75 structs with clang's, holds a disposed
handle to refusing a call, and holds the iOS native-symbol list to the
P/Invokes of Native.g.cs (`IosSymbols`). Ninety-eight tests in all. **A test
has to be able to fail**, and each of these was seen red: a passage's title
written into its text (every fixture, both hosts); a field's `optional`
dropped (the-chosen, both hosts); the driver's latency offset removed (the
late-voice test); a countdown measured from `Update` rather than from when
it was due, and not kept across `SetLocale` (`Countdowns`); a fix's latitude
and longitude swapped, and the player's `Now` never handed over (`Senses`);
two fields of a struct swapped (the layout test, naming
`offsetof(RippleEntry, id)`); the disposed guard removed from `Session`; a
symbol dropped from the iOS list (`IosSymbols`); the Android libraries left
out of a package (`check-android.sh`), and the iOS ones left out
(`check-ios.sh`, where the device link fails naming the missing archive).

## The decisions

- **The interop is generated, and checked against its header twice.**
  ClangSharp writes `Native.g.cs` from the pinned archive's `ripple.h`; CI
  regenerates it and fails on any difference, and the layout test holds
  every struct's size and offsets to what clang made of the same header, on
  every platform CI runs. C names are kept, so a line is found by grepping
  the header. `bool` fields are `byte`, and an optional is a `present` byte
  and a value.
- **Blittable only, and nothing calls back.** The ABI has no function
  pointers, so every `DllImport` takes pointers and blittable structs: no
  marshalling stub, which is what lets NativeAOT and IL2CPP take it as it
  is. Strings go in as UTF-8 written into `Scratch`, unmanaged memory freed
  when the call is over; nothing is pinned.
- **The layer copies out of every batch**, as `ripple.hpp` does: an `Effect`
  is a record of managed strings, and the engine's batch is freed before a
  verb returns. `RippleEngine.Outstanding` is zero whenever no call is in
  flight, and a test holds it there.
- **C# 9 and `netstandard2.1`**, because that is what Unity compiles and
  loads. No `readonly record struct`, `required`, file-scoped namespaces or
  generic math in `src/`; records with `init` need the `IsExternalInit`
  polyfill in `Internal/`. The tool and the tests are net8.0.
- **A disposed handle throws `ObjectDisposedException`.** `SafeHandle`
  still answers its old pointer after `Dispose`, so each handle's `Raw`
  checks `IsClosed` before the engine sees it; a forgotten `Dispose` is the
  finalizer's. A session made from a campaign outlives it, as in the engine.
- **Exceptions, typed by the engine's code.** One `RippleException` per kind
  of failure, the engine's `RippleError` copied into `Failure`. The C++
  layer's no-exceptions mode has no C# counterpart worth its weight.
- **Interfaces for the speaker**, not base classes as in C++, so a
  `MonoBehaviour` or a Godot `Node` can be one. `Resolves` is required: an
  interface default is one more thing for IL2CPP to get right.
- **Verbs read as what the reader did.** `Choose(index)` and `Leave()` rather
  than C++'s `choose(std::optional)`; `Answer(null)` is leaving a form.
- **The effects are nested** — `Effect.Show`, `Effect.Trace` — so a host that
  also says `using System.Diagnostics;` has no `Trace` to collide with.
- **Two engine packages, one managed one.** The managed package names no
  engine, so a consumer picks the variant by which engine package it adds,
  and the `buildTransitive` targets refuse both. A project with neither
  builds, and `EnsureLoaded` throws `EngineNotFoundException` naming them.
- **The driver and the player own no clock and no thread**, as in C++: a
  `Func<ulong>` of milliseconds, `Clocks.Steady()` unless a test passes a
  number.
- **The player runs a countdown; the session never does.** A countdown is
  two waits on the host's clock (`campaign-host-api.md` §4b), so `Player`
  keeps them beside the hold: measured from when the begin was due rather
  than from when `Update` came round, kept across `SetLocale` — the same
  visit — and dropped by any verb that moves the story.
  `PlayerHandlers.Countdowns = false` runs none, which is the conformance
  driver's setting (a fixture's moves say when) and a reader's who asked for
  no time limits.
- **What a story reads of the world is pushed, and the wall clock is a
  handler.** `PlayerHandlers.Now` is a `Func<Moment?>` the player hands to
  `SetNow` before every verb; the millisecond clock above stays the one that
  times holds and countdowns, because a story's *now* is the reader's wall
  clock and a timer's is monotonic. A permission kind is `PermissionKind`
  over the C ABI's `uint32_t`, open for appending: a kind this binding does
  not name arrives as its number.
- **New fields arrive as `init` properties** where a positional record
  already had a constructor — `PromptField`'s `Label`, `Placeholder`,
  `Optional` and `Options` — so a 1.5 caller's `new PromptField(name, type,
  default)` still compiles. `Effect.Notice` and `Effect.Permit` are flat, as
  `Ended` and `Failed` are: a nested `Notice` record could not hold a
  property named `Notice`.
- **Publishing is nuget.org's Trusted Publishing**: the release job's OIDC
  token is exchanged by `NuGet/login` for a key that lives an hour; no key is
  stored. The engines are pushed before the bindings.

## Traps

- **`CopyToOutputDirectory="PreserveNewest"` keeps the wrong variant.** The
  two engines' libraries carry their archives' mtimes, a second apart, so
  after switching `RippleVariant` the older one is never copied over the
  newer. `build/Engine.targets` copies `Always`.
- **.NET's RID graph falls back from `android-*` to `linux-*`.** A native
  package that lost its Android libraries still builds an APK — with the
  glibc build inside, which a phone refuses at load. `check-android.sh`
  compares the APK's library with the package's Android one byte for byte,
  never by name.
- **A mutation inside a struct's padding proves nothing.** Widening a byte
  that sits before eight-aligned padding changes no offset and no size, and
  the layout test rightly passes; reorder two fields to see it fail.
- **A distro's SDK names its own RID** (`ubuntu.24.04-x64`), and the engine
  ships for portable ones. `build/Engine.targets` builds the RID from the OS
  and the architecture, not from `NETCoreSdkRuntimeIdentifier`.
- **NuGet appends the recursive directory to a folder `PackagePath`.** The
  runtimes are packed with `PackagePath="runtimes"`; `runtimes/%(RecursiveDir)`
  doubles every path.
- **`NuGet/login`'s `user` is the person, not the organisation.** It is
  the nuget.org account that created the trusted-publishing policy
  (`Wilielmus`), even though the policy and the packages belong to the
  `ConnectingTheDots` organisation; naming the organisation fails the token
  exchange. Set the first release's run straight.
- **`printf`, not `yes`, into a pipe on CI.** GitHub's bash runs with
  `pipefail`, and `yes` ends by SIGPIPE.
- **.NET in the browser cannot link the engine.** The `wasm-tools` workload
  brings its own Emscripten (3.1.56 in .NET 10), whose `wasm-opt` rejects
  `--enable-bulk-memory-opt` in the engine's objects; the engine's floor is
  4.0. Measured, not assumed — and `docs/consuming.md` says it.
- **A tree build replaces the pinned one in `out/engine/`** and drops the
  digest marker, so the next pinned fetch unpacks the release again rather
  than trusting it.
- **iOS is static-only, and linking the `.a` is not enough.** The engine ships
  no iOS dylib, so the native package's `buildTransitive` targets link
  `libripple_effect_engine.a` into the app with a `<NativeReference>`
  (`ForceLoad`, plus the frameworks `native-static-libs.txt` names — the Python
  build wants CoreFoundation and iconv). But a force-loaded archive whose
  symbols nothing references is dead-stripped, and a dynamic P/Invoke name is
  not referenced, so every C entry point is named in `<ReferenceNativeSymbol>`
  to be kept and exported, and `NativeLibraryResolver` (net8.0 only;
  `netstandard2.1`, which Unity compiles, has no `NativeLibrary`) points the
  `ripple_effect_engine` name at the main image. Desktop keeps loading the
  dylib. `scripts/check-ios.sh` builds for device and simulator and plays a
  fixture; the `IosSymbols` test holds the symbol list to Native.g.cs.
- **`RuntimeIdentifiers` (plural) that names a simulator makes a device build
  target the simulator.** The iOS SDK sets `_SdkIsSimulator` from *either*
  `RuntimeIdentifier` or `RuntimeIdentifiers` containing `simulator`, so a
  project listing both and built with `-r ios-arm64` compiles for the simulator
  while linking the device `.a`, and `ld` fails on the architecture mismatch.
  Build one runtime identifier at a time with `-r`, and do not pre-declare
  both.
- **A device build is unsigned on a free runner.** `-p:EnableCodeSigning=false`
  gets the link — which is what `check-ios.sh` proves on the device, running
  nothing, as `check-android.sh` runs nothing on a phone.
- **The .NET iOS workload needs its matching Xcode, and a hosted runner lags.**
  `dotnet workload install ios` on a given SDK band pulls the workload built for
  the current Xcode (27.0.10722 needs Xcode 27), and GitHub's macOS images are
  often a release behind (26.6 at most, late 2026), so the build fails outright
  with *requires Xcode 27.0, current is 26.6*. The `ios` job therefore gates on
  the runner's Xcode major and skips with a notice when it is too old — it is
  not red, and it runs for real the moment a runner ships the Xcode. Locally
  the workload and Xcode already agree, so `check-ios.sh` is the real check
  until then. Do not pin the workload down to the runner's Xcode to force it
  green: that diverges what CI builds from what a consumer on current tools
  gets.

## Releasing

1. A new engine: `scripts/pin-engine.sh <version>`, then
   `scripts/generate.sh`; within a contract the header, and so the diff, is
   empty.
2. `RippleVersion` in `Directory.Build.props`, and a `## <version>` section in
   `CHANGELOG.md`.
3. Green CI on main, then dispatch `release.yml` — the maintainer's to
   dispatch. It tests, stages, packs, checks the packages as a consumer and
   an Android app, pushes the engines and then the bindings to nuget.org,
   and tags and releases `v<version>` with the packages and `SHA256SUMS`.
