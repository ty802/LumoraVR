// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Linq.Expressions;
using System.Reflection;
using Lumora.Wasm;

namespace Lumora.Script;

// One number inside a struct type and the lane it rides in. Path is the chain of fields that reaches it,
// since a matrix keeps its numbers in columns and a box in its two corners.
internal sealed class ScriptLeaf
{
    public FieldInfo[] Path { get; }
    public Type ClrType { get; }
    public WasmValueType Lane { get; }

    public ScriptLeaf(FieldInfo[] path)
    {
        Path = path;
        ClrType = path[^1].FieldType;
        Lane = LaneOf(ClrType) ?? throw new InvalidOperationException($"{ClrType} is not a number a lane can hold");
    }

    public static WasmValueType? LaneOf(Type type)
    {
        if (type == typeof(float)) return WasmValueType.F32;
        if (type == typeof(int) || type == typeof(bool)) return WasmValueType.I32;
        if (type == typeof(double)) return WasmValueType.F64;
        if (type == typeof(long)) return WasmValueType.I64;
        return null;
    }

    public ulong ToRaw(object value)
    {
        if (ClrType == typeof(float)) return LaneCodec.FromFloat((float)value);
        if (ClrType == typeof(int)) return LaneCodec.FromInt((int)value);
        if (ClrType == typeof(bool)) return LaneCodec.FromBool((bool)value);
        if (ClrType == typeof(double)) return LaneCodec.FromDouble((double)value);
        return LaneCodec.FromLong((long)value);
    }

    public object FromRaw(ulong raw)
    {
        if (ClrType == typeof(float)) return LaneCodec.ToFloat(raw);
        if (ClrType == typeof(int)) return LaneCodec.ToInt(raw);
        if (ClrType == typeof(bool)) return LaneCodec.ToBool(raw);
        if (ClrType == typeof(double)) return LaneCodec.ToDouble(raw);
        return LaneCodec.ToLong(raw);
    }

    public MethodInfo ToRawMethod => typeof(LaneCodec).GetMethod(
        ClrType == typeof(float) ? nameof(LaneCodec.FromFloat)
        : ClrType == typeof(int) ? nameof(LaneCodec.FromInt)
        : ClrType == typeof(bool) ? nameof(LaneCodec.FromBool)
        : ClrType == typeof(double) ? nameof(LaneCodec.FromDouble)
        : nameof(LaneCodec.FromLong))!;

    public MethodInfo FromRawMethod => typeof(LaneCodec).GetMethod(
        ClrType == typeof(float) ? nameof(LaneCodec.ToFloat)
        : ClrType == typeof(int) ? nameof(LaneCodec.ToInt)
        : ClrType == typeof(bool) ? nameof(LaneCodec.ToBool)
        : ClrType == typeof(double) ? nameof(LaneCodec.ToDouble)
        : nameof(LaneCodec.ToLong))!;

    // The leaf's value inside a boxed struct.
    public object Get(object root)
    {
        object current = root;
        foreach (var field in Path)
            current = field.GetValue(current)!;
        return current;
    }

    // Sets the leaf inside a boxed struct. A nested struct is a copy, so each level is written back.
    public void Set(object root, object value) => SetAt(root, 0, value);

    private void SetAt(object holder, int depth, object value)
    {
        var field = Path[depth];
        if (depth == Path.Length - 1)
        {
            field.SetValue(holder, value);
            return;
        }
        var inner = field.GetValue(holder)!;
        SetAt(inner, depth + 1, value);
        field.SetValue(holder, inner);
    }

    public Expression Read(Expression root)
    {
        Expression current = root;
        foreach (var field in Path)
            current = Expression.Field(current, field);
        return current;
    }
}
