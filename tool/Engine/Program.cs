// Where the engine comes from: the pinned release, verified by digest, or a
// ripple_effect checkout built with cargo. The C# twin of the C/C++ binding's
// cmake/RippleEngine.cmake, written as a program because MSBuild reads no
// JSON and unpacks no tar.
//
//   fetch --rid <rid> [--variant python|no-python] [--conformance]
//       one target's archive into out/engine/<variant>/<rid>/, and with
//       --conformance the fixtures into out/engine/conformance/
//   stage --variant <v> --out <dir>
//       every target the native package ships, laid out as NuGet's
//       runtimes/<rid>/native/, with the engine's NOTICE
//
// RIPPLE_ENGINE_TREE=<a ripple_effect checkout> builds `fetch`'s target there
// instead, and marks it UNVERIFIED: an unreleased engine, never a release.
//
// engine.json is the trust anchor here. Nothing in .NET checks an Ed25519
// signature without a dependency, so the manifest's is checked when the pin
// is written (scripts/pin-engine.sh) and in CI, and a configure trusts the
// committed digests and refuses anything else.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RippleFx.EngineTool;

internal static class Program
{
    // NuGet's runtime identifiers, and the engine's name for each.
    internal static readonly IReadOnlyDictionary<string, string> Triples = new Dictionary<string, string>
    {
        ["linux-x64"] = "x86_64-unknown-linux-gnu",
        ["linux-arm64"] = "aarch64-unknown-linux-gnu",
        ["osx-x64"] = "x86_64-apple-darwin",
        ["osx-arm64"] = "aarch64-apple-darwin",
        ["win-x64"] = "x86_64-pc-windows-msvc",
        ["win-arm64"] = "aarch64-pc-windows-msvc",
        ["android-arm64"] = "aarch64-linux-android",
        ["android-x64"] = "x86_64-linux-android",
        ["ios-arm64"] = "aarch64-apple-ios",
        ["iossimulator-arm64"] = "aarch64-apple-ios-sim",
    };

