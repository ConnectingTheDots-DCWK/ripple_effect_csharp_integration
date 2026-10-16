# Changelog

`MAJOR.MINOR` is the Ripple Effect engine contract these bindings bind, and
`PATCH` is their own. Each version pins one engine release, named here.

## 1.5.0

- **The C# bindings**, pinning engine 1.5.3: `RippleFx.Engine` on nuget.org,
  for `netstandard2.1` and `net8.0` in C# 9, with no dependencies. `Campaign`
  opened from a folder or from memory, `Session` and every verb, the effects
  as a closed hierarchy of records, `Value` with an Int that stays an Int, the
  engine's failures as typed exceptions, and the `Player` and
  `SoundscapeDriver` helpers over them.
- **The engine as two packages**, `RippleFx.Engine.Native` with Python and
  `RippleFx.Engine.Native.NoPython` without, for Windows, macOS and Linux on
  x64 and arm64, Android on arm64 and x86_64, and iOS on device and
  simulator. A project references exactly one, and one that references both
  fails to build saying so.
- **`RippleFx.Engine.Interop`**, the engine's C ABI as P/Invoke, generated
  from the pinned release's header with ClangSharp, every signature
  blittable; CI regenerates it and fails on a difference, and a test holds
  .NET's layout of every struct to clang's.
- **No speaker ships**, as in the C/C++ bindings: `ISpeaker` and `IVoice` are
  the seam a host's audio is plugged into, and `docs/speaker.md` the
  contract.
- **.NET in the browser is not a target**: its `wasm-tools` workload links
  with Emscripten 3.1.56, and the engine needs 4.0 or later.
  `docs/consuming.md` has why.
