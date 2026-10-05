// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets.Interop;

// Maths and values that are not one node of ours: followers with a memory, comparisons within a tolerance,
// angles, colours, text formatted from a constant pattern, and picking by index, each built from our nodes.
// -xlinka
internal static partial class PackageNodeTable
{
    private static void Maths()
    {
        // Rotations compose rather than multiply: the second turn first, then the first.
        On("ValueMul", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            if (type == "floatQ")
            {
                string turn = s.Add("CombineRotations", "floatQ,floatQ");
                s.In("A", turn, "then", new[] { 0d, 0d, 0d, 1d });
                s.In("B", turn, "first", new[] { 0d, 0d, 0d, 1d });
                s.Out("", turn, "rotation");
                return;
            }
            string product = s.Add("Multiply", $"{type},{type}");
            s.In("A", product, "a", Identity(type));
            s.In("B", product, "b", Identity(type));
            s.Out("", product, "result");
        });
        Map("ValueDiv", "Divide", "{T},{T}", ins: "A=a B=b", outs: "=result", values: "A=identity B=identity");
        Map("ValueAbs", "Abs", "{T}", ins: "N=value", outs: "=result");
        Map("ValueSmoothLerp", "SmoothFollow", "{T}", ins: "Input=value Speed=speed", outs: "=result", values: "Speed=0");
        Map("SmoothSlerp_floatQ", "SmoothFollow", "floatQ", ins: "Input=value Speed=speed", outs: "=result", values: "Speed=0");
        Map("ValueConstantLerp", "SteadyFollow", "{T}", ins: "Input=value Speed=speed", outs: "=result", values: "Speed=0");
        Map("ValueDelta", "FrameChange", "{T}", ins: "Value=value", outs: "=delta");
        Map("Approximately_Float", "Approximately", "float,float,float", ins: "A=a B=b Epsilon=epsilon", outs: "=result");
        Map("Approximately_Float3", "Approximately", "float3,float3,float", ins: "A=a B=b Epsilon=epsilon", outs: "=result");
        Map("Approximately_FloatQ", "Approximately", "floatQ,floatQ,float", ins: "A=a B=b Epsilon=epsilon", outs: "=result",
            values: "A=[0,0,0,1] B=[0,0,0,1]");
        Map("Angle_Float3", "AngleBetween", "float3,float3", ins: "A=a B=b", outs: "=degrees");
        Map("InvertColorX", "InvertColor", "colorHDR", ins: "Color=value", outs: "=color");
        Map("ColorXHue", "FromHsvHdr", null, ins: "Hue=hue", outs: "=color", set: "saturation=1 brightness=1");
        Map("ColorXMulAlpha", "MultiplyColorChannel", "colorHDR,colorchannel,float", ins: "Color=value Value=amount", outs: "=color",
            set: "channel=Alpha");
        Map("ToString_DateTime", "FormatDate", null, ins: "V=date Format=format", outs: "=text", values: "Format=G");

