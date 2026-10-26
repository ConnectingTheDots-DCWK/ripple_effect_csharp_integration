using System;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    /// <summary>
    /// What a call came to, by the engine's codes — and <see cref="Reentrant"/>,
    /// this binding's own, for a verb a <see cref="Player"/> was asked for while
    /// it was delivering.
    /// </summary>
    public enum Code
    {
        Success = 0,
        BundleFormat = 1,
        BundleVersion = 2,
        NeedsPython = 3,
        WrongTurn = 10,
        NoSuchChoice = 11,
        ChoiceSpent = 12,
        Stopped = 13,
        NothingAsked = 14,
        UnknownLocale = 15,
        NotPermitted = 16,
        StandIn = 17,
        NoCountdown = 18,
        CountdownNotBegun = 19,
        BoardGone = 20,
        NodeGone = 21,
        InvalidArgument = -1,
        Trap = -2,
        Reentrant = -100,
    }

    /// <summary>
    /// Everything the engine said about a failure. Which fields mean something
    /// depends on <see cref="Code"/>, as in the C header's <c>RippleError</c>.
    /// </summary>
    public sealed record Failure(Code Code, string Message)
    {
        /// <summary><see cref="Code.BundleFormat"/>: the document at fault.</summary>
        public string? Location { get; init; }

        /// <summary><see cref="Code.BundleVersion"/>: the bundle's format.</summary>
        public long Found { get; init; }

        /// <summary><see cref="Code.BundleVersion"/>: the newest this engine reads.</summary>
        public long Supported { get; init; }

        /// <summary><see cref="Code.BundleVersion"/>: the oldest, when there is a floor.</summary>
        public long? Oldest { get; init; }

        /// <summary><see cref="Code.WrongTurn"/>: what the story was asking (a <c>RippleTurnKind</c>).</summary>
        public uint Asked { get; init; }

        /// <summary><see cref="Code.WrongTurn"/>: the verb that answered it (a <c>RippleVerb</c>).</summary>
        public uint Answered { get; init; }

        /// <summary><see cref="Code.NoSuchChoice"/>: how many choices were offered.</summary>
        public uint Offered { get; init; }

        /// <summary><see cref="Code.NoSuchChoice"/>: the index taken.</summary>
        public uint Chosen { get; init; }

        /// <summary>
        /// The choice, locale, board or node a host or resume error is about —
        /// and for <see cref="Code.NotPermitted"/> and <see cref="Code.StandIn"/>,
        /// the kind's wire name.
        /// </summary>
        public string Subject { get; init; } = "";

        /// <summary><see cref="Code.NodeGone"/>: the node.</summary>
        public string Node { get; init; } = "";

        internal static unsafe Failure Take(RippleResult code, RippleError* error)
        {
            var failure = new Failure((Code)code, "the engine refused the call");
            if (error != null)
            {
                var message = Read.Str(error->message);
                failure = failure with
                {
                    Message = message.Length > 0 ? message : failure.Message,
                    Location = Read.Opt(error->location),
                    Found = error->found,
                    Supported = error->supported,
                    Oldest = Read.Opt(error->oldest),
                    Asked = error->asked,
                    Answered = error->answered,
                    Offered = error->offered,
                    Chosen = error->chosen,
                    Subject = Read.Str(error->subject),
                    Node = Read.Str(error->node),
                };
                Native.ripple_free(error);
            }
            return failure;
        }
    }

    /// <summary>The base of every failure thrown, carrying what the engine said.</summary>
    public class RippleException : Exception
    {
        public RippleException(Failure failure) : base(failure.Message)
        {
            Failure = failure;
        }

        public Failure Failure { get; }

        public Code Code => Failure.Code;

        internal static RippleException Of(Failure failure) => failure.Code switch
        {
            Code.BundleFormat or Code.BundleVersion => new BundleException(failure),
            Code.NeedsPython => new NeedsPythonException(failure),
            Code.WrongTurn or Code.NoSuchChoice or Code.ChoiceSpent or Code.Stopped
                or Code.NothingAsked or Code.UnknownLocale or Code.NotPermitted or Code.StandIn
                or Code.NoCountdown or Code.CountdownNotBegun => new HostException(failure),
            Code.BoardGone or Code.NodeGone => new ResumeException(failure),
            Code.Trap => new EngineTrapException(failure),
            Code.Reentrant => new ReentrantVerbException(failure),
            _ => new InvalidArgumentException(failure),
        };
    }

    /// <summary>A document of the bundle is malformed, missing, or from a newer engine.</summary>
    public sealed class BundleException : RippleException
    {
        public BundleException(Failure failure) : base(failure) { }
    }

    /// <summary>The bundle needs Python, and this is the <c>no-python</c> engine.</summary>
    public sealed class NeedsPythonException : RippleException
    {
        public NeedsPythonException(Failure failure) : base(failure) { }
    }

    /// <summary>A verb the story was not asking for, or a reading it will not take — host API §11.</summary>
    public sealed class HostException : RippleException
    {
        public HostException(Failure failure) : base(failure) { }
    }

    /// <summary>A save that cannot be reunited with this campaign.</summary>
    public sealed class ResumeException : RippleException
    {
        public ResumeException(Failure failure) : base(failure) { }
    }

    /// <summary>A malformed argument; nothing was done.</summary>
    public sealed class InvalidArgumentException : RippleException
    {
        public InvalidArgumentException(Failure failure) : base(failure) { }
    }

    /// <summary>
    /// The engine panicked, or did earlier on this handle, which is poisoned:
    /// dispose it, open again, and resume from the last checkpoint.
    /// </summary>
    public sealed class EngineTrapException : RippleException
    {
        public EngineTrapException(Failure failure) : base(failure) { }
    }

    /// <summary>
    /// A <see cref="Player"/> verb called while it was delivering a batch, from
    /// inside a handler. Schedule the verb for after the handler instead.
    /// </summary>
    public sealed class ReentrantVerbException : RippleException
    {
        public ReentrantVerbException(Failure failure) : base(failure) { }
    }

    /// <summary>
    /// The engine library could not be loaded, or is another contract's. The
    /// managed package carries no engine: reference <c>RippleFx.Engine.Native</c>
    /// (with Python) or <c>RippleFx.Engine.Native.NoPython</c>, exactly one.
    /// </summary>
    public sealed class EngineNotFoundException : Exception
    {
        public EngineNotFoundException(string message, Exception? inner = null) : base(message, inner) { }
    }

    internal static unsafe class Check
    {
        public static void Ok(RippleResult code, RippleError* error)
        {
            if (code != RippleResult.RIPPLE_SUCCESS)
            {
                throw RippleException.Of(Failure.Take(code, error));
            }
        }
    }
}
