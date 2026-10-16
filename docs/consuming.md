# Consuming the bindings

Two packages, always: the bindings, and one engine.

```sh
dotnet add package RippleFx.Engine
dotnet add package RippleFx.Engine.Native            # with Python
dotnet add package RippleFx.Engine.Native.NoPython   # or without
```

| Package | What it is |
| --- | --- |
| `RippleFx.Engine` | The bindings, managed only: `netstandard2.1` and `net8.0`, no dependencies. |
| `RippleFx.Engine.Native` | The engine with its Python interpreter, as NuGet ships native code: `runtimes/<rid>/native/`. |
| `RippleFx.Engine.Native.NoPython` | The engine without it — about a third of the size — which refuses a campaign that needs Python when it is opened (`NeedsPythonException`). The editor's *Needs no Python* export is checked for exactly that host. |

**Exactly one engine.** The two ship libraries of the same name, and
whichever won the copy would decide silently whether a story runs its
scripts, so a project that references both fails to build and says so.
`RippleEngine.HasPython` reports which one loaded. A project that references
neither builds, and the first call that needs the engine throws
`EngineNotFoundException`, naming the two packages.

**The versions move together.** `MAJOR.MINOR` is the engine contract these
bindings bind; the engine packages of a release carry the libraries of the
one engine release it pins, checked against the digests in `engine.json` and
signed by the engine's key. `RippleEngine.EnsureLoaded()` checks, once, that
the library loaded binds the contract the bindings do.

## Opening a campaign

`Campaign.OpenDirectory(folder)` opens the bundle in the folder that holds
`campaign.json`, reading files as the story asks for them. Where the platform
has no folder to give — a packed asset system, a phone's bundled assets, a
console — `Campaign.OpenFiles(files)` takes every file of the bundle by its
path inside it, and copies them. A speaker then reads media with
`Campaign.MediaBytes(index)` rather than from `VoiceSource.File`.

Everything a verb answers is a copy: effects, passages and values are C#
records that outlive the session and the campaign. `Campaign`, `Session`,
`Mixer`, `SoundscapeDriver` and `Player` are `IDisposable`, and a forgotten
`Dispose` is collected rather than leaked. A session goes on working after
its campaign is disposed.

## Per platform

- **Windows** (`win-x64`, `win-arm64`): `ripple_effect_engine.dll`, built
  against the dynamic CRT.
- **macOS** (`osx-x64`, `osx-arm64`), 12.0 and up: one library per
  architecture; a universal app takes both RIDs.
- **Linux** (`linux-x64`, `linux-arm64`): glibc 2.35 and up. A distribution
  that is not glibc — Alpine's musl — has no engine build.
- **Android** (`android-arm64`, `android-x64`), API 24 and up: .NET for
  Android packs the library into the APK under `lib/arm64-v8a/` and
  `lib/x86_64/`. CI builds an app from the packages and checks the APK holds
  the engine's Android build for both, byte for byte: .NET's RID graph falls
  back from `android-*` to `linux-*`, so the name alone would not show a
  Linux library that a phone cannot load.
- **iOS** (`ios-arm64`, `iossimulator-arm64`), 13.0 and up: static only — the
  engine ships no shared library for iOS. The packages carry
  `libripple_effect_engine.a` and the system libraries a static link needs
  (`native-static-libs.txt`).

A self-contained or single-file publish takes the library with it, as it
does any NuGet native asset.

**Ahead of time.** Every signature in `RippleFx.Engine.Interop` is
blittable and nothing calls back into managed code, so no marshalling stub
is generated at run time: the assembly is marked AOT-compatible for `net8.0`,
and the same holds under IL2CPP.

## On the web

**.NET in the browser is not a target yet.** The engine's web build for a
host that links native code is `wasm32-unknown-emscripten`, from engine
1.5.3, and it needs Emscripten 4.0 or later. .NET's `wasm-tools` workload
links native code with its own Emscripten — 3.1.34 in .NET 8, 3.1.56 in .NET
10 — and that one's optimiser refuses the engine's objects (measured with
.NET 10.0.13: `wasm-opt` rejects `--enable-bulk-memory-opt`). Nothing here
can change which Emscripten .NET bundles; a later engine release built for
it, or a .NET that moves to Emscripten 4, would open the door.

Unity's web build is another matter: Unity 6000.5 and later bundle
Emscripten 4.0.19, which links the engine, and that is the Unity package's
path to the browser.

## The engine's notice

The engine packages carry `ENGINE-NOTICE.txt` and `LICENSE-ENGINE.txt` at
their root. The notice must be reproduced wherever your product lists the
third-party software it contains: the Ripple Effect Runtime Licence asks that
and nothing else.
