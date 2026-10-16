using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    /// <summary>One file of a bundle held in memory, by its path inside the bundle.</summary>
    public sealed record BundleFile(string Path, byte[] Bytes);

    /// <summary>
    /// A bundle, opened. Immutable, and shared by every session made from it;
    /// it may be disposed before they are, and they go on working.
    /// </summary>
    public sealed unsafe class Campaign : IDisposable
    {
        private readonly CampaignHandle handle;

        private Campaign(CampaignHandle handle, string? folder)
        {
            this.handle = handle;
            Folder = folder;
        }

        /// <summary>The folder it was opened from; null for a bundle in memory.</summary>
        public string? Folder { get; }

        /// <summary>
        /// The engine's handle, for a call this binding does not wrap. It stays
        /// good while this campaign is neither disposed nor collected.
        /// </summary>
        public IntPtr DangerousHandle => handle.DangerousGetHandle();

        // A disposed handle still answers its old pointer; the engine must never see it.
        internal RippleCampaign* Raw => handle.IsClosed
            ? throw new ObjectDisposedException(nameof(Campaign))
            : (RippleCampaign*)handle.DangerousGetHandle();

        /// <summary>
        /// Opens the bundle in <paramref name="folder"/>, the one that holds
        /// <c>campaign.json</c>. Files are read as the story asks for them, and
        /// never from outside the folder.
        /// </summary>
        public static Campaign OpenDirectory(string folder)
        {
            RippleEngine.EnsureLoaded();
            using var scratch = new Scratch();
            RippleCampaign* raw = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_open_dir(scratch.Str(folder), &raw, &error), error);
            return new Campaign(new CampaignHandle((IntPtr)raw), folder);
        }

        /// <summary>
        /// Opens a bundle held in memory: every file of it, by its path inside
        /// the bundle. The bytes are copied. On the web and on a console, where
        /// a folder is not what the platform has, this is the way in.
        /// </summary>
        public static Campaign OpenFiles(IReadOnlyList<BundleFile> files)
        {
            RippleEngine.EnsureLoaded();
            using var scratch = new Scratch();
            var table = scratch.Array<RippleFile>(files.Count);
            for (var i = 0; i < files.Count; i++)
            {
                table[i].path = scratch.Str(files[i].Path);
                table[i].bytes = scratch.Bytes(files[i].Bytes);
                table[i].len = (UIntPtr)files[i].Bytes.Length;
            }
            RippleCampaign* raw = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_open_memory(table, (UIntPtr)files.Count, &raw, &error), error);
            return new Campaign(new CampaignHandle((IntPtr)raw), null);
        }

        /// <summary><c>campaign.json</c>, read afresh on every call: keep it rather than ask again.</summary>
        public Manifest Manifest
        {
            get
            {
                RippleManifest* output = null;
                RippleError* error = null;
                Check.Ok(Native.ripple_campaign_manifest(Raw, &output, &error), error);
                try
                {
                    return From.Manifest(output);
                }
                finally
                {
                    Release(output);
                }
            }
        }

        public Board Board(uint index)
        {
            RippleBoard* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_board(Raw, index, &output, &error), error);
            try
            {
                return From.Board(output);
            }
            finally
            {
                Release(output);
            }
        }

        public Soundscape Soundscape(uint index)
        {
            RippleSoundscape* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_soundscape(Raw, index, &output, &error), error);
            try
            {
                return From.Soundscape(output);
            }
            finally
            {
                Release(output);
            }
        }

        /// <summary>A board's index by its id; null when there is no such board.</summary>
        public uint? BoardWithId(string id)
        {
            using var scratch = new Scratch();
            RippleOptionalU32 output = default;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_board_with_id(Raw, scratch.Str(id), &output, &error), error);
            GC.KeepAlive(this);
            return Read.Opt(output);
        }

        /// <summary>A node of a board, for when a trace names one; null when there is none.</summary>
        public Node? Node(uint board, string id)
        {
            using var scratch = new Scratch();
            RippleNode* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_node(Raw, board, scratch.Str(id), &output, &error), error);
            try
            {
                return output == null ? null : From.Node(output);
            }
            finally
            {
                Release(output);
            }
        }

        /// <summary>Everything wrong with the bundle the engine can see without playing it.</summary>
        public IReadOnlyList<BundleProblem> Validate()
        {
            RippleBundleProblems* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_validate(Raw, &output, &error), error);
            try
            {
                return Read.List(output->items, output->count, item =>
                    new BundleProblem(Read.Str(((RippleBundleProblem*)item)->message), Read.Str(((RippleBundleProblem*)item)->location)));
            }
            finally
            {
                Release(output);
            }
        }

        /// <summary>A medium's path inside the bundle; null when there is no such medium.</summary>
        public string? MediaPath(uint index)
        {
            RippleStr* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_media_path(Raw, index, &output, &error), error);
            try
            {
                return output == null ? null : Read.Str(*output);
            }
            finally
            {
                Release(output);
            }
        }

        /// <summary>
        /// A medium's file on disk — the folder joined with its path — for a
        /// campaign opened from a folder; null otherwise.
        /// </summary>
        public string? MediaFile(uint index)
        {
            if (Folder == null)
            {
                return null;
            }
            var path = MediaPath(index);
            return path == null ? null : System.IO.Path.Combine(Folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        /// <summary>A medium's bytes, for a packed asset system; null when there is no such medium.</summary>
        public byte[]? MediaBytes(uint index)
        {
            RippleBytes* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_campaign_media_bytes(Raw, index, &output, &error), error);
            try
            {
                if (output == null)
                {
                    return null;
                }
                var bytes = new byte[checked((int)output->len)];
                if (bytes.Length > 0)
                {
                    Marshal.Copy((IntPtr)output->ptr, bytes, 0, bytes.Length);
                }
                return bytes;
            }
            finally
            {
                Release(output);
            }
        }

        public void Dispose() => handle.Dispose();

        private void Release(void* owned)
        {
            Native.ripple_free(owned);
            GC.KeepAlive(this);
        }
    }

    internal sealed class CampaignHandle : SafeHandle
    {
        public CampaignHandle(IntPtr raw) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(raw);

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override unsafe bool ReleaseHandle()
        {
            Native.ripple_campaign_destroy((RippleCampaign*)handle);
            return true;
        }
    }
}
