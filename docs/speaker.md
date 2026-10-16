# The speaker contract

How a campaign's soundscapes reach a host's audio — NAudio, OpenAL, FMOD,
Wwise, Unity's or Godot's audio, or a game engine's own mixer. **This
repository ships no speaker.** The audio APIs a C# host might use have
nothing in common, and every one of them is a dependency worth more than the
engine. What ships is the seam: the engine's scheduler decides every voice,
and a speaker of yours plays them. It binds **Ripple Effect contract 1.5**,
and transcribes the engine's `host-audio-system.md` §9, the source of truth;
it is the C/C++ binding's contract, word for word where the two can be.

## Three layers, and the one you write

1. **The session** says what the story asks for, as `Effect.Audio`: `Play` a
   soundscape, `Stop` it, `Cut` everything, `Cue` a sound.
2. **The scheduler** decides every voice from those: which file, from where,
   how loud, when it fades, when one soundscape hands over to the next. It is
   the engine's mixer (`Mixer`), with no clock, no file and no voice of its
   own. **`SoundscapeDriver`** drives it: it forwards the session's commands,
   ticks it from your loop, and does every duty the scheduler asks of a host.
3. **The speaker** makes sounds. It is the only part that touches an audio
   API, and the only part you write.

```csharp
using var audio = new SoundscapeDriver(campaign, new MySpeaker());   // the speaker is yours
using var player = new Player(Session.Create(campaign, config), new PlayerHandlers { Audio = audio });
// then, every frame:  audio.Update();  player.Update();
```

## The interface

```csharp
public interface ISpeaker
{
    IVoice? Open(VoiceSource source, VoiceRole role, VoiceEvents events);
    bool Resolves(VoiceSource source);
}

public interface IVoice
{
    void Play(VoicePlay request);
    void SetGain(double gain);
    void Pause();
    void Resume();
    void Stop();
}

public sealed record VoicePlay
{
    public double FromMs { get; init; }      // into the file
    public double? ToMs { get; init; }       // the file's end when null
    public double Gain { get; init; }        // 0–1, on the engine's gain scale (below)
    public double Rate { get; init; }        // 1 is as recorded
    public bool Looping { get; init; }
    public double LoopFromMs { get; init; }  // where each turn of a loop comes round to
}

public sealed record VoiceSource
{
    public uint Media { get; init; }         // the medium, by index
    public string Path { get; init; }        // inside the bundle
    public string? File { get; init; }       // on disk, for a campaign opened from a folder
}
```

They are interfaces rather than base classes so that the thing already
holding your audio — a `MonoBehaviour`, a Godot `Node` — can be the speaker.

## What each part must do

- **`Open(source, role, events)`** returns once the voice can begin *at
  once*: the file opened, or enough of it decoded. It returns **null** when
  the file cannot be played, and the driver tells the scheduler, which hears
  that as silence and never retries. A `Track` may be minutes long and is
  usually better streamed; a `Cue` is a short sound that must start on time
  and is usually better decoded whole. The role is a hint about strategy,
  not a different contract. **Keep `events`** with the voice. For a campaign
  opened from memory `source.File` is null: read the bytes with
  `Campaign.MediaBytes(source.Media)`.
- **`Play(request)`** begins at once and replaces whatever the voice was
  playing. It may be called again on the same voice: a gapless bed whose file
  ended, coming round, which should follow on as closely as the API allows.
  It plays from `FromMs` to `ToMs`, or to the file's end. When `Looping`,
  reaching `ToMs` comes round to **`LoopFromMs`, not `FromMs`**: a bed joined
  partway in loops over its whole clip.
- **`events.Ended()`** once per play, when a non-looping voice reaches the
  end of what it was given. Never after `Stop()`, never for a looping voice.
- **`events.Duration(ms)`** once, when the file's length is known. A track
  played to its file's end has no end the scheduler can plan a fade-out or a
  gapless turn for until then.
- **Both `events` calls are thread-safe** and may come from an audio
  thread: the driver hears them on its next `Update()`. A call from a voice
  that was stopped meanwhile is ignored.
