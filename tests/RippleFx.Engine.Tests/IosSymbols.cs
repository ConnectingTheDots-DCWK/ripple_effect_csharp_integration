// The iOS native package links the engine statically, and names every C entry
// point in <ReferenceNativeSymbol> so the native linker keeps it past
// -dead_strip and exports it for the P/Invokes to find (see the package's
// buildTransitive .targets). That list has to be exactly the P/Invokes of
// Native.g.cs: a symbol missing from it is dead-stripped and fails only on a
// device, and a stale one fails the device link outright. This holds the two
// in step, so adding a C function the generator picks up — or a contract that
// changes the set — is caught here rather than on an iPhone.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class IosSymbols
    {
        private static readonly string Targets = Path.Combine(
            Fixtures.Root, "packaging", "RippleFx.Engine.Native", "buildTransitive", "RippleFx.Engine.Native.targets");

        private static readonly string Interop = Path.Combine(
            Fixtures.Root, "src", "RippleFx.Engine", "Interop", "Native.g.cs");

        [Test]
        public void TheSymbolListIsEveryPInvokeOfTheHeader()
        {
            var pinvokes = Names(File.ReadAllText(Interop), @"extern[^;]*?\b(ripple_\w+)\s*\(");
            var exported = Names(File.ReadAllText(Targets), "<ReferenceNativeSymbol Include=\"(ripple_\\w+)\"");
            Assert.That(pinvokes, Is.Not.Empty, "no P/Invokes found in Native.g.cs");
            Assert.That(exported, Is.EquivalentTo(pinvokes),
                "the ReferenceNativeSymbol list in RippleFx.Engine.Native.targets must be exactly Native.g.cs's P/Invokes");
        }

        private static HashSet<string> Names(string source, string pattern) =>
            Regex.Matches(source, pattern, RegexOptions.Singleline)
                .Select(match => match.Groups[1].Value)
                .ToHashSet();
    }
}
