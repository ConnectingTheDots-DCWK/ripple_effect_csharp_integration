#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // What C# 9's `init` and records need, and netstandard2.1 lacks.
    internal static class IsExternalInit
    {
    }
}
#endif
