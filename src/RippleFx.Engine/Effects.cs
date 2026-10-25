using System.Collections.Generic;

namespace RippleFx.Engine
{
    /// <summary>
    /// One thing a verb led to. A verb answers a batch of them in the order
    /// they are applied, and the last is a Turn — where the story now is:
    /// <see cref="Show"/>, <see cref="Ask"/>, <see cref="Hold"/>,
    /// <see cref="Permit"/>, <see cref="Ended"/>, <see cref="Failed"/> or
    /// <see cref="Stopped"/>.
    /// <code>
    /// switch (effect)
    /// {
    ///     case Effect.Show show: Present(show.Passage); break;
    ///     case Effect.Audio audio: driver.Apply(audio.Command); break;
    /// }
    /// </code>
    /// </summary>
    public abstract record Effect
    {
        private Effect()
        {
        }

        /// <summary>Whether this is a Turn: where the story now is.</summary>
        public virtual bool IsTurn => false;

        /// <summary>Whether this is the end of the story: ended, failed or stopped.</summary>
        public virtual bool IsOver => false;

        /// <summary>One event of the run's trace, when the session traces.</summary>
        public sealed record Trace(RunEvent Event) : Effect;

        /// <summary>A named callback the author declared, fired.</summary>
        public sealed record Callback(CallbackEvent Event) : Effect;

        /// <summary>The story moved to another board, by index into the manifest's boards.</summary>
        public sealed record Jumped(uint Board, string? Node) : Effect;

        /// <summary>The soundscape now in force; null for silence.</summary>
        public sealed record AmbienceChanged(Ambience? Ambience) : Effect;

        /// <summary>A command for the sound: give it to a <see cref="SoundscapeDriver"/>.</summary>
        public sealed record Audio(AudioCommand Command) : Effect;

        /// <summary>Which stores moved; a store that did not is null.</summary>
        public sealed record Changed(IReadOnlyList<Entity>? Entities, Memory? Memory, IReadOnlyList<CvarSnapshot>? Cvars) : Effect;

        /// <summary>
        /// A read of the world outside the story that did not get the real
        /// value — once per kind and outcome in a playthrough, before the Turn
        /// of the verb that read it. <c>Code</c> is the playthrough family's —
        /// <c>I.RT.05</c> a stand-in, <c>W.RT.06</c> a refusal, <c>I.RT.07</c>
        /// nothing — and its letter is the severity; <c>Node</c> is the node
        /// whose read it was, and <c>Message</c> the engine's English sentence.
        /// </summary>
        public sealed record Notice(string Code, PermissionKind Kind, string? Node, string Message) : Effect;

        /// <summary>
        /// A Turn: the passage, answered with <c>Choose</c> or <c>Leave</c>
        /// — or, when it carries a <see cref="Passage.Timer"/>, by the host's
        /// clock with <c>BeginCountdown</c> and <c>Expire</c>.
        /// </summary>
        public sealed record Show(Passage Passage) : Effect
        {
            public override bool IsTurn => true;
        }

        /// <summary>A Turn: a form, answered with <c>Answer</c>.</summary>
        public sealed record Ask(Prompt Prompt) : Effect
        {
            public override bool IsTurn => true;
        }

        /// <summary>A Turn: a wait, answered with <c>Elapsed</c> once the host's clock says so.</summary>
        public sealed record Hold(ulong Ms) : Effect
        {
            public override bool IsTurn => true;
        }

        /// <summary>
        /// A Turn before the story begins: it reads these kinds and nobody has
        /// answered. Answer each with <c>Permit</c>; the answer to the last one
        /// begins the story. A host that already knows its answers gives them
        /// before <c>Start</c>, and never sees this.
        /// </summary>
        public sealed record Permit(IReadOnlyList<PermissionKind> Kinds) : Effect
        {
            public override bool IsTurn => true;
        }

        /// <summary>
        /// A Turn: the story is over. <c>RanOut</c> is true when it ran out of
        /// road rather than reaching an End.
        /// </summary>
        public sealed record Ended(string? Title, bool RanOut) : Effect
        {
            public override bool IsTurn => true;

            public override bool IsOver => true;
        }

        /// <summary>A Turn: the story could not go on.</summary>
        public sealed record Failed(string Message, string? Node) : Effect
        {
            public override bool IsTurn => true;

            public override bool IsOver => true;
        }

        /// <summary>A Turn: the session stopped.</summary>
        public sealed record Stopped : Effect
        {
            public override bool IsTurn => true;

            public override bool IsOver => true;
        }
    }
}
