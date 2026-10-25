using System;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    /// <summary>The engine library this process loaded, and what it binds.</summary>
    public static unsafe class RippleEngine
    {
        /// <summary>The contract these bindings bind: <c>MAJOR.MINOR</c>.</summary>
        public const string BoundContract = "1.6";

        private static readonly object Gate = new object();
        private static volatile bool checkedOnce;

        /// <summary>The engine library's version, <c>1.6.0</c> — the one loaded, not the one built against.</summary>
        public static string Version
        {
            get
            {
                EnsureLoaded();
                return Read.Str(Native.ripple_engine_version());
            }
        }

        /// <summary>The contract the loaded library binds, <c>1.6</c>.</summary>
        public static string Contract
        {
            get
            {
                EnsureLoaded();
                return Read.Str(Native.ripple_engine_contract());
            }
        }

        /// <summary>Whether the loaded library runs scripts: false for the <c>no-python</c> one.</summary>
        public static bool HasPython
        {
            get
            {
                EnsureLoaded();
                return Native.ripple_engine_has_python() != 0;
            }
        }

        /// <summary>
        /// How many things the engine handed out and has not had back. Zero
        /// whenever nothing of this binding is mid-call: everything it reads, it
        /// copies and frees before a verb returns.
        /// </summary>
        public static long Outstanding
        {
            get
            {
                EnsureLoaded();
                return (long)Native.ripple_outstanding();
            }
        }

        /// <summary>
        /// Loads the library and checks it binds <see cref="BoundContract"/>,
        /// once; every handle's constructor calls it, so the first failure is
        /// this one, saying what to reference, rather than a bare
        /// <see cref="DllNotFoundException"/> from deep inside a verb.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (checkedOnce)
            {
                return;
            }
            lock (Gate)
            {
                if (checkedOnce)
                {
                    return;
                }
                string contract;
                try
                {
                    contract = Read.Str(Native.ripple_engine_contract());
                }
                catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    throw new EngineNotFoundException(
                        "The Ripple Effect engine library (ripple_effect_engine) could not be loaded for this platform. "
                        + "Reference exactly one of RippleFx.Engine.Native (with Python) or "
                        + "RippleFx.Engine.Native.NoPython alongside RippleFx.Engine.",
                        error);
                }
                if (contract != BoundContract)
                {
                    throw new EngineNotFoundException(
                        $"The engine library loaded binds contract {contract}, and these bindings bind {BoundContract}. "
                        + "Reference the RippleFx.Engine.Native package of the same MAJOR.MINOR.");
                }
                checkedOnce = true;
            }
        }
    }
}
