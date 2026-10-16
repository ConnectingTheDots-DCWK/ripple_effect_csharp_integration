using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RippleFx.Engine.Tests
{
    internal static class Fixtures
    {
        /// <summary>The repository, as the build named it.</summary>
        public static string Root { get; } = typeof(Fixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RippleRoot").Value!;

        /// <summary>
        /// The conformance fixtures: a RIPPLE_ENGINE_TREE's own when one is set,
        /// as the engine was built from it, and the pinned release's otherwise.
        /// </summary>
        public static string Directory
        {
            get
            {
                var tree = Environment.GetEnvironmentVariable("RIPPLE_ENGINE_TREE");
                return string.IsNullOrEmpty(tree)
                    ? Path.Combine(Root, "out", "engine", "conformance", "fixtures")
                    : Path.Combine(tree, "packages", "ripple_runtime", "test", "fixtures");
            }
        }

        public static IReadOnlyList<string> All() => System.IO.Directory.GetDirectories(Directory)
            .Where(fixture => File.Exists(Path.Combine(fixture, "input.json")))
            .OrderBy(fixture => fixture, StringComparer.Ordinal)
            .ToArray();

        public static string Bundle(string name) => Path.Combine(Directory, name, "bundle");
    }

    internal static class Json
    {
        private static readonly char[] FloatMarks = { '.', 'e', 'E' };

        /// <summary>A JSON file as a Value: a number with a point or an exponent is a Float, any other an Int.</summary>
        public static Value Read(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return From(document.RootElement);
        }

        private static Value From(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Null => Value.Null,
            JsonValueKind.True => Value.Bool(true),
            JsonValueKind.False => Value.Bool(false),
            JsonValueKind.String => Value.Text(element.GetString()!),
            JsonValueKind.Number => element.GetRawText().IndexOfAny(FloatMarks) >= 0
                ? Value.Float(element.GetDouble())
                : Value.Int(element.GetInt64()),
            JsonValueKind.Array => Value.List(element.EnumerateArray().Select(From)),
            _ => Value.Map(element.EnumerateObject().Select(property => new KeyValuePair<string, Value>(property.Name, From(property.Value)))),
        };

        /// <summary>
        /// A value as text that keeps what the fixtures assert: an Int and a Float
        /// of the same size differ, and an object's key order does not count. The
        /// C# twin of the JS binding's canonical.ts.
        /// </summary>
        public static string Canonical(Value value)
        {
            switch (value.Kind)
            {
                case ValueKind.Null:
                    return "null";
                case ValueKind.Bool:
                    return value.AsBool() ? "true" : "false";
                case ValueKind.Int:
                    return value.AsInt().ToString(CultureInfo.InvariantCulture);
                case ValueKind.Float:
                    {
                        var real = value.AsFloat();
                        if (!double.IsNaN(real) && !double.IsInfinity(real) && Math.Floor(real) == real && Math.Abs(real) < 1e15)
                        {
                            return real.ToString("F1", CultureInfo.InvariantCulture);
                        }
                        return real.ToString("R", CultureInfo.InvariantCulture);
                    }
                case ValueKind.Text:
                    return JsonSerializer.Serialize(value.AsText());
                case ValueKind.List:
                    return "[" + string.Join(",", value.AsList().Select(Canonical)) + "]";
                default:
                    return "{" + string.Join(",", value.AsMap()
                        .Select(entry => (Key: JsonSerializer.Serialize(entry.Key), Value: Canonical(entry.Value)))
                        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                        .ThenBy(entry => entry.Value, StringComparer.Ordinal)
                        .Select(entry => entry.Key + ":" + entry.Value)) + "}";
            }
        }
    }
}
