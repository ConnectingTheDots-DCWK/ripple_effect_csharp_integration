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
    }

    /// <summary>
    /// The loop over a session, written once. It delivers every effect in
    /// order, keeps the Turn the last one named, answers a hold from its clock
    /// when <see cref="Update"/> finds it due, applies the sound to the driver,
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

        public void SetLocale(string locale) => Verb(nameof(SetLocale), () => Session.SetLocale(locale));

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
            var paused = Session.Pause();
            Dispatch(paused.Effects);
            return paused.Save;
        }

        /// <summary>What the host calls from its loop: answers a hold whose time has come.</summary>
        public void Update()
        {
            if (HoldDue is not { } due || clock() < due || delivering)
            {
                return;
            }
            HoldDue = null;
            IReadOnlyList<Effect> next;
            try
            {
                next = Session.Elapsed();
            }
            catch (RippleException error) when (handlers.OnError != null)
            {
                handlers.OnError(error);
                return;
            }
            Dispatch(next);
        }

        /// <summary>Delivers nothing more, moves the story no further, and disposes the session.</summary>
        public void Dispose()
        {
            HoldDue = null;
            disposed = true;
            Session.Dispose();
        }

        private static ReentrantVerbException Reentrant(string verb) => new ReentrantVerbException(new Failure(Code.Reentrant,
            $"{verb}() was called while the player was delivering a batch; call it after the handler returns"));

        private void Verb(string name, Func<IReadOnlyList<Effect>> run)
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
            Dispatch(run());
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
            if (effects.Count > 0 && !disposed && effects[effects.Count - 1] is Effect.Hold hold)
            {
                HoldDue = clock() + hold.Ms;
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
