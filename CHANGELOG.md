# Changelog

`MAJOR.MINOR` is the Ripple Effect engine contract these bindings bind, and
`PATCH` is their own. Each version pins one engine release, named here.

## 1.6.0 — unreleased

Binds contract 1.6, Grammar Lich, built and tested against the frozen
engine tree until engine 1.6.0 is released and pinned.

- **A story may read the clock and the reader's position, and asks first.**
  `Manifest.Permissions` lists what it reads; `Effect.Permit`, a Turn before
  the story begins, asks; `Session.Permit(kind, granted)` answers, at any
  time and before `Start` included. The host hands the readings in —
  `SetNow` a `Moment`, `SetPosition` a `Position` — and a reader's stand-in
  goes in with `SetStandIn`. `Effect.Notice` says, once, that a read got a
  stand-in, a refusal or nothing. `PermissionKind`, with its wire names in
  `PermissionKinds`.
- **A passage may count down.** `Passage.Timer`, a `ChoiceTimer`, and
  `Session.BeginCountdown` and `Expire`. The `Player` runs it on its clock,
  keeps it across a language change and drops it on any verb that moves the
  story — never on one that answers nothing or is refused; with
  `PlayerHandlers.Countdowns = false` it runs none, for a reader who asked
  for no time limits. `PlayerHandlers.Now` hands the player's wall clock to
  `SetNow` before every verb.
- **A form speaks the reader's language.** `PromptField.Label` and
  `Placeholder` in the current language, `Optional` for a field that may be
  left empty, and `Options`, each a `PromptOption`, answered by `Id`.
- `Code.NotPermitted`, `StandIn`, `NoCountdown` and `CountdownNotBegun`,
  each a `HostException`.
- Every one of the thirty-three conformance fixtures plays through
  `Session` and `Player`, the nine new ones among them.
- `examples/ConsolePlayer` reads the keyboard on a thread of its own, so a
  countdown runs out while the reader thinks; it asks before a story reads
  the clock or the reader's position, and fills a form by its labels, with a
  menu for its options.

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
  fails to build saying so. On iOS the engine is a static library linked
  into the app — the one platform with no shared build — and the package's
  targets keep and export its C ABI so the P/Invokes resolve through the main
  image; CI builds an app for device and simulator and plays a fixture on a
  booted simulator.
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
