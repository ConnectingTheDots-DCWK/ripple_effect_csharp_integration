// A value through the arena and back, through the engine where it can be: an
// Int at the edge of 64 bits, a Float that is not a number, a map in the order
// it was written.

using System;
using NUnit.Framework;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine.Tests
{
    public unsafe class Values
    {
        [Test]
        public void AnArenaReadsBackAsItWasWritten()
        {
            var written = Value.Map(
                ("zeta", long.MaxValue),
                ("alpha", double.NaN),
                ("list", Value.List(1, 1.0, "é")),
                ("nothing", Value.Null));
            using var scratch = new Scratch();
            var read = Arena.Read(*Arena.Write(scratch, written));
            var map = read.AsMap();
            Assert.That(map, Has.Count.EqualTo(4));
            Assert.That(map[0].Key, Is.EqualTo("zeta"));
            Assert.That(map[1].Key, Is.EqualTo("alpha"));
            Assert.That(map[0].Value.AsInt(), Is.EqualTo(long.MaxValue));
            Assert.That(double.IsNaN(map[1].Value.AsFloat()), Is.True);
            var list = map[2].Value.AsList();
            Assert.That(list[0].Kind, Is.EqualTo(ValueKind.Int));
            Assert.That(list[1].Kind, Is.EqualTo(ValueKind.Float));
            Assert.That(list[2].AsText(), Is.EqualTo("é"));
            Assert.That(map[3].Value.IsNull, Is.True);
            Assert.That(read, Is.EqualTo(written));
        }

        [Test]
        public void TheEngineReadsAnIntAndAFloatApart()
        {
            Assert.That(Value.Int(long.MaxValue).ToStoryText(), Is.EqualTo("9223372036854775807"));
            Assert.That(Value.Int(7).ToStoryText(), Is.Not.EqualTo(Value.Float(7.5).ToStoryText()));
        }

        [Test]
        public void AHostileArenaReadsAsNull()
        {
            // A list naming itself, and a child past the end.
            var nodes = stackalloc RippleValueNode[2];
            var children = stackalloc uint[] { 0, 1, 9 };
            nodes[0] = new RippleValueNode { kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_ITEMS };
            nodes[0].data.items = new RippleIndices { ptr = children, count = (UIntPtr)3 };
            nodes[1] = new RippleValueNode { kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_INTEGER };
            nodes[1].data.integer = 7;
            var list = Arena.Read(new RippleValue { nodes = nodes, count = (UIntPtr)2 }).AsList();
            Assert.That(list[0].IsNull, Is.True);
            Assert.That(list[1].AsInt(), Is.EqualTo(7));
            Assert.That(list[2].IsNull, Is.True);
        }
    }
}