    // What a target's archive holds that the package ships: the shared
    // library, or — where the platform links statically — the static one.
    internal static string[] Libraries(string rid) => rid switch
    {
        _ when rid.StartsWith("win-", StringComparison.Ordinal) => new[] { "ripple_effect_engine.dll" },
        _ when rid.StartsWith("osx-", StringComparison.Ordinal) => new[] { "libripple_effect_engine.dylib" },
        _ when rid.StartsWith("ios", StringComparison.Ordinal) => new[] { "libripple_effect_engine.a", "native-static-libs.txt" },
        _ => new[] { "libripple_effect_engine.so" },
    };

    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var root = FindRoot();
            var pin = Pin.Read(Path.Combine(root, "engine.json"));
            switch (options.Command)
            {
                case "fetch":
                    Fetch(root, pin, options);
                    return 0;
                case "stage":
                    Stage(root, pin, options);
                    return 0;
                default:
                    throw new Failure("usage: fetch --rid <rid> [--variant v] [--conformance] | stage --variant v --out <dir>");
            }
        }
        catch (Failure failure)
        {
            Console.Error.WriteLine("engine: " + failure.Message);
            return 1;
        }
    }

    private static void Fetch(string root, Pin pin, Options options)
    {
        var rid = options.Require("rid");
        var variant = options.Variant;
        var into = Path.Combine(root, "out", "engine", variant, rid);
        var tree = Environment.GetEnvironmentVariable("RIPPLE_ENGINE_TREE");
        if (!string.IsNullOrEmpty(tree))
        {
            // The tree's own fixtures are read in place (the tests look there
            // whenever RIPPLE_ENGINE_TREE is set), so there is nothing to unpack.
            FromTree(tree!, rid, variant, into);
            return;
        }
        var archive = pin.Archive(variant, Triple(rid));
        Unpack(root, pin, archive, into);
        if (options.Has("conformance"))
        {
            Unpack(root, pin, pin.Conformance, Path.Combine(root, "out", "engine", "conformance"));
        }
    }

    private static void Stage(string root, Pin pin, Options options)
    {
        var variant = options.Variant;
        var output = Path.GetFullPath(options.Require("out"));
        foreach (var (rid, triple) in Triples)
        {
            var unpacked = Path.Combine(root, "out", "engine", variant, rid);
            Unpack(root, pin, pin.Archive(variant, triple), unpacked);
            var native = Path.Combine(output, "runtimes", rid, "native");
            Directory.CreateDirectory(native);
            foreach (var library in Libraries(rid))
            {
                var from = Path.Combine(unpacked, "lib", library);
                if (!File.Exists(from))
                {
                    throw new Failure($"{triple}'s archive has no lib/{library}");
                }
                File.Copy(from, Path.Combine(native, library), overwrite: true);
            }
            if (rid == "linux-x64")
            {
                // Every target's notice lists the same software; only its first
                // line names the triple, so the package's names them all.
                var notice = File.ReadAllText(Path.Combine(unpacked, "NOTICE"))
                    .Replace($"({triple}, C ABI)", "(C ABI, every target in this package)", StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(output, "ENGINE-NOTICE.txt"), notice);
            }
        }
        Console.WriteLine($"staged {Triples.Count} targets of engine {pin.Engine} ({variant}) into {output}");
    }

    private static string Triple(string rid) =>
        Triples.TryGetValue(rid, out var triple)
            ? triple
            : throw new Failure($"the engine ships no build for {rid}; it ships for {string.Join(", ", Triples.Keys)}");

    // Downloads `archive` unless it is already unpacked from the same digest,
    // refusing it unless its SHA-256 is the pinned one.
    private static void Unpack(string root, Pin pin, Archive archive, string into)
    {
        var marker = Path.Combine(into, ".sha256");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == archive.Sha256)
        {
            return;
        }
        var downloads = Path.Combine(root, "out", "engine", "downloads");
        Directory.CreateDirectory(downloads);
        var file = Path.Combine(downloads, archive.File);
        if (!File.Exists(file) || Digest(file) != archive.Sha256)
        {
            Download(pin, archive.File, file);
        }
        var have = Digest(file);
        if (have != archive.Sha256)
        {
            File.Delete(file);
            throw new Failure($"{archive.File} has SHA-256 {have}, and engine.json pins {archive.Sha256}; refused");
        }
        if (Directory.Exists(into))
        {
            Directory.Delete(into, recursive: true);
        }
        Directory.CreateDirectory(into);
        using (var stream = File.OpenRead(file))
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(gzip, into, overwriteFiles: true);
        }
        File.WriteAllText(marker, archive.Sha256 + "\n");
    }

    private static void Download(Pin pin, string name, string to)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var tag = "engine-v" + pin.Engine;
        var tried = new List<string>();
        foreach (var template in pin.Urls)
        {
            var url = template.Replace("{tag}", tag, StringComparison.Ordinal).Replace("{file}", name, StringComparison.Ordinal);
            try
            {
                using var response = http.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    tried.Add($"{url}: {(int)response.StatusCode}");
                    continue;
                }
                var partial = to + ".part";
                using (var output = File.Create(partial))
                {
                    response.Content.CopyToAsync(output).GetAwaiter().GetResult();
                }
                File.Move(partial, to, overwrite: true);
                Console.Error.WriteLine($"engine: downloaded {name}");
                return;
            }
            catch (HttpRequestException error)
            {
                tried.Add($"{url}: {error.Message}");
            }
        }
        throw new Failure($"could not download {name} for {tag}:\n  " + string.Join("\n  ", tried));
    }

    private static string Digest(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // Builds the C ABI in a ripple_effect checkout and lays it out as an
    // archive would be, so everything after reads one layout: `target/` with
    // the interpreter, `target/no-python/` without, as the engine's release.
    private static void FromTree(string tree, string rid, string variant, string into)
    {
        var triple = Triple(rid);
        var targetDir = Path.Combine(tree, "target");
        var cargoArgs = new List<string> { "build", "--quiet", "--release", "-p", "ripple-capi", "--lib", "--target", triple };
        if (variant == "no-python")
        {
            targetDir = Path.Combine(tree, "target", "no-python");
            cargoArgs.AddRange(new[] { "--target-dir", targetDir, "--no-default-features" });
        }
        Console.Error.WriteLine($"engine: building {variant} for {triple} in {tree} (UNVERIFIED)");
        var cargo = Environment.GetEnvironmentVariable("CARGO") ?? "cargo";
        var start = new ProcessStartInfo(cargo) { WorkingDirectory = tree };
        foreach (var arg in cargoArgs)
        {
            start.ArgumentList.Add(arg);
        }
        using (var process = Process.Start(start) ?? throw new Failure("could not run cargo"))
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Failure($"cargo could not build the engine in {tree}");
            }
        }
        var built = Path.Combine(targetDir, triple, "release");
        if (Directory.Exists(into))
        {
            Directory.Delete(into, recursive: true);
        }
        Directory.CreateDirectory(Path.Combine(into, "lib"));
        Directory.CreateDirectory(Path.Combine(into, "include", "ripple"));
        File.Copy(Path.Combine(tree, "crates", "ripple-capi", "include", "ripple", "ripple.h"),
            Path.Combine(into, "include", "ripple", "ripple.h"));
        foreach (var name in new[] { "libripple_effect_engine.so", "libripple_effect_engine.dylib", "libripple_effect_engine.a", "ripple_effect_engine.dll" })
        {
            var from = Path.Combine(built, name);
            if (File.Exists(from))
            {
                File.Copy(from, Path.Combine(into, "lib", name));
            }
        }
        File.WriteAllText(Path.Combine(into, "UNVERIFIED"), $"built from {tree}, not a release\n");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "engine.json")) && File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }
        var here = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(here, "engine.json")) ? here : throw new Failure("run from the repository: no engine.json above " + AppContext.BaseDirectory);
    }
}