- **`SetGain`** writes a new gain. The scheduler writes one every tick a fade
  moves, a step at a time, so smooth between them over a few milliseconds,
  or a fade is a staircase.
- **`Pause` and `Resume`** hold a voice exactly where it is and let it go
  again. The driver pauses every voice together.
- **`Stop`** ends the voice for good and lets its resources go.
- **`Resolves(source)`** is asked before a soundscape plays: a track whose
  file your speaker cannot play is told to the scheduler as silence up front,
  rather than tried. Answer true when unsure.

**The gain scale is cubic.** The editor writes a gain to mpv's volume
control, whose curve is cubic, so a speaker writing amplitude writes **`gain
* gain * gain`**: half the gain is an eighth of the amplitude. A speaker that
writes the gain as amplitude mixes every soundscape louder in its quiet parts
than its author heard it.

## What the driver does, so the speaker does not

All of this is `SoundscapeDriver`'s and must not be repeated in a speaker:
- **the clock and the tick**: `Update()` every frame or so; the editor ticks
  every 25 ms, and nothing is lost by more often;
- **the scheduler's commands**, applied in order;
- **a late voice**: the time `Open` took, times `Rate`, is added to `FromMs`,
  and a looping clip wraps inside its loop. A clip already over plays
  nothing and is reported ended;
- **voices reported ended or measured**, told to the scheduler;
- **tracks with no file**, told as silence rather than tried;
- **mute**, which is the scheduler's: it writes zero and remembers;
- **the handover between soundscapes**, which never overlap: the one
  leaving fades out over its own fade-out before the next begins.

The driver and the player read the clock they were given — `Clocks.Steady()`
unless you pass another — and own no thread. A game loop is the timer.

## Mapping notes

These are notes, not code: nothing here references an audio library.

- **NAudio** (Windows): an `AudioFileReader` per voice, its `CurrentTime` set
  for `FromMs`, its `Volume` to `gain³`; a `WaveOutEvent` whose
  `PlaybackStopped` posts `events.Ended()` unless you stopped it. A loop to
  `LoopFromMs` and a rate are sample providers of your own over the reader.
- **OpenAL** (OpenTK, Silk.NET): a source per voice, `AL_GAIN` to `gain³`,
  `AL_PITCH` to `Rate` (which also shifts the pitch), `AL_SEC_OFFSET` for
  `FromMs`; streamed tracks queue buffers, and the last one drained is the end.
- **FMOD** (its C# wrapper): `CREATESTREAM` for a track, `CREATESAMPLE` for a
  cue; `Channel.setPosition`, `setPitch`, `setVolume(gain³)`,
  `setLoopPoints(LoopFromMs, ToMs)`, and an `END` channel callback posting
  `events.Ended()`. `Sound.getLength` is `Duration`.
- **Godot** (C#): an `AudioStreamPlayer` per voice; `Play(FromMs / 1000)`,
  `PitchScale` for `Rate`, `VolumeDb = Mathf.LinearToDb(gain³)`, and its
  `Finished` signal posting `events.Ended()`.
- **Unity**: an `AudioSource` per voice, `time` for `FromMs`, `pitch` for
  `Rate`, `volume = gain³`. Unity's own package of these bindings will ship
  this speaker; until then it is yours.
- **A game engine's own audio** usually has all of this under other names;
  the cubic gain is the line most often missed.

## Proving a speaker

`tests/RippleFx.Engine.Tests/Driver.cs` is how the driver is held to the
scheduler, on a speaker that writes down what it is told. A speaker of your
own should pass the shape the JS binding's Web Audio speaker is held to in
three browsers:
- a file ends near its length, and reports it;
- a clip ends near its `ToMs`;
- a loop never ends;
- a cue ends near its length;
- a paused cue ends later by the pause;
- a stopped voice never ends.

**And hear it**, with `examples/ConsolePlayer` over a real campaign and your
speaker in place of the printing one: a test can count voices, but only a
person can hear a seam.
