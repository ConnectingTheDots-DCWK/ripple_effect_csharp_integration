// A campaign in a terminal, in C# — the Player, the soundscape driver, and a
// speaker that prints what it would play instead of playing it: the seam a
// real speaker (NAudio, OpenAL, FMOD, Wwise, Unity's AudioSource, an engine's
// own) is plugged into. docs/speaker.md is the contract.
//
//   dotnet run --project examples/ConsolePlayer -- <bundle folder> [locale]

using System;
using System.Globalization;
using System.Threading;
using RippleFx.Engine;

namespace RippleFx.Examples
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: ConsolePlayer <bundle folder> [locale]");
                return 2;
            }
            try
            {
                using var campaign = Campaign.OpenDirectory(args[0]);
                var manifest = campaign.Manifest;
                Console.WriteLine($"{manifest.Project.Title} — engine {RippleEngine.Version}");

                using var audio = new SoundscapeDriver(campaign, new PrintingSpeaker());
                var config = new SessionConfig
                {
                    Board = manifest.Entries.Count == 0 ? 0 : manifest.Entries[0],
                    Seed = (ulong)DateTime.UtcNow.Ticks,
                    Locale = args.Length > 1 ? args[1] : null,
                };
                using var player = new Player(
                    Session.Create(campaign, config),
                    new PlayerHandlers { Audio = audio, OnError = error => Console.Error.WriteLine(error.Message) });
                player.OnAnyCallback(fired => Console.WriteLine($"  [callback] {fired.Callback} ({fired.EventName})"));

                player.Start();
                // The host's loop: a frame every 25 ms, the driver and the player
                // told the time has moved, a turn answered when one is showing.
                while (player.Turn is { IsOver: false } turn)
                {
                    audio.Update();
                    player.Update();
                    switch (turn)
                    {
                        case Effect.Show shown:
                            if (Ask(shown.Passage) is { } picked)
                            {
                                player.Choose(picked);
                            }
                            else
                            {
                                player.Leave();
                            }
                            break;
                        case Effect.Ask:
                            player.Answer(null);
                            break;
                        default:
                            Thread.Sleep(25);
                            break;
                    }
                }
                switch (player.Turn)
                {
                    case Effect.Ended ended:
                        Console.WriteLine($"\n— The end{(ended.Title != null ? ": " + ended.Title : "")} —");
                        break;
                    case Effect.Failed failed:
                        Console.WriteLine($"\nThe story could not go on: {failed.Message}");
                        break;
                }
                return 0;
            }
            catch (RippleException error)
            {
                Console.Error.WriteLine($"ripple: {error.Message}");
                return 1;
            }
        }

        // The index of the choice taken, or null when the reader leaves.
        private static int? Ask(Passage passage)
        {
            Console.WriteLine($"\n== {passage.Title} ==\n{passage.Text}\n");
            if (passage.Choices.Count == 0)
            {
                return null;
            }
            for (var i = 0; i < passage.Choices.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {passage.Choices[i].Text}{(passage.Choices[i].Available ? "" : " (spent)")}");
            }
            while (true)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null)
                {
                    return null;
                }
                if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var picked)
                    && picked >= 1 && picked <= passage.Choices.Count && passage.Choices[picked - 1].Available)
                {
                    return picked - 1;
                }
            }
        }

        // Prints every voice command it is given. A real one opens the file and
        // plays it; this one says what it would have done.
        private sealed class PrintingSpeaker : ISpeaker
        {
            public IVoice? Open(VoiceSource source, VoiceRole role, VoiceEvents events)
            {
                Console.WriteLine($"  [sound] open {source.File ?? source.Path} ({(role == VoiceRole.Cue ? "cue" : "track")})");
                return new PrintingVoice(source.Path);
            }

            public bool Resolves(VoiceSource source) => true;
        }

        private sealed class PrintingVoice : IVoice
        {
            private readonly string path;

            public PrintingVoice(string path) => this.path = path;

            public void Play(VoicePlay request) =>
                Console.WriteLine($"  [sound] play {path} from {request.FromMs:F0} ms at gain {request.Gain:F2}{(request.Looping ? ", looping" : "")}");

            public void SetGain(double gain)
            {
            }

            public void Pause() => Console.WriteLine($"  [sound] pause {path}");

            public void Resume() => Console.WriteLine($"  [sound] resume {path}");

            public void Stop() => Console.WriteLine($"  [sound] stop {path}");
        }
    }
}