internal sealed class Failure : Exception
{
    public Failure(string message) : base(message) { }
}

internal sealed record Archive(string File, string Sha256);

internal sealed class Pin
{
    private readonly JsonElement archives;

    private Pin(JsonElement root)
    {
        Engine = root.GetProperty("engine").GetString() ?? throw new Failure("engine.json names no engine");
        Urls = root.GetProperty("urls").EnumerateArray().Select(url => url.GetString()!).ToArray();
        archives = root.GetProperty("archives");
    }

    public string Engine { get; }

    public IReadOnlyList<string> Urls { get; }

    public Archive Conformance => Read(archives.GetProperty("conformance"), "the conformance fixtures");

    public static Pin Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new Pin(document.RootElement.Clone());
    }

    public Archive Archive(string variant, string triple)
    {
        if (!archives.TryGetProperty(variant, out var targets) || !targets.TryGetProperty(triple, out var archive))
        {
            throw new Failure($"engine.json pins no {variant} archive for {triple}");
        }
        return Read(archive, $"{triple} ({variant})");
    }

    private static Archive Read(JsonElement entry, string what)
    {
        var sha = entry.GetProperty("sha256");
        if (sha.ValueKind == JsonValueKind.Null)
        {
            throw new Failure($"engine.json pins no release yet for {what}: set RIPPLE_ENGINE_TREE to a ripple_effect checkout");
        }
        return new Archive(entry.GetProperty("file").GetString()!, sha.GetString()!);
    }
}

internal sealed class Options
{
    private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);

    public string Command { get; private set; } = "";

    public string Variant
    {
        get
        {
            var variant = values.TryGetValue("variant", out var given) && given != null ? given : "python";
            return variant is "python" or "no-python" ? variant : throw new Failure($"--variant is python or no-python, not {variant}");
        }
    }

    public static Options Parse(string[] args)
    {
        var options = new Options { Command = args.Length > 0 ? args[0] : "" };
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new Failure($"unexpected argument {args[i]}");
            }
            var name = args[i][2..];
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            options.values[name] = value;
        }
        return options;
    }

    public bool Has(string name) => values.ContainsKey(name);

    public string Require(string name) =>
        values.TryGetValue(name, out var value) && value != null ? value : throw new Failure($"--{name} is required");
}
