using System;
using System.Collections.Generic;

namespace RippleFx.Engine
{
    /// <summary>What a <see cref="Player"/> hands the host.</summary>
    public sealed record PlayerHandlers
    {
        /// <summary>Every effect, in the order the engine produced it.</summary>
        public Action<Effect>? OnEffect { get; init; }

        /// <summary>
        /// A failure of a verb the player made by itself — the <c>Elapsed</c>
        /// after a hold — or a handler's exception. Delivery goes on either way.
        /// Without one, a self-made verb's failure is thrown from
        /// <see cref="Player.Update"/>, and a handler's is lost.
        /// </summary>
        public Action<Exception>? OnError { get; init; }

        /// <summary>Where the story's sound goes; without one, nothing is heard.</summary>
        public SoundscapeDriver? Audio { get; init; }

        /// <summary>
        /// Whether the player runs a passage's countdown on its clock — begins
        /// it and expires it from <see cref="Player.Update"/>. True unless a
        /// reader asked for no time limits (WCAG 2.2.1), or the host runs them
        /// itself with <see cref="Player.BeginCountdown"/> and <see cref="Player.Expire"/>.
        /// </summary>
        public bool Countdowns { get; init; } = true;

        /// <summary>
        /// What time it is for the story, handed to <see cref="Session.SetNow"/>
        /// before every verb the player makes or is asked for — for a host
        /// that granted <see cref="PermissionKind.DateTime"/>. Without one, the
        /// story has no clock unless the host sets it on the session.
        /// </summary>
        public Func<Moment?>? Now { get; init; }
    }

    /// <summary>
    /// The loop over a session, written once. It delivers every effect in
    /// order, keeps the Turn the last one named, answers a hold from its clock
    /// when <see cref="Update"/> finds it due, runs a passage's countdown on
    /// the same clock (<c>campaign-host-api.md</c> §4b), applies the sound to the driver,
    /// and is the table the author's named callbacks are dispatched through
    /// (<c>campaign-host-api.md</c> §5b). A verb from inside a handler is
    /// refused: the rest of the batch would arrive after the new one.
    /// </summary>
    public sealed class Player : IDisposable
    {
        private readonly PlayerHandlers handlers;
        private readonly Func<ulong> clock;
        private readonly Dictionary<string, Action<CallbackEvent>> callbacks = new Dictionary<string, Action<CallbackEvent>>(StringComparer.Ordinal);
        private readonly List<Action<CallbackEvent>> every = new List<Action<CallbackEvent>>();
        private bool delivering;
        private bool disposed;

        /// <param name="session">The session to play; the player owns it from here, and disposes it.</param>
        /// <param name="handlers">What the player hands the host.</param>
        /// <param name="now">The clock, in milliseconds; <see cref="Clocks.Steady"/> unless a test says otherwise.</param>
        public Player(Session session, PlayerHandlers handlers, Func<ulong>? now = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            clock = now ?? Clocks.Steady();
        }

        /// <summary>
        /// The session underneath, for what the player does not wrap. A verb
        /// called on it goes round the player.
        /// </summary>
        public Session Session { get; }

        /// <summary>Where the story is: the last Turn delivered.</summary>
        public Effect? Turn { get; private set; }

        /// <summary>When the pending hold is due, by the player's clock; null when none is.</summary>
        public ulong? HoldDue { get; private set; }

        /// <summary>
        /// When the shown passage's countdown begins, by the player's clock;
        /// null when it has begun, or there is none to run.
        /// </summary>
        public ulong? CountdownBegins { get; private set; }

        /// <summary>
        /// When the countdown runs out, by the player's clock, once it has
        /// begun; null otherwise. A host drawing a visible
        /// <see cref="ChoiceTimer"/> counts down to this.
        /// </summary>
        public ulong? CountdownExpires { get; private set; }

        /// <summary>
        /// How many callbacks fired with nothing registered under their name —
        /// not a failure: the author and the host declare their halves apart.
        /// </summary>
        public long CallbackMisses { get; private set; }

        /// <summary>Calls <paramref name="handler"/> whenever the callback named <paramref name="name"/> fires, replacing any handler already under that name.</summary>
        public void OnCallback(string name, Action<CallbackEvent> handler) => callbacks[name] = handler;

        public void RemoveCallback(string name) => callbacks.Remove(name);

        /// <summary>Calls <paramref name="handler"/> for every callback that fires, registered or not.</summary>
        public void OnAnyCallback(Action<CallbackEvent> handler) => every.Add(handler);

        public void ClearCallbacks()
        {
            callbacks.Clear();
            every.Clear();
        }

        public void Start() => Verb(nameof(Start), () => Session.Start());

        public void Choose(int index) => Verb(nameof(Choose), () => Session.Choose(index));

        public void Leave() => Verb(nameof(Leave), () => Session.Leave());

        public void Answer(IEnumerable<KeyValuePair<string, Value>>? fields) => Verb(nameof(Answer), () => Session.Answer(fields));

        /// <summary>The reader's answer for one kind; the answer to the last one asked begins the story.</summary>
        public void Permit(PermissionKind kind, bool granted) => Verb(nameof(Permit), () => Session.Permit(kind, granted));

        /// <summary>
        /// Begins the shown passage's countdown now, for a host that runs
        /// countdowns itself; with <see cref="PlayerHandlers.Countdowns"/> on,
        /// the player then runs it out on its clock.
        /// </summary>
        public void BeginCountdown() => Verb(nameof(BeginCountdown), () => Session.BeginCountdown(), CountdownStep.Begin);

        /// <summary>Runs the shown passage's countdown out now: its default is taken.</summary>
        public void Expire() => Verb(nameof(Expire), () => Session.Expire());

