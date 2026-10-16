// The soundscape driver against a speaker that writes down what it is told,
// on a clock that is a number. Nothing here makes a sound: what is checked is
// what a speaker would have been asked to do — which a person still has to
// hear before a real speaker is trusted.
//
// The fixture is `the-choir`: `tavern`, a bed with a one-second fade-in and a
// two-second fade-out whose clip runs to the file's end, and `rain`, a
// ten-second loop. Its media are named in the manifest and not shipped, which
// is all a recording speaker needs.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class Driver
    {
        [Test]
        public void ASoundscapePlayedOpensItsVoiceAndFadesItIn()
        {
            using var rig = new Rig();
            rig.Log.Now = 1000;
            rig.Driver.Apply(Play(rig.Named("tavern")));
            rig.RunTo(3000);
            Assert.That(rig.Calls("open", "media/music/tavern.ogg"), Has.Count.EqualTo(1));
            var plays = rig.Calls("play");
            Assert.That(plays, Has.Count.EqualTo(1));
            Assert.That(plays[0].Play!.ToMs, Is.Null, "an auto bed with no clip end plays to the file's end");
            // The fade-in is the scheduler's, written a step at a time, up and only up.
            var gains = rig.Calls("gain");
            Assert.That(gains, Has.Count.GreaterThan(3));
            for (var i = 1; i < gains.Count; i++)
            {
                Assert.That(gains[i].Gain, Is.GreaterThanOrEqualTo(gains[i - 1].Gain));
            }
            Assert.That(rig.Driver.Playing, Is.EqualTo(rig.Named("tavern").Id));
        }

        [Test]
        public void AVoiceThatOpensLateStartsLaterInItsFile()
        {
            using var prompt = new Rig(latency: 0);
            using var late = new Rig(latency: 120);
            foreach (var rig in new[] { prompt, late })
            {
                rig.Log.Now = 1000;
                rig.Driver.Apply(Play(rig.Named("rain")));
                rig.RunTo(1500);
            }
            var onTime = prompt.Calls("play");
            var behind = late.Calls("play");
            Assert.That(onTime, Has.Count.EqualTo(1));
            Assert.That(behind, Has.Count.EqualTo(1));
            // The time `Open` took, times the rate, is added to `from` (§9).
            Assert.That(behind[0].Play!.FromMs - onTime[0].Play!.FromMs, Is.EqualTo(120.0 * onTime[0].Play!.Rate));
            // A `loop` track is a fresh voice per pass; only a gapless `auto` bed is
            // one looping voice — so this one is not.
            Assert.That(behind[0].Play!.Looping, Is.False);
        }

        [Test]
        public void AHandoverFadesTheOldOutBeforeTheNewOpens()
        {
            using var rig = new Rig();
            rig.Log.Now = 1000;
            rig.Driver.Apply(Play(rig.Named("tavern")));
            rig.RunTo(4000);
            var before = rig.Log.Calls.Count;
            rig.Driver.Apply(Play(rig.Named("rain")));
            rig.RunTo(9000);

            // The tavern's fade-out, down and only down, then its stop.
            var down = new List<double>();
            ulong stopped = 0;
            foreach (var call in rig.Log.Calls.Skip(before).Where(call => call.Path == "media/music/tavern.ogg"))
            {
                if (call.What == "gain")
                {
                    down.Add(call.Gain);
                }
                if (call.What == "stop")
                {
                    stopped = call.At;
                }
            }
            Assert.That(down, Has.Count.GreaterThan(3));
            for (var i = 1; i < down.Count; i++)
            {
                Assert.That(down[i], Is.LessThanOrEqualTo(down[i - 1]));
            }
            Assert.That(stopped, Is.GreaterThan(0), "the tavern was never stopped");
            // Sequential handover: rain opens only once the tavern has gone.
            var rain = rig.Calls("open", "media/sfx/rain.ogg");
            Assert.That(rain, Has.Count.EqualTo(1));
            Assert.That(rain[0].At, Is.GreaterThanOrEqualTo(stopped));
            Assert.That(stopped - 4000, Is.GreaterThanOrEqualTo(1900), "the fade-out is the soundscape's two seconds");
        }

        [Test]
        public void AVoiceReportedEndedIsLetGoAndAStoppedOneIsNotHeardFrom()
        {
            using var rig = new Rig();
            rig.Log.Now = 1000;
            rig.Driver.Apply(Play(rig.Named("tavern")));
            rig.RunTo(1100);
            Assert.That(rig.Driver.Voices, Is.EqualTo(1));
            // The file's length, once the decoder knows it, then its end.
            rig.Log.Events[0].Duration(30000.0);
            rig.Log.Events[0].Ended();
            rig.RunTo(1200);
            // A gapless auto bed comes round on the same voice rather than a new one.
            Assert.That(rig.Calls("open"), Has.Count.EqualTo(1));
            Assert.That(rig.Calls("play"), Has.Count.GreaterThanOrEqualTo(2));

            rig.Driver.Apply(new AudioCommand { Kind = AudioKind.Stop });
            rig.RunTo(5000);
            Assert.That(rig.Driver.Voices, Is.Zero);
            var after = rig.Log.Calls.Count;
            rig.Log.Events[0].Ended(); // a stopped voice's end, late: ignored
            rig.RunTo(5100);
            Assert.That(rig.Log.Calls, Has.Count.EqualTo(after));
            Assert.That(rig.Driver.Idle, Is.True);
        }

        [Test]
        public void MutingWritesEveryGainAsZero()
        {
            using var rig = new Rig();
            rig.Log.Now = 1000;
            rig.Driver.Apply(Play(rig.Named("rain")));
            rig.RunTo(1500);
            rig.Driver.Muted = true;
            rig.RunTo(2000);
            var gains = rig.Calls("gain");
            Assert.That(gains, Is.Not.Empty);
            Assert.That(gains[gains.Count - 1].Gain, Is.Zero);
            Assert.That(rig.Driver.Muted, Is.True);
        }

        [Test]
        public void PausingHoldsEveryVoiceAndTheTimeline()
        {
            using var rig = new Rig();
            rig.Log.Now = 1000;
            rig.Driver.Apply(Play(rig.Named("rain")));
            rig.RunTo(1500);
            rig.Driver.Pause();
            Assert.That(rig.Calls("pause"), Has.Count.EqualTo(1));
            var paused = rig.Log.Calls.Count;
            rig.RunTo(20000);
            Assert.That(rig.Log.Calls, Has.Count.EqualTo(paused), "nothing moves while paused");
            rig.Driver.Resume();
            Assert.That(rig.Calls("resume"), Has.Count.EqualTo(1));
        }

        private static AudioCommand Play(Soundscape scape) => new AudioCommand { Kind = AudioKind.Play, Soundscape = scape };

        private sealed record Call(string What, ulong At, ulong Voice, string Path)
        {
            public VoicePlay? Play { get; init; }

            public double Gain { get; init; }
        }

        private sealed class Recording
        {
            public ulong Now { get; set; }

            public ulong Latency { get; set; }

            public ulong Opened { get; set; }

            public List<Call> Calls { get; } = new List<Call>();

            public List<VoiceEvents> Events { get; } = new List<VoiceEvents>();
        }

        private sealed class RecordingVoice : IVoice
        {
            private readonly Recording log;
            private readonly ulong voice;
            private readonly string path;

            public RecordingVoice(Recording log, ulong voice, string path)
            {
                this.log = log;
                this.voice = voice;
                this.path = path;
            }

            public void Play(VoicePlay request) => Note("play", play: request);

            public void SetGain(double gain) => Note("gain", gain);

            public void Pause() => Note("pause");

            public void Resume() => Note("resume");

            public void Stop() => Note("stop");

            private void Note(string what, double gain = 0, VoicePlay? play = null) =>
                log.Calls.Add(new Call(what, log.Now, voice, path) { Gain = gain, Play = play });
        }

        // Opens every voice it is asked for, `Latency` milliseconds late, and
        // keeps each voice's events so a test can report an end or a length.
        private sealed class RecordingSpeaker : ISpeaker
        {
            private readonly Recording log;

            public RecordingSpeaker(Recording log) => this.log = log;

            public IVoice? Open(VoiceSource source, VoiceRole role, VoiceEvents events)
            {
                log.Now += log.Latency;
                var voice = ++log.Opened;
                log.Calls.Add(new Call("open", log.Now, voice, source.Path));
                log.Events.Add(events);
                return new RecordingVoice(log, voice, source.Path);
            }

            public bool Resolves(VoiceSource source) => true;
        }

        private sealed class Rig : IDisposable
        {
            public Rig(ulong latency = 0)
            {
                Log.Latency = latency;
                Campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-choir"));
                Driver = new SoundscapeDriver(Campaign, new RecordingSpeaker(Log), new SoundscapeOptions { Now = () => Log.Now });
            }

            public Recording Log { get; } = new Recording();

            public Campaign Campaign { get; }

            public SoundscapeDriver Driver { get; }

            public Soundscape Named(string name)
            {
                var manifest = Campaign.Manifest;
                for (var i = 0; i < manifest.Soundscapes.Count; i++)
                {
                    if (manifest.Soundscapes[i].Name == name)
                    {
                        return Campaign.Soundscape((uint)i);
                    }
                }
                throw new InvalidOperationException("no soundscape " + name);
            }

            // The host's loop, every 25 ms, until `until`.
            public void RunTo(ulong until)
            {
                while (Log.Now < until)
                {
                    Log.Now += 25;
                    Driver.Update();
                }
            }

            public List<Call> Calls(string what, string? path = null) =>
                Log.Calls.Where(call => call.What == what && (path == null || call.Path == path)).ToList();

            public void Dispose()
            {
                Driver.Dispose();
                Campaign.Dispose();
            }
        }
    }
}
