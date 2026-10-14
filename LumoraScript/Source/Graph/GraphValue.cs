// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A constant on a node, as the document holds it. What it means depends on the port it lands on; the
// compiler converts and range-checks it there.
public sealed class GraphValue
{
    public GraphValueKind Kind { get; }
    public bool Bool { get; }
    public double Number { get; }
    public string? String { get; }
    public int Binding { get; }
    public double[] Numbers { get; }

    private GraphValue(GraphValueKind kind, bool boolean = false, double number = 0, string? text = null, int binding = 0,
        double[]? numbers = null)
    {
        Kind = kind;
        Bool = boolean;
        Number = number;
        String = text;
        Binding = binding;
        Numbers = numbers ?? Array.Empty<double>();
    }

    public static readonly GraphValue Null = new(GraphValueKind.Null);

    public static GraphValue Of(bool value) => new(GraphValueKind.Bool, boolean: value);

    public static GraphValue Of(double value)
        => double.IsFinite(value) ? new(GraphValueKind.Number, number: value) : throw new ArgumentException("not a finite number");

    public static GraphValue Of(string value) => new(GraphValueKind.String, text: value ?? throw new ArgumentNullException(nameof(value)));

    public static GraphValue OfBinding(int slot)
        => slot >= 0 ? new(GraphValueKind.Binding, binding: slot) : throw new ArgumentOutOfRangeException(nameof(slot));

    public static GraphValue Of(double[] values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("not a finite number");
        }
        return new(GraphValueKind.Numbers, numbers: (double[])values.Clone());
    }

    // One value on its own, spelled the way a graph spells it, for editors that keep constants apart
    // from a whole document.
    public string ToJson() => GraphWriter.WriteValueText(this);

    public static GraphValue ParseJson(string json, GraphLimits? limits = null)
        => GraphReader.ReadValue(json, limits ?? GraphLimits.Default);

    // Use the compiler's literal rules when an editor considers changing a port's type.
    public bool TryValidate(ScriptType type, out string error)
        => ConstantValue.TryConvert(this, type, out _, out error);

    public override string ToString() => Kind switch
    {
        GraphValueKind.Bool => Bool ? "true" : "false",
        GraphValueKind.Number => Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GraphValueKind.String => $"\"{String}\"",
        GraphValueKind.Binding => $"binding {Binding}",
        GraphValueKind.Numbers => $"[{string.Join(", ", Numbers)}]",
        _ => "null",
    };
}