        /// <summary>Reads on in another language. A countdown under way goes on: the passage shown again is the same visit.</summary>
        public void SetLocale(string locale) => Verb(nameof(SetLocale), () => Session.SetLocale(locale), CountdownStep.Keep);

        public void Restart() => Verb(nameof(Restart), () => Session.Restart());

        public void Stop() => Verb(nameof(Stop), () => Session.Stop());

        /// <summary>
        /// Puts the story down and answers its save — null, and nothing stopped,
        /// unless the story is on a passage or a prompt.
        /// </summary>
        public string? Pause()
        {
            if (delivering)
            {
                throw Reentrant(nameof(Pause));
            }
            HoldDue = null;
            ForgetCountdown();
            var paused = Session.Pause();
            Dispatch(paused.Effects);
            return paused.Save;
        }

        /// <summary>
        /// What the host calls from its loop: answers a hold whose time has
        /// come, and begins or runs out a countdown whose time has.
        /// </summary>
        public void Update()
        {
            if (delivering || disposed)
            {
                return;
            }
            var now = clock();
            if (HoldDue is { } due && now >= due)
            {
                HoldDue = null;
                Itself(() => Session.Elapsed());
            }
            else if (CountdownBegins is { } begins && now >= begins)
            {
                // Measured from when it was due, not from when Update came
                // round to it, so a slow frame does not lengthen the countdown.
                CountdownBegins = null;
                CountdownExpires = begins + Timer()?.TimeoutMs;
                Itself(() => Session.BeginCountdown());
            }
            else if (CountdownExpires is { } expires && now >= expires)
            {
                ForgetCountdown();
                Itself(() => Session.Expire());
            }
        }

        /// <summary>Delivers nothing more, moves the story no further, and disposes the session.</summary>
        public void Dispose()
        {
            HoldDue = null;
            ForgetCountdown();
            disposed = true;
            Session.Dispose();
        }

        private static ReentrantVerbException Reentrant(string verb) => new ReentrantVerbException(new Failure(Code.Reentrant,
            $"{verb}() was called while the player was delivering a batch; call it after the handler returns"));

        // What a verb does to a countdown under way: any verb that moves the
        // story drops it; a language change keeps it, the passage being the
        // same visit; a countdown begun by hand is run out on the clock.
        private enum CountdownStep
        {
            Forget,
            Keep,
            Begin,
        }

        private void Verb(string name, Func<IReadOnlyList<Effect>> run, CountdownStep countdown = CountdownStep.Forget)
        {
            if (delivering)
            {
                throw Reentrant(name);
            }
            if (disposed)
            {
                return;
            }
            HoldDue = null;
            if (countdown == CountdownStep.Forget)
            {
                ForgetCountdown();
            }
            Tell();
            var effects = run();
            if (countdown == CountdownStep.Begin && CountdownExpires == null && handlers.Countdowns && Timer() is { } timer)
            {
                CountdownBegins = null;
                CountdownExpires = clock() + timer.TimeoutMs;
            }
            Dispatch(effects);
        }

        // A verb the player makes by itself, on its clock: a failure goes to
        // OnError when there is one, and is thrown from Update otherwise.
        private void Itself(Func<IReadOnlyList<Effect>> run)
        {
            IReadOnlyList<Effect> next;
            try
            {
                Tell();
                next = run();
            }
            catch (RippleException error) when (handlers.OnError != null)
            {
                handlers.OnError(error);
                return;
            }
            Dispatch(next);
        }

        // The story's clock, before every verb that can step.
        private void Tell()
        {
            if (handlers.Now is { } now)
            {
                Session.SetNow(now());
            }
        }

        private ChoiceTimer? Timer() => (Turn as Effect.Show)?.Passage.Timer;

        private void ForgetCountdown()
        {
            CountdownBegins = null;
            CountdownExpires = null;
        }

        // A handler's error is the host's bug and not the story's: reported, and
        // the rest of the batch still delivered.
        private void Absorb(Action handler)
        {
            try
            {
                handler();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                handlers.OnError?.Invoke(error);
            }
        }

        private void Dispatch(IReadOnlyList<Effect> effects)
        {
            if (disposed)
            {
                return;
            }
            delivering = true;
            try
            {
                foreach (var effect in effects)
                {
                    if (disposed)
                    {
                        break;
                    }
                    if (effect.IsTurn)
                    {
                        Turn = effect;
                    }
                    if (effect is Effect.Audio audio && handlers.Audio is { } driver)
                    {
                        Absorb(() => driver.Apply(audio.Command));
                    }
                    if (effect is Effect.Callback fired)
                    {
                        Fire(fired.Event);
                    }
                    if (handlers.OnEffect is { } onEffect)
                    {
                        Absorb(() => onEffect(effect));
                    }
                }
            }
            finally
            {
                delivering = false;
            }
            if (effects.Count == 0 || disposed)
            {
                return;
            }
            switch (effects[effects.Count - 1])
            {
                case Effect.Hold hold:
                    HoldDue = clock() + hold.Ms;
                    break;
                case Effect.Show { Passage: { Timer: { } timer } } when handlers.Countdowns && CountdownBegins == null && CountdownExpires == null:
                    // A new visit to a timed passage; a language change's
                    // re-render kept the countdown it had.
                    CountdownBegins = clock() + timer.AfterMs;
                    break;
            }
        }

        private void Fire(CallbackEvent fired)
        {
            if (callbacks.TryGetValue(fired.Callback, out var handler))
            {
                Absorb(() => handler(fired));
            }
            else
            {
                CallbackMisses++;
            }
            foreach (var each in every.ToArray())
            {
                Absorb(() => each(fired));
            }
        }
    }
}
