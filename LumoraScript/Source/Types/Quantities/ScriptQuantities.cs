// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Lumora.Wasm;

namespace Lumora.Script;

// Reading and writing quantities as text. "1.5 m", "30cm" and "5 ft 3 in" all read; a bare number is in
// the base unit. A unit is matched exactly first and without case after, so "Mm" and "mm" stay apart
// while "KM" still reads. -xlinka
public static class ScriptQuantities
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IEnumerable<(string Name, ScriptValueCodec Codec)> Codecs
    {
        get
        {
            yield return Codec<DistanceKind>();
            yield return Codec<AngleKind>();
            yield return Codec<MassKind>();
            yield return Codec<SpeedKind>();
            yield return Codec<AccelerationKind>();
            yield return Codec<TemperatureKind>();
            yield return Codec<AreaKind>();
            yield return Codec<VolumeKind>();
            yield return Codec<ForceKind>();
            yield return Codec<EnergyKind>();
            yield return Codec<PowerKind>();
            yield return Codec<FrequencyKind>();
            yield return Codec<PressureKind>();
            yield return Codec<DataSizeKind>();
        }
    }

    public static ScriptUnit? FindUnit(ScriptUnit[] units, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return units[0];
        name = name.Trim();
        foreach (var unit in units)
        {
            if (unit.Names(name, StringComparison.Ordinal))
                return unit;
        }
        foreach (var unit in units)
        {
            if (unit.Names(name, StringComparison.OrdinalIgnoreCase))
                return unit;
        }
        return null;
    }

    // Terms add up, each a number and an optional unit: "5 ft 3 in", "-12.5", "1e3 m", "10 nautical miles".
    // A unit runs until whitespace that is followed by the next number.
    public static bool TryParse(string? text, ScriptUnit[] units, out double baseValue)
        => TryParse(text, units, units[0], out baseValue);

    // The same, with a number that has no unit taken in bare rather than the base unit.
    public static bool TryParse(string? text, ScriptUnit[] units, ScriptUnit bare, out double baseValue)
    {
        baseValue = 0d;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        int i = 0;
        int terms = 0;
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                break;

            int start = i;
            if (text[i] is '+' or '-')
                i++;
            int digits = i;
            while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                i++;
            if (i == digits)
                return false;
            if (i < text.Length && text[i] is 'e' or 'E')
            {
                int mark = i++;
                if (i < text.Length && text[i] is '+' or '-')
                    i++;
                int exponent = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                if (i == exponent)
                    i = mark;
            }
            if (!double.TryParse(text[start..i], NumberStyles.Float, Invariant, out double amount) || !double.IsFinite(amount))
                return false;

            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            int unitStart = i;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    int next = i;
                    while (next < text.Length && char.IsWhiteSpace(text[next]))
                        next++;
                    if (next >= text.Length || char.IsAsciiDigit(text[next]) || text[next] is '+' or '-' or '.')
                        break;
                    i = next;
                    continue;
                }
                i++;
            }
            var unit = unitStart == i ? bare : FindUnit(units, text[unitStart..i]);
            if (unit == null)
                return false;
            baseValue += unit.ToBase(amount);
            terms++;
        }
        return terms > 0 && double.IsFinite(baseValue);
    }

    // decimals < 0 writes every digit needed to read the value back exactly.
    public static string Format(double baseValue, ScriptUnit[] units, ScriptUnit unit, int decimals)
        => Format(baseValue, unit, decimals, null, false);

    // A number format, such as "0.00" or "N1", wins over decimals when there is one. Long names write the unit
    // out, "1 metre" and "2.5 metres", where the unit has a name.
    public static string Format(double baseValue, ScriptUnit unit, int decimals, string? format, bool longName)
    {
        double value = unit.FromBase(baseValue);
        string number = string.IsNullOrWhiteSpace(format)
            ? decimals < 0 ? value.ToString("R", Invariant) : value.ToString("F" + Math.Clamp(decimals, 0, 15), Invariant)
            : Number(value, format.Trim());
        string words = !longName ? unit.Symbol : (number is "1" or "-1" ? unit.Name : unit.Plural) ?? unit.Symbol;
        return $"{number} {words}";
    }

    // The unit that writes the value most plainly: of the steps marked for it, the largest that leaves a
    // number of at least 1, or the smallest when none does. The base unit for 0, for a value that is not
    // finite, and for a kind with no steps.
    public static ScriptUnit AutoUnit(ScriptUnit[] units, double baseValue)
    {
        if (baseValue == 0d || !double.IsFinite(baseValue))
            return units[0];
        ScriptUnit? best = null;
        ScriptUnit? smallest = null;
        foreach (var unit in units)
        {
            if (!unit.Auto)
                continue;
            if (smallest == null || unit.Factor < smallest.Factor)
                smallest = unit;
            if (Math.Abs(unit.FromBase(baseValue)) >= 1d && (best == null || unit.Factor > best.Factor))
                best = unit;
        }
        return best ?? smallest ?? units[0];
    }

    // A standard format's precision past 17 digits, more than a double holds, is held at 17.
    private static string Number(double value, string format)
    {
        bool standard = format.Length > 1 && char.IsAsciiLetter(format[0]);
        if (standard && int.TryParse(format.AsSpan(1), NumberStyles.None, Invariant, out int digits))
            format = format[0] + Math.Min(digits, 17).ToString(Invariant);
        try
        {
            return value.ToString(format, Invariant);
        }
        catch (FormatException)
        {
            var shown = format.Length > 32 ? format[..32] + "..." : format;
            throw new InvalidOperationException($"'{shown}' is not a number format");
        }
    }

    private static (string, ScriptValueCodec) Codec<TKind>() where TKind : IScriptQuantityKind
    {
        var units = TKind.Units;
        var codec = new ScriptScalarCodec<ScriptQuantity<TKind>>(WasmValueType.F64,
            raw => new ScriptQuantity<TKind>(LaneCodec.ToDouble(raw)),
            value => LaneCodec.FromDouble(value.BaseValue),
            (string text, out ScriptQuantity<TKind> value) =>
            {
                bool ok = TryParse(text, units, out double parsed);
                value = new ScriptQuantity<TKind>(parsed);
                return ok;
            },
            value => Format(value.BaseValue, units, units[0], -1),
            value => value.BaseValue);
        return (TKind.Name, codec);
    }
}
