# Ripple Effect for C#

The C# bindings of the [Ripple Effect](https://ripplefx.app) engine: play a
campaign written in the Ripple Effect editor from a game, an app or an engine
of your own — a .NET game, a MonoGame or Godot C# project, a tool, a server —
with the same story, the same dice and the same soundscapes the editor plays.
A Unity package will build on these.

| | |
| --- | --- |
| **`RippleFx.Engine`** | The bindings: `Campaign`, `Session` and its effects, `Value`, typed exceptions, and the `Player` and `SoundscapeDriver` helpers. `netstandard2.1` and `net8.0`, C# 9, with no dependencies. Underneath, `RippleFx.Engine.Interop` is the engine's C ABI as P/Invoke, generated from its header, every signature blittable — safe under NativeAOT and IL2CPP. |
| **`RippleFx.Engine.Native`** | The engine itself, with its Python interpreter, for Windows, macOS and Linux on x64 and arm64, Android on arm64 and x86_64, and iOS on device and simulator. |
| **`RippleFx.Engine.Native.NoPython`** | The same without Python: smaller, and plays every campaign exported with *Needs no Python*. |

**No speaker ships.** A story's soundscapes are scheduled by the engine and
played by your audio — NAudio, OpenAL, FMOD, Wwise or your engine's own —
through a small interface.
[`docs/speaker.md`](https://github.com/ConnectingTheDots-DCWK/ripple_effect_csharp_integration/blob/main/docs/speaker.md)
is the contract.

## A story in a few lines

```csharp
using RippleFx.Engine;

using var campaign = Campaign.OpenDirectory("campaigns/the-city-of-elsenmar");
using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });

for (var batch = session.Start(); ;)
{
    var turn = batch[batch.Count - 1];            // the Turn comes last
    if (turn.IsOver) break;
    batch = turn switch
    {
        Effect.Show shown => session.Choose(Pick(shown.Passage)),   // Markdown, and its pictures
        Effect.Hold hold => WaitThenElapsed(session, hold.Ms),      // your clock, never the engine's
        Effect.Ask asked => session.Answer(Fill(asked.Prompt)),
        Effect.Permit permit => Allow(session, permit.Kinds),       // the clock, a position: the reader says
        _ => throw new InvalidOperationException(),
    };
}
```

`Player` is the same loop written once: it answers holds and runs a
passage's countdown from your frame's `Update()`, applies the sound, and
dispatches the author's named callbacks. A story that reads the clock or the
reader's position asks first, with `Effect.Permit`, and is handed both by
you — the engine reads neither.
`examples/ConsolePlayer` is a terminal player.

## Getting it

```sh
dotnet add package RippleFx.Engine
dotnet add package RippleFx.Engine.Native            # or RippleFx.Engine.Native.NoPython
```

Reference exactly one of the two engines; a build that references both says
so. Platforms, the variants, opening a campaign from memory, and what the web
needs are in
[`docs/consuming.md`](https://github.com/ConnectingTheDots-DCWK/ripple_effect_csharp_integration/blob/main/docs/consuming.md).

**The engine is released on its own.** Each release of these bindings pins
one engine release, `engine-v<version>` of the public
[ripple_engine_releases](https://github.com/ConnectingTheDots-DCWK/ripple_engine_releases),
whose signed manifest names every archive, and the engine packages carry
exactly those archives' libraries. A version here is the engine's contract,
`MAJOR.MINOR`, and a `PATCH` of its own: 1.6.0 binds contract 1.6.

## Working on it

```sh
dotnet test                                                 # the pinned engine, with Python
dotnet test -p:RippleVariant=no-python
RIPPLE_ENGINE_TREE=<a ripple_effect checkout> dotnet test   # an unreleased engine
```

Every conformance fixture of the engine release plays through `Session` and
through `Player`, the soundscape driver is held to the scheduler on a speaker
that writes down what it is told, and .NET's layout of every engine struct is
held to a C compiler's.
[`CLAUDE.md`](https://github.com/ConnectingTheDots-DCWK/ripple_effect_csharp_integration/blob/main/CLAUDE.md)
has the rest.

## Licence

The bindings are MIT. The engine is © ConnectingTheDots under the
[Ripple Effect Runtime Licence](https://ripplefx.app/go/runtime-licence):
free, redistributable unmodified in function as part of your product, on the
condition that its notice is reproduced. See
[`LICENSE`](https://github.com/ConnectingTheDots-DCWK/ripple_effect_csharp_integration/blob/main/LICENSE).