        On("ValueSquare", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            string square = s.Add("Multiply", $"{type},{type}");
            s.In("N", square, "a");
            s.In("N", square, "b");
            s.Out("", square, "result");
        });

        // Left to right, one Subtract a step.
        On("ValueSubMulti", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            int count = s.Node.Refs("Inputs").Count;
            if (count < 2)
            {
                s.OutThrough("", "Inputs");
                return;
            }
            string? last = null;
            for (int i = 1; i < count; i++)
            {
                string step = s.Add("Subtract", $"{type},{type}");
                if (last == null)
                    s.InAt("Inputs", 0, step, "a");
                else
                    s.Wire(last, "result", step, "a");
                s.InAt("Inputs", i, step, "b");
                last = step;
            }
            s.Out("", last!, "result");
        });

        // Not the same, by value, for every type.
        Same(s =>
        {
            var type = s.Node.Class == "ObjectNotEquals" ? ObjectType(s.Node.Args.Count > 0 ? s.Node.Args[0] : null) : s.Arg(0);
            if (type == null)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            string same = s.Add("IsSame", type);
            s.In("A", same, "a");
            s.In("B", same, "b");
            string not = s.Add("Not", "bool");
            s.Wire(same, "same", not, "value");
            s.Out("", not, "result");
        }, "ValueNotEquals", "ObjectNotEquals");

        // Output i is the value where the index is i, else the default.
        On("ValueDemultiplex", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            int count = s.Node.Refs("ValueOutputs").Count;
            if (s.Node.Stored("ValueOutputs") is Persistence.DataTreeList outputs)
                count = System.Math.Max(count, outputs.Count);
            for (int i = 0; i < count; i++)
            {
                if (!s.IsRead($"ValueOutputs[{i}]"))
                    continue;
                string at = s.Add("IsEqual", "int,int");
                s.In("Index", at, "a");
                s.Set(at, "b", (double)i);
                string pick = s.Add("Conditional", type);
                s.Wire(at, "result", pick, "condition");
                s.In("Value", pick, "whenTrue");
                s.In("DefaultValue", pick, "whenFalse");
                s.Out($"ValueOutputs[{i}]", pick, "result");
            }
        });

        // The first wired entry equal to the match, -1 when none.
        On("IndexOfFirstValueMatch", s =>
        {
            if (s.Arg(0) is not { } type)
            {
                s.Fail("its type has no counterpart here");
                return;
            }
            var entries = s.Node.Refs("Values");
            string? chain = null;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i] == null)
                    continue;
                string same = s.Add("IsSame", type);
                s.In("Match", same, "a");
                s.InAt("Values", i, same, "b");
                string pick = s.Add("Conditional", "int");
                s.Wire(same, "same", pick, "condition");
                s.Set(pick, "whenTrue", (double)i);
                if (chain == null)
                    s.Set(pick, "whenFalse", -1d);
                else
                    s.Wire(chain, "result", pick, "whenFalse");
                chain = pick;
            }
            if (chain == null)
            {
                s.OutConstant("Index", -1d);
                s.OutConstant("FoundMatch", false);
                return;
            }
            s.Out("Index", chain, "result");
            string found = s.Add("GreaterOrEqual", "int,int");
            s.Wire(chain, "result", found, "a");
            s.Set(found, "b", 0d);
            s.Out("FoundMatch", found, "result");
        });

        // A constant pattern becomes the text it builds: its pieces and each value in turn, numbers to the
        // decimals a pattern like {0:F3} or {0:.0} asks for.
        On("FormatString", s =>
        {
            var format = s.Feeder("Format") is { Class: "ValueObjectInput" } constant ? constant.Value("Value") as string : null;
            if (format == null)
            {
                s.Fail("formats with a pattern worked out as it runs, which is not carried");
                return;
            }
            var pieces = new List<Action<string, string>>();
            int at = 0;
            foreach (Match match in Regex.Matches(format, @"\{(\d+)(?::([^}]*))?\}"))
            {
                if (match.Index > at)
                {
                    string literal = format[at..match.Index].Replace("{{", "{").Replace("}}", "}");
                    pieces.Add((node, port) => s.Set(node, port, literal));
                }
                at = match.Index + match.Length;
                int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                string spec = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
                var feeder = FeederAt(s, "Parameters", index);
                string? type = feeder == null ? null : ValueTypeOf(feeder);
                pieces.Add((node, port) =>
                {
                    if (type == "string" || type == null)
                    {
                        s.InAt("Parameters", index, node, port);
                        return;
                    }
                    int? decimals = Decimals(spec);
                    if (decimals != null && type is "float" or "double")
                    {
                        string number = s.Add("FormatNumber", $"{type},int");
                        s.InAt("Parameters", index, number, "value");
                        s.Set(number, "decimals", (double)decimals.Value);
                        s.Wire(number, "result", node, port);
                        return;
                    }
                    string text = s.Add("ToText", type);
                    s.InAt("Parameters", index, text, "value");
                    s.Wire(text, "result", node, port);
                });
            }
            if (at < format.Length)
            {
                string tail = format[at..].Replace("{{", "{").Replace("}}", "}");
                pieces.Add((node, port) => s.Set(node, port, tail));
            }
            if (pieces.Count == 0)
            {
                s.OutConstant("", string.Empty);
                return;
            }
            if (pieces.Count == 1)
                pieces.Add((node, port) => s.Set(node, port, string.Empty));
            var parts = new string[pieces.Count];
            Array.Fill(parts, "string");
            string joined = s.Add("Concat", string.Join(",", parts));
            for (int i = 0; i < pieces.Count; i++)
                pieces[i](joined, Lettered(i));
            s.Out("", joined, "result");
        });
    }

    // The decimals a numeric format asks for: Fk or Nk, bare F or N for two, or a custom 0.00 or .0.
    private static int? Decimals(string spec)
    {
        if (spec.Length == 0)
            return null;
        if (spec[0] is 'F' or 'f' or 'N' or 'n')
            return spec.Length > 1 && int.TryParse(spec[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : 2;
        int dot = spec.IndexOf('.');
        return dot >= 0 ? spec.Length - dot - 1 : 0;
    }

    // The package node feeding entry i of a list input, through relays and casts.
    private static PackageNode? FeederAt(PackageNodeScope s, string list, int index)
    {
        var ids = s.Node.Refs(list);
        string? id = index < ids.Count ? ids[index] : null;
        for (int depth = 0; depth < 32 && s.Index.TryNode(id, out var node, out _); depth++)
        {
            if (node.Class is not ("ValueRelay" or "ObjectRelay" or "ValueToObjectCast" or "ObjectCast" or "ContinuouslyChangingValueRelay"))
                return node;
            id = node.Ref("Input");
        }
        return null;
    }

    // What type of value a package node gives, from its own generic argument, where it says.
    private static string? ValueTypeOf(PackageNode node)
    {
        if (node.Class is "ValueObjectInput" or "ObjectConditional" || node.Class.StartsWith("ToString", StringComparison.Ordinal)
            || node.Class.StartsWith("FormatString", StringComparison.Ordinal) || node.Class.StartsWith("Concatenate", StringComparison.Ordinal))
            return "string";
        return node.Arg(0) ?? (node.Class.EndsWith("_Float", StringComparison.Ordinal) ? "float" : null);
    }
}
