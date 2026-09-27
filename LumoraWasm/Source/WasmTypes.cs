// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Text;

namespace Lumora.Wasm;

public enum WasmValueType : byte
{
    I32 = 0x7F,
    I64 = 0x7E,
    F32 = 0x7D,
    F64 = 0x7C,
    V128 = 0x7B,
    FuncRef = 0x70,
    ExternRef = 0x6F,
}

public static class WasmValueTypes
{
    public static bool IsReference(WasmValueType type) => type is WasmValueType.FuncRef or WasmValueType.ExternRef;

    public static bool IsNumeric(WasmValueType type)
        => type is WasmValueType.I32 or WasmValueType.I64 or WasmValueType.F32 or WasmValueType.F64;

    public static string Name(WasmValueType type) => type switch
    {
        WasmValueType.I32 => "i32",
        WasmValueType.I64 => "i64",
        WasmValueType.F32 => "f32",
        WasmValueType.F64 => "f64",
        WasmValueType.V128 => "v128",
        WasmValueType.FuncRef => "funcref",
        WasmValueType.ExternRef => "externref",
        _ => "?",
    };
}

public sealed class WasmFuncType : IEquatable<WasmFuncType>
{
    public static readonly WasmFuncType Empty = new(Array.Empty<WasmValueType>(), Array.Empty<WasmValueType>());

    public WasmValueType[] Parameters { get; }
    public WasmValueType[] Results { get; }

    public WasmFuncType(WasmValueType[] parameters, WasmValueType[] results)
    {
        Parameters = parameters;
        Results = results;
    }

    public bool Equals(WasmFuncType? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return Parameters.AsSpan().SequenceEqual(other.Parameters) && Results.AsSpan().SequenceEqual(other.Results);
    }

    public override bool Equals(object? obj) => Equals(obj as WasmFuncType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var p in Parameters)
            hash.Add(p);
        hash.Add(-1);
        foreach (var r in Results)
            hash.Add(r);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var sb = new StringBuilder("(");
        for (int i = 0; i < Parameters.Length; i++)
            sb.Append(i == 0 ? "" : " ").Append(WasmValueTypes.Name(Parameters[i]));
        sb.Append(") -> (");
        for (int i = 0; i < Results.Length; i++)
            sb.Append(i == 0 ? "" : " ").Append(WasmValueTypes.Name(Results[i]));
        return sb.Append(')').ToString();
    }
}

public readonly struct WasmSizeRange
{
    public readonly uint Min;
    public readonly uint? Max;

    public WasmSizeRange(uint min, uint? max)
    {
        Min = min;
        Max = max;
    }

    // Import matching: something whose current range is this satisfies a declaration of `declared`.
    public bool Satisfies(in WasmSizeRange declared)
    {
        if (Min < declared.Min)
            return false;
        if (declared.Max is uint declaredMax)
            return Max is uint max && max <= declaredMax;
        return true;
    }
}

public readonly record struct WasmTableType(WasmValueType ElementType, WasmSizeRange Size);

public readonly record struct WasmMemoryType(WasmSizeRange Pages);

public readonly record struct WasmGlobalType(WasmValueType ValueType, bool Mutable);

public enum WasmExternKind : byte
{
    Function = 0,
    Table = 1,
    Memory = 2,
    Global = 3,
}
