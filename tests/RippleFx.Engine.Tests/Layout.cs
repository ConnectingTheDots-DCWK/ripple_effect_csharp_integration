// Every struct of the engine's header, as .NET lays out the generated
// declaration on this machine, held to how clang laid out the header itself
// (layout.json, written by scripts/generate.sh): the size, and each field's
// offset. A generator that got a union, a bool or a padding rule wrong would
// pass every type check and corrupt every call; this is where it shows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine.Tests
{
    public class Layout
    {
        private static readonly Dictionary<string, Record> Clang = Load();

        private static string[] Records() => Clang.Keys.ToArray();

        [TestCaseSource(nameof(Records))]
        public void TheStructIsLaidOutAsTheHeaderIs(string name)
        {
            var record = Clang[name];
            var type = typeof(Native).Assembly.GetType("RippleFx.Engine.Interop." + name);
            Assert.That(type, Is.Not.Null, $"{name} is in the header and not in Native.g.cs");
            Assert.That(Marshal.SizeOf(type!), Is.EqualTo(record.Size), $"sizeof({name})");
            var fields = type!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Select(field => field.Name), Is.EquivalentTo(record.Fields.Keys), $"{name}'s fields");
            foreach (var (field, offset) in record.Fields)
            {
                Assert.That((int)Marshal.OffsetOf(type, field), Is.EqualTo(offset), $"offsetof({name}, {field})");
            }
        }

        [Test]
        public void EveryGeneratedStructWithFieldsIsInTheRecord()
        {
            var generated = typeof(Native).Assembly.GetTypes()
                .Where(type => type.Namespace == "RippleFx.Engine.Interop" && type.IsValueType && !type.IsEnum
                    && type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length > 0)
                .Select(type => type.Name);
            Assert.That(Clang.Keys, Is.SupersetOf(generated));
        }

        private sealed record Record(int Size, Dictionary<string, int> Fields);

        private static Dictionary<string, Record> Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "layout.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.EnumerateObject().ToDictionary(
                entry => entry.Name,
                entry => new Record(
                    entry.Value.GetProperty("size").GetInt32(),
                    entry.Value.GetProperty("fields").EnumerateObject().ToDictionary(field => field.Name, field => field.Value.GetInt32())));
        }
    }
}
