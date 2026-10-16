using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine.Internal
{
    /// <summary>
    /// Unmanaged memory for one call's arguments — strings, tables, value
    /// arenas — all freed together when the call is over. Nothing in it moves,
    /// so a pointer taken into it stays good for as long as the scratch lives,
    /// with no pinning to get wrong.
    /// </summary>
    internal sealed unsafe class Scratch : IDisposable
    {
        private readonly List<IntPtr> blocks = new List<IntPtr>();

        public void* Allocate(int bytes)
        {
            var block = Marshal.AllocHGlobal(Math.Max(bytes, 1));
            blocks.Add(block);
            return (void*)block;
        }

        public T* Array<T>(int count) where T : unmanaged
        {
            var items = (T*)Allocate(sizeof(T) * count);
            new Span<T>(items, count).Clear();
            return items;
        }

        /// <summary>
        /// A string as the engine reads one. A present empty string has a
        /// pointer and a length of zero; <c>null</c> is absent, a NULL pointer.
        /// </summary>
        public RippleStr Str(string? text)
        {
            if (text == null)
            {
                return default;
            }
            var length = Encoding.UTF8.GetByteCount(text);
            var bytes = (byte*)Allocate(length + 1);
            fixed (char* chars = text)
            {
                Encoding.UTF8.GetBytes(chars, text.Length, bytes, length);
            }
            bytes[length] = 0;
            return new RippleStr { ptr = (sbyte*)bytes, len = (UIntPtr)length };
        }

        public byte* Bytes(byte[] bytes)
        {
            var copy = (byte*)Allocate(bytes.Length);
            if (bytes.Length > 0)
            {
                Marshal.Copy(bytes, 0, (IntPtr)copy, bytes.Length);
            }
            return copy;
        }

        public void Dispose()
        {
            foreach (var block in blocks)
            {
                Marshal.FreeHGlobal(block);
            }
            blocks.Clear();
        }
    }

    /// <summary>Reading what the engine wrote: strings, optionals, lists.</summary>
    internal static unsafe class Read
    {
        public static string Str(RippleStr text) => text.ptr == null ? "" : Decode(text);

        public static string? Opt(RippleStr text) => text.ptr == null ? null : Decode(text);

        /// <summary>A NUL-terminated string the engine keeps, such as its version.</summary>
        public static string Str(sbyte* text) => text == null ? "" : Utf8(text);

        public static uint? Opt(RippleOptionalU32 value) => value.present != 0 ? value.value : (uint?)null;

        public static ulong? Opt(RippleOptionalU64 value) => value.present != 0 ? value.value : (ulong?)null;

        public static long? Opt(RippleOptionalI64 value) => value.present != 0 ? value.value : (long?)null;

        public static IReadOnlyList<T> List<TItem, T>(TItem* items, UIntPtr count, Func<IntPtr, T> read) where TItem : unmanaged
        {
            var n = checked((int)count);
            if (n == 0 || items == null)
            {
                return System.Array.Empty<T>();
            }
            var list = new T[n];
            for (var i = 0; i < n; i++)
            {
                list[i] = read((IntPtr)(items + i));
            }
            return list;
        }

        public static IReadOnlyList<string> Texts(RippleStr* items, UIntPtr count) =>
            List(items, count, item => Str(*(RippleStr*)item));

        public static IReadOnlyList<uint> Indices(uint* items, UIntPtr count) =>
            List(items, count, item => *(uint*)item);

        private static string Decode(RippleStr text) =>
            Encoding.UTF8.GetString((byte*)text.ptr, checked((int)text.len));

        private static string Utf8(sbyte* text)
        {
            var length = 0;
            while (text[length] != 0)
            {
                length++;
            }
            return Encoding.UTF8.GetString((byte*)text, length);
        }
    }
}
