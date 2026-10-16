#if NET8_0_OR_GREATER
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine.Internal
{
    /// <summary>
    /// Points <c>[DllImport("ripple_effect_engine")]</c> at the right image on
    /// iOS. There the engine is a static library linked into the app's single
    /// executable rather than a dylib Mono can <c>dlopen</c> by name, so the
    /// P/Invoke would otherwise fail with <see cref="DllNotFoundException"/>
    /// even though the symbols are present; this resolves the name to the main
    /// program image, where the native package's <c>ForceLoad</c> keeps them.
    /// On every other platform the engine is the dylib the native package ships
    /// under <c>runtimes/&lt;rid&gt;/native/</c>, so the resolver steps aside
    /// and the default search loads it.
    /// </summary>
    /// <remarks>
    /// net8.0 only: <see cref="NativeLibrary"/> is a .NET Core 3 API absent from
    /// netstandard2.1, which Unity compiles — and Unity's IL2CPP links the
    /// engine statically and resolves the C names itself, so it needs none of
    /// this.
    /// </remarks>
    internal static class NativeLibraryResolver
    {
        [ModuleInitializer]
        internal static void Register() =>
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
            libraryName == "ripple_effect_engine" && OperatingSystem.IsIOS()
                ? NativeLibrary.GetMainProgramHandle()
                : IntPtr.Zero;
    }
}
#endif
