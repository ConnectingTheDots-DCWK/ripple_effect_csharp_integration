using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    /// <summary>What a <see cref="Value"/> holds.</summary>
    public enum ValueKind
    {
        Null,
        Bool,
        Int,
        Float,
        Text,
        List,
        Map,
    }

    /// <summary>
    /// A story's value: null, a bool, an Int (<see cref="long"/>), a Float
    /// (<see cref="double"/>), text, a list or a map. An Int and a Float never
    /// become each other, and a map keeps the order it was written in.
    /// Immutable; equal when it holds the same thing, a map's order included.
    /// </summary>
    public sealed class Value : IEquatable<Value>
    {
        private readonly bool truth;
        private readonly long whole;
        private readonly double real;
        private readonly string? text;
        private readonly IReadOnlyList<Value>? items;
        private readonly IReadOnlyList<KeyValuePair<string, Value>>? entries;

        private Value(ValueKind kind, bool truth = false, long whole = 0, double real = 0, string? text = null,
            IReadOnlyList<Value>? items = null, IReadOnlyList<KeyValuePair<string, Value>>? entries = null)
        {
            Kind = kind;
            this.truth = truth;
            this.whole = whole;
            this.real = real;
            this.text = text;
            this.items = items;
            this.entries = entries;
        }

        /// <summary>The null value.</summary>
        public static Value Null { get; } = new Value(ValueKind.Null);

        public ValueKind Kind { get; }

        public bool IsNull => Kind == ValueKind.Null;

        public static Value Bool(bool truth) => new Value(ValueKind.Bool, truth: truth);

        public static Value Int(long whole) => new Value(ValueKind.Int, whole: whole);

        public static Value Float(double real) => new Value(ValueKind.Float, real: real);

        public static Value Text(string text) => new Value(ValueKind.Text, text: text ?? throw new ArgumentNullException(nameof(text)));

        public static Value List(IEnumerable<Value> items) =>
            new Value(ValueKind.List, items: items.Select(item => item ?? Null).ToArray());

        public static Value List(params Value[] items) => List((IEnumerable<Value>)items);

        /// <summary>A map, in the order <paramref name="entries"/> gives.</summary>
        public static Value Map(IEnumerable<KeyValuePair<string, Value>> entries) =>
            new Value(ValueKind.Map, entries: entries.Select(entry =>
                new KeyValuePair<string, Value>(entry.Key ?? throw new ArgumentException("a map key is never null", nameof(entries)), entry.Value ?? Null)).ToArray());

        /// <summary>A map from (key, value) pairs, in the order given.</summary>
        public static Value Map(params (string Key, Value Value)[] entries) =>
            Map(entries.Select(entry => new KeyValuePair<string, Value>(entry.Key, entry.Value)));

        public static implicit operator Value(bool truth) => Bool(truth);

        public static implicit operator Value(int whole) => Int(whole);

        public static implicit operator Value(long whole) => Int(whole);

        public static implicit operator Value(double real) => Float(real);

        public static implicit operator Value(string? text) => text == null ? Null : Text(text);

        public bool AsBool() => Kind == ValueKind.Bool ? truth : throw Wrong(ValueKind.Bool);

        public long AsInt() => Kind == ValueKind.Int ? whole : throw Wrong(ValueKind.Int);

        public double AsFloat() => Kind == ValueKind.Float ? real : throw Wrong(ValueKind.Float);

        public string AsText() => Kind == ValueKind.Text ? text! : throw Wrong(ValueKind.Text);

        public IReadOnlyList<Value> AsList() => Kind == ValueKind.List ? items! : throw Wrong(ValueKind.List);

        public IReadOnlyList<KeyValuePair<string, Value>> AsMap() => Kind == ValueKind.Map ? entries! : throw Wrong(ValueKind.Map);

        /// <summary>A map's value under <paramref name="key"/>, or null — for a list or anything else too.</summary>
        public Value? Find(string key)
        {
            if (Kind != ValueKind.Map)
            {
                return null;
            }
            foreach (var entry in entries!)
            {
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        /// <summary>The value as text the way a passage prints it, asked of the engine.</summary>
        public unsafe string ToStoryText()
        {
            RippleEngine.EnsureLoaded();
            using var scratch = new Scratch();
            var view = Arena.Write(scratch, this);
            RippleStr* output = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_value_as_text(view, &output, &error), error);
            try
            {
                return Read.Str(*output);
            }
            finally
            {
                Native.ripple_free(output);
            }
        }

        public bool Equals(Value? other)
        {
            if (other is null || other.Kind != Kind)
            {
                return false;
            }
            switch (Kind)
            {
                case ValueKind.Null:
                    return true;
                case ValueKind.Bool:
                    return truth == other.truth;
                case ValueKind.Int:
                    return whole == other.whole;
                case ValueKind.Float:
                    return real.Equals(other.real);
                case ValueKind.Text:
                    return text == other.text;
                case ValueKind.List:
                    return items!.SequenceEqual(other.items!);
                default:
                    return entries!.Count == other.entries!.Count
                        && entries.Zip(other.entries, (a, b) => a.Key == b.Key && a.Value.Equals(b.Value)).All(same => same);
            }
        }

        public override bool Equals(object? obj) => obj is Value other && Equals(other);

        public override int GetHashCode() => Kind switch
        {
            ValueKind.Bool => truth.GetHashCode(),
            ValueKind.Int => whole.GetHashCode(),
            ValueKind.Float => real.GetHashCode(),
            ValueKind.Text => StringComparer.Ordinal.GetHashCode(text!),
            ValueKind.List => items!.Count,
            ValueKind.Map => entries!.Count,
            _ => 0,
        };

        /// <summary>The value as JSON-like text, for a log or a debugger: <c>7</c> and <c>7.0</c> differ.</summary>
        public override string ToString()
        {
            var built = new StringBuilder();
            Append(built);
            return built.ToString();
        }

        private void Append(StringBuilder built)
        {
            switch (Kind)
            {
                case ValueKind.Null:
                    built.Append("null");
                    break;
                case ValueKind.Bool:
                    built.Append(truth ? "true" : "false");
                    break;
                case ValueKind.Int:
                    built.Append(whole.ToString(CultureInfo.InvariantCulture));
                    break;
                case ValueKind.Float:
                    var shown = real.ToString("R", CultureInfo.InvariantCulture);
                    built.Append(shown);
                    if (!double.IsNaN(real) && !double.IsInfinity(real) && shown.IndexOfAny(new[] { '.', 'E', 'e' }) < 0)
                    {
                        built.Append(".0");
                    }
                    break;
                case ValueKind.Text:
                    built.Append('"').Append(text!.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    break;
                case ValueKind.List:
                    built.Append('[');
                    for (var i = 0; i < items!.Count; i++)
                    {
                        if (i > 0)
                        {
                            built.Append(',');
                        }
                        items[i].Append(built);
                    }
                    built.Append(']');
                    break;
                default:
                    built.Append('{');
                    for (var i = 0; i < entries!.Count; i++)
                    {
                        if (i > 0)
                        {
                            built.Append(',');
                        }
                        built.Append('"').Append(entries[i].Key).Append("\":");
                        entries[i].Value.Append(built);
                    }
                    built.Append('}');
                    break;
            }
        }

        private InvalidOperationException Wrong(ValueKind wanted) =>
            new InvalidOperationException($"the value is {Kind}, not {wanted}");
    }

    /// <summary>A value as the engine lays one out: a flat table of nodes, the root first.</summary>
    internal static unsafe class Arena
    {
        // A child that is not after its parent, or past the end, reads as null:
        // the rule that keeps an arena from looping, as the engine reads one.
        public static Value Read(RippleValue arena) => Node(arena.nodes, checked((int)arena.count), 0);

        public static Value? Read(RippleValue* arena) => arena == null ? null : Read(*arena);

        private static Value Node(RippleValueNode* nodes, int count, int at)
        {
            if (at >= count || nodes == null)
            {
                return Value.Null;
            }
            var node = nodes[at];
            Value Child(uint of) => of > at && of < count ? Node(nodes, count, (int)of) : Value.Null;
            switch ((RippleValueKind)node.kind)
            {
                case RippleValueKind.RIPPLE_VALUE_KIND_BOOLEAN:
                    return Value.Bool(node.data.boolean != 0);
                case RippleValueKind.RIPPLE_VALUE_KIND_INTEGER:
                    return Value.Int(node.data.integer);
                case RippleValueKind.RIPPLE_VALUE_KIND_REAL:
                    return Value.Float(node.data.real);
                case RippleValueKind.RIPPLE_VALUE_KIND_TEXT:
                    return Value.Text(Internal.Read.Str(node.data.text));
                case RippleValueKind.RIPPLE_VALUE_KIND_ITEMS:
                    {
                        var n = checked((int)node.data.items.count);
                        var list = new Value[n];
                        for (var i = 0; i < n; i++)
                        {
                            list[i] = Child(node.data.items.ptr[i]);
                        }
                        return Value.List(list);
                    }
                case RippleValueKind.RIPPLE_VALUE_KIND_ENTRIES:
                    {
                        var n = checked((int)node.data.entries.count);
                        var map = new KeyValuePair<string, Value>[n];
                        for (var i = 0; i < n; i++)
                        {
                            var entry = node.data.entries.ptr[i];
                            map[i] = new KeyValuePair<string, Value>(Internal.Read.Str(entry.key), Child(entry.node));
                        }
                        return Value.Map(map);
                    }
                default:
                    return Value.Null;
            }
        }

        /// <summary>
        /// <paramref name="root"/> laid out in <paramref name="scratch"/>, which
        /// owns everything the view points at.
        /// </summary>
        public static RippleValue* Write(Scratch scratch, Value root)
        {
            var nodes = new List<RippleValueNode>();
            Push(scratch, nodes, root);
            var table = scratch.Array<RippleValueNode>(nodes.Count);
            for (var i = 0; i < nodes.Count; i++)
            {
                table[i] = nodes[i];
            }
            var view = scratch.Array<RippleValue>(1);
            view->nodes = table;
            view->count = (UIntPtr)nodes.Count;
            return view;
        }

        private static uint Push(Scratch scratch, List<RippleValueNode> nodes, Value value)
        {
            var at = (uint)nodes.Count;
            nodes.Add(default);
            var node = new RippleValueNode { kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_NULL };
            switch (value.Kind)
            {
                case ValueKind.Bool:
                    node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_BOOLEAN;
                    node.data.boolean = value.AsBool() ? (byte)1 : (byte)0;
                    break;
                case ValueKind.Int:
                    node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_INTEGER;
                    node.data.integer = value.AsInt();
                    break;
                case ValueKind.Float:
                    node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_REAL;
                    node.data.real = value.AsFloat();
                    break;
                case ValueKind.Text:
                    node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_TEXT;
                    node.data.text = scratch.Str(value.AsText());
                    break;
                case ValueKind.List:
                    {
                        var list = value.AsList();
                        var children = scratch.Array<uint>(list.Count);
                        for (var i = 0; i < list.Count; i++)
                        {
                            children[i] = Push(scratch, nodes, list[i]);
                        }
                        node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_ITEMS;
                        node.data.items = new RippleIndices { ptr = children, count = (UIntPtr)list.Count };
                        break;
                    }
                case ValueKind.Map:
                    {
                        var map = value.AsMap();
                        var children = scratch.Array<RippleValueEntry>(map.Count);
                        for (var i = 0; i < map.Count; i++)
                        {
                            children[i].node = Push(scratch, nodes, map[i].Value);
                            children[i].key = scratch.Str(map[i].Key);
                        }
                        node.kind = (uint)RippleValueKind.RIPPLE_VALUE_KIND_ENTRIES;
                        node.data.entries = new RippleEntries { ptr = children, count = (UIntPtr)map.Count };
                        break;
                    }
            }
            nodes[(int)at] = node;
            return at;
        }

        /// <summary>Named values, as <c>answer</c> takes them.</summary>
        public static RippleFields* Fields(Scratch scratch, IReadOnlyList<KeyValuePair<string, Value>> fields)
        {
            var table = scratch.Array<RippleField>(fields.Count);
            for (var i = 0; i < fields.Count; i++)
            {
                table[i].key = scratch.Str(fields[i].Key);
                table[i].value = *Write(scratch, fields[i].Value ?? Value.Null);
            }
            var view = scratch.Array<RippleFields>(1);
            view->fields = table;
            view->count = (UIntPtr)fields.Count;
            return view;
        }
    }
}
