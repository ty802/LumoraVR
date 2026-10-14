// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Lumora.Wasm;

namespace Lumora.Script;

// The built-in coded types. Narrow integers ride an i32 lane zero- or sign-extended, so a lane never
// holds a byte bigger than a byte; a date keeps its ticks and its kind in one i64, the way a date keeps
// them itself; a span of time is its ticks. -xlinka
public static class ScriptCodecs
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const ulong DateTicksMask = 0x3FFFFFFFFFFFFFFFul;

    public static readonly ScriptScalarCodec<byte> Byte = Integer<byte>(WasmValueType.I32, raw => (byte)raw, v => v,
        (string s, out byte v) => byte.TryParse(Digits(s, out var style), style, Invariant, out v));

    public static readonly ScriptScalarCodec<sbyte> SByte = Integer<sbyte>(WasmValueType.I32, raw => (sbyte)(uint)raw, v => (uint)(int)v,
        (string s, out sbyte v) => sbyte.TryParse(Digits(s, out var style), style, Invariant, out v));

    public static readonly ScriptScalarCodec<short> Short = Integer<short>(WasmValueType.I32, raw => (short)(uint)raw, v => (uint)(int)v,
        (string s, out short v) => short.TryParse(Digits(s, out var style), style, Invariant, out v));

    public static readonly ScriptScalarCodec<ushort> UShort = Integer<ushort>(WasmValueType.I32, raw => (ushort)raw, v => v,
        (string s, out ushort v) => ushort.TryParse(Digits(s, out var style), style, Invariant, out v));

    public static readonly ScriptScalarCodec<uint> UInt = Integer<uint>(WasmValueType.I32, raw => (uint)raw, v => v,
        (string s, out uint v) => uint.TryParse(Digits(s, out var style), style, Invariant, out v));

    public static readonly ScriptScalarCodec<ulong> ULong = new(WasmValueType.I64, raw => raw, v => v,
        (string s, out ulong v) => ulong.TryParse(Digits(s, out var style), style, Invariant, out v),
        v => v.ToString(Invariant),
        v => v <= (1ul << 53) ? v : null);

    public static readonly ScriptScalarCodec<Half> HalfFloat = new(WasmValueType.I32,
        raw => BitConverter.UInt16BitsToHalf((ushort)raw),
        v => BitConverter.HalfToUInt16Bits(v),
        (string s, out Half v) => Half.TryParse(s, NumberStyles.Float, Invariant, out v),
        v => ((float)v).ToString("R", Invariant),
        v => Half.IsFinite(v) ? (double)(float)v : null);

    public static readonly ScriptScalarCodec<char> Char = new(WasmValueType.I32, raw => (char)(ushort)raw, v => v,
        TryParseChar, FormatChar, null);

    public static readonly ScriptScalarCodec<DateTime> Date = new(WasmValueType.I64,
        raw => new DateTime((long)System.Math.Min(raw & DateTicksMask, (ulong)DateTime.MaxValue.Ticks),
            (raw >> 62) is var kind && kind <= 2 ? (DateTimeKind)kind : DateTimeKind.Unspecified),
        v => (ulong)v.Ticks | ((ulong)v.Kind << 62),
        TryParseDate,
        FormatDate);

    public static readonly ScriptScalarCodec<TimeSpan> Duration = new(WasmValueType.I64,
        raw => new TimeSpan(unchecked((long)raw)),
        v => unchecked((ulong)v.Ticks),
        TryParseDuration,
        v => v.ToString("c", Invariant));

    public static readonly ScriptGuidCodec Guid = new();

    // One of an enum's names. It rides an i32 lane, or an i64 for an enum as wide as a long.
    //
    // Most enums carry their number straight across without boxing it, which every node call taking or making
    // a hand or a body part used to do twice. An enum over uint or ulong keeps the checked conversion, which
    // refuses a number past the signed range as it always has.
    public static ScriptScalarCodec<TEnum> Enum<TEnum>() where TEnum : struct, Enum
    {
        var underlying = System.Enum.GetUnderlyingType(typeof(TEnum));
        bool wide = underlying == typeof(long) || underlying == typeof(ulong);
        bool direct = underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short)
            || underlying == typeof(sbyte) || underlying == typeof(byte) || underlying == typeof(ushort);
        bool signed = underlying == typeof(sbyte) || underlying == typeof(short);
        Func<ulong, TEnum> fromLane = direct
            ? raw => EnumFromBits<TEnum>(wide ? unchecked((long)raw) : unchecked((int)(uint)raw))
            : raw => (TEnum)System.Enum.ToObject(typeof(TEnum), wide ? unchecked((long)raw) : unchecked((int)(uint)raw));
        Func<TEnum, ulong> toLane = direct
            ? value => wide ? unchecked((ulong)EnumBits(value, signed)) : unchecked((uint)(int)EnumBits(value, signed))
            : value => wide ? unchecked((ulong)Convert.ToInt64(value, Invariant)) : unchecked((uint)Convert.ToInt32(value, Invariant));
        return new ScriptScalarCodec<TEnum>(wide ? WasmValueType.I64 : WasmValueType.I32, fromLane, toLane,
            (string s, out TEnum v) => System.Enum.TryParse(s, ignoreCase: true, out v),
            value => value.ToString());
    }

    // The enum's own number, widened the way its underlying type widens.
    private static long EnumBits<TEnum>(TEnum value, bool signed) where TEnum : struct, Enum
    {
        switch (System.Runtime.CompilerServices.Unsafe.SizeOf<TEnum>())
        {
            case 1:
                return signed ? System.Runtime.CompilerServices.Unsafe.As<TEnum, sbyte>(ref value) : System.Runtime.CompilerServices.Unsafe.As<TEnum, byte>(ref value);
            case 2:
                return signed ? System.Runtime.CompilerServices.Unsafe.As<TEnum, short>(ref value) : System.Runtime.CompilerServices.Unsafe.As<TEnum, ushort>(ref value);
            case 4:
                return System.Runtime.CompilerServices.Unsafe.As<TEnum, int>(ref value);
            default:
                return System.Runtime.CompilerServices.Unsafe.As<TEnum, long>(ref value);
        }
    }

    // The low bits of a number as the enum, as many as the enum is wide.
    private static TEnum EnumFromBits<TEnum>(long bits) where TEnum : struct, Enum
    {
        switch (System.Runtime.CompilerServices.Unsafe.SizeOf<TEnum>())
        {
            case 1:
            {
                byte narrow = unchecked((byte)bits);
                return System.Runtime.CompilerServices.Unsafe.As<byte, TEnum>(ref narrow);
            }
            case 2:
            {
                short narrow = unchecked((short)bits);
                return System.Runtime.CompilerServices.Unsafe.As<short, TEnum>(ref narrow);
            }
            case 4:
            {
                int narrow = unchecked((int)bits);
                return System.Runtime.CompilerServices.Unsafe.As<int, TEnum>(ref narrow);
            }
            default:
                return System.Runtime.CompilerServices.Unsafe.As<long, TEnum>(ref bits);
        }
    }

    private static ScriptScalarCodec<T> Integer<T>(WasmValueType lane, Func<ulong, T> fromLane, Func<T, ulong> toLane,
        ScriptScalarCodec<T>.Parser parse) where T : struct, IConvertible
        => new(lane, fromLane, toLane, parse, v => v.ToString(Invariant), v => v.ToDouble(Invariant));

    // "0x1F" reads as hex; anything else as a plain integer.
    private static string Digits(string text, out NumberStyles style)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            style = NumberStyles.AllowHexSpecifier;
            return text[2..];
        }
        style = NumberStyles.Integer;
        return text;
    }

    // A character as itself, or by name where it would not show: "space", "tab", "\n", "U+00E9".
    private static bool TryParseChar(string text, out char value)
    {
        value = default;
        switch (text)
        {
            case "space": value = ' '; return true;
            case "tab" or "\\t": value = '\t'; return true;
            case "newline" or "\\n": value = '\n'; return true;
            case "return" or "\\r": value = '\r'; return true;
            case "null" or "\\0": value = '\0'; return true;
        }
        if (text.Length == 1)
        {
            value = text[0];
            return true;
        }
        if (text.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(text[2..], NumberStyles.AllowHexSpecifier, Invariant, out ushort code))
        {
            value = (char)code;
            return true;
        }
        return false;
    }

    private static string FormatChar(char value) => value switch
    {
        ' ' => "space",
        '\t' => "\\t",
        '\n' => "\\n",
        '\r' => "\\r",
        '\0' => "\\0",
        _ when char.IsControl(value) || char.IsWhiteSpace(value) || char.IsSurrogate(value) => $"U+{(int)value:X4}",
        _ => value.ToString(),
    };

    // A date's text never goes through the machine's time zone: an offset written in the text is applied as
    // written and gives a UTC date, and a local date is written as it reads, without an offset. Otherwise
    // the host's zone would leak into constants and synced values.
    private static bool TryParseDate(string text, out DateTime value)
    {
        if (!DateTime.TryParse(text, Invariant, DateTimeStyles.RoundtripKind, out value))
            return false;
        if (value.Kind == DateTimeKind.Local && DateTimeOffset.TryParse(text, Invariant, DateTimeStyles.AssumeUniversal, out var stated))
            value = stated.UtcDateTime;
        return true;
    }

    private static string FormatDate(DateTime value) => value.Kind == DateTimeKind.Local
        ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("O", Invariant)
        : value.ToString("O", Invariant);

    // "1:30" is ninety minutes and "90s" ninety seconds, next to the usual "d.hh:mm:ss.fffffff".
    private static bool TryParseDuration(string text, out TimeSpan value)
    {
        if (TimeSpan.TryParse(text, Invariant, out value))
            return true;
        if (text.Length > 1 && char.IsLetter(text[^1]) && double.TryParse(text[..^1], NumberStyles.Float, Invariant, out double amount)
            && double.IsFinite(amount))
        {
            double seconds = char.ToLowerInvariant(text[^1]) switch
            {
                's' => amount,
                'm' => amount * 60,
                'h' => amount * 3600,
                'd' => amount * 86400,
                _ => double.NaN,
            };
            if (double.IsFinite(seconds) && System.Math.Abs(seconds) < TimeSpan.MaxValue.TotalSeconds)
            {
                value = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        value = default;
        return false;
    }
}
