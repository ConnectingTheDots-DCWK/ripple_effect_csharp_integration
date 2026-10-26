// A campaign in a terminal, in C# — the Player, the soundscape driver, and a
// speaker that prints what it would play instead of playing it: the seam a
// real speaker (NAudio, OpenAL, FMOD, Wwise, Unity's AudioSource, an engine's
// own) is plugged into. docs/speaker.md is the contract.
//
//   dotnet run --project examples/ConsolePlayer -- <bundle folder> [locale]
//
// The keyboard is read on a thread of its own, so the loop keeps the time
// while the reader thinks: a hold is answered and a passage's countdown runs
// out on the player's clock, as they would in a game's frame loop.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    new PlayerHandlers
                    {
                        Audio = audio,
                        OnError = error => Console.Error.WriteLine(error.Message),
                        OnEffect = effect =>
                        {
                            if (effect is Effect.Notice notice)
                            {
                                Console.WriteLine($"  [{notice.Code}] {notice.Message}");
                            }
                        },
                        // This machine's clock, for a story the reader let read it.
                        Now = Moment.Now,
                    });
                player.OnAnyCallback(fired => Console.WriteLine($"  [callback] {fired.Callback} ({fired.EventName})"));

                var keyboard = new Keyboard();
                player.Start();
                // The host's loop: a frame every 25 ms, the driver and the player
                // told the time has moved, a turn answered when the reader has.
                Effect? shown = null;
                Form? form = null;
                var asking = 0;
                while (player.Turn is { IsOver: false } turn)
                {
                    audio.Update();
                    player.Update();
                    if (!ReferenceEquals(turn, player.Turn))
                    {
                        continue;
                    }
                    if (!ReferenceEquals(turn, shown))
                    {
                        shown = turn;
                        form = turn is Effect.Ask asked ? new Form(asked.Prompt) : null;
                        asking = 0;
                        Present(turn, form);
                    }
                    if (turn is Effect.Hold || !keyboard.TryRead(out var line))
                    {
                        continue;
                    }
                    switch (turn)
                    {
                        case Effect.Show { Passage: var passage }:
                            if (line == null)
                            {
                                player.Leave();
                            }
                            else if (Pick(line, passage.Choices.Count) is { } picked && passage.Choices[picked].Available)
                            {
                                player.Choose(picked);
                            }
                            break;
                        case Effect.Ask:
                            if (line == null)
                            {
                                player.Answer(null);
                            }
                            else if (form!.Take(line))
                            {
                                player.Answer(form.Answers);
                            }
                            else
                            {
                                form.AskNext();
                            }
                            break;
                        case Effect.Permit permit:
                            // One question per kind, in order; the last answer begins the story.
                            player.Permit(permit.Kinds[asking++], line != null && line.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase));
                            if (ReferenceEquals(player.Turn, permit) && asking < permit.Kinds.Count)
                            {
                                AskPermission(permit.Kinds[asking]);
                            }
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

        private static void Present(Effect turn, Form? form)
        {
            switch (turn)
            {
                case Effect.Show { Passage: var passage }:
                    Console.WriteLine($"\n== {passage.Title} ==\n{passage.Text}\n");
                    for (var i = 0; i < passage.Choices.Count; i++)
                    {
                        Console.WriteLine($"  {i + 1}. {passage.Choices[i].Text}{(passage.Choices[i].Available ? "" : " (spent)")}");
                    }
                    if (passage.Timer is { Visible: true } timer && timer.Default < passage.Choices.Count)
                    {
                        var seconds = (timer.AfterMs + timer.TimeoutMs) / 1000.0;
                        Console.WriteLine($"  (in {seconds.ToString("0.#", CultureInfo.InvariantCulture)} s, {passage.Choices[timer.Default].Text})");
                    }
                    Console.Write("> ");
                    break;
                case Effect.Ask { Prompt: var prompt }:
                    Console.WriteLine($"\n== {prompt.Title} ==");
                    form!.AskNext();
                    break;
                case Effect.Permit permit:
                    AskPermission(permit.Kinds[0]);
                    break;
            }
        }

        private static void AskPermission(PermissionKind kind)
        {
            var noun = kind == PermissionKind.DateTime ? "the date and time" : "where you are";
            Console.Write($"\nThis story would like to read {noun}. Allow it? [y/N] ");
        }

        private static int? Pick(string line, int count) =>
            int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var picked) && picked >= 1 && picked <= count
                ? picked - 1
                : (int?)null;

        // A form asked a field at a time, by its label: a numbered menu for a
        // field with options, answered by their ids; a field's default for an
        // empty line, and nothing for an optional one with none. A required
        // field is asked again rather than sent empty.
        private sealed class Form
        {
            private readonly Prompt prompt;
            private readonly List<KeyValuePair<string, Value>> answers = new List<KeyValuePair<string, Value>>();
            private int skipped;

            public Form(Prompt prompt) => this.prompt = prompt;

            public IReadOnlyList<KeyValuePair<string, Value>> Answers => answers;

            private PromptField Field => prompt.Fields[answers.Count + skipped];

            public void AskNext()
            {
                var field = Field;
                Console.WriteLine($"{field.Label}{(field.Optional ? " (optional)" : "")}");
                for (var i = 0; i < field.Options.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {field.Options[i].Label}");
                }
                var hint = field.Options.Count > 0 && field.Type.EndsWith("List", StringComparison.Ordinal)
                    ? "numbers, separated by spaces"
                    : field.Placeholder;
                Console.Write(hint.Length > 0 ? $"({hint}) > " : "> ");
            }

            // Takes one line for the field being asked; true once every field has its answer.
            public bool Take(string line)
            {
                var field = Field;
                var typed = line.Trim();
                Value? answer = null;
                if (typed.Length == 0 && field.Options.Count > 0)
                {
                    // A menu starts on the option whose value is the default.
                    answer = field.Options.FirstOrDefault(option => option.Value.Equals(field.Default)) is { } start ? Value.Text(start.Id) : null;
                }
                else if (typed.Length == 0)
                {
                    answer = field.Default.IsNull ? null : field.Default;
                }
                else if (field.Options.Count == 0)
                {
                    answer = Value.Text(typed);
                }
                else
                {
                    var ids = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(word => Pick(word, field.Options.Count))
                        .Where(index => index != null)
                        .Select(index => Value.Text(field.Options[index!.Value].Id))
                        .ToList();
                    if (ids.Count > 0)
                    {
                        answer = field.Type.EndsWith("List", StringComparison.Ordinal) ? Value.List(ids) : ids[0];
                    }
                }
                if (answer == null && !field.Optional)
                {
                    Console.WriteLine("  This one wants an answer.");
                    return false;
                }
                if (answer == null)
                {
                    skipped++;
                }
                else
                {
                    answers.Add(new KeyValuePair<string, Value>(field.Name, answer));
                }
                return answers.Count + skipped == prompt.Fields.Count;
            }
        }

        // Standard input, a line at a time, on a thread of its own. Once input
        // ends every read is null, which leaves whatever is showing.
        private sealed class Keyboard
        {
            private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
            private volatile bool ended;

            public Keyboard()
            {
                new Thread(() =>
                {
                    while (Console.ReadLine() is { } line)
                    {
                        lines.Enqueue(line);
                    }
                    ended = true;
                })
                { IsBackground = true }.Start();
            }

            // A line when one is waiting; otherwise a frame's sleep, and false.
            public bool TryRead(out string? line)
            {
                if (lines.TryDequeue(out var next))
                {
                    line = next;
                    return true;
                }
                line = null;
                if (ended)
                {
                    return true;
                }
                Thread.Sleep(25);
                return false;
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
