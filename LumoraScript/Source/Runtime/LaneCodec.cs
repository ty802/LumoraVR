// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Script values to and from WebAssembly lanes, the raw ulong form the engine keeps every value in.
// Floats leave C# canonicalised, the same way the engine's own arithmetic writes them, so a NaN from a
// host call cannot carry a payload that differs between peers. -xlinka
public static class LaneCodec
{
    private const uint CanonicalNaN32 = 0x7FC00000u;
    private const ulong CanonicalNaN64 = 0x7FF8000000000000ul;

    public static ulong FromBool(bool value) => value ? 1ul : 0ul;
    public static ulong FromInt(int value) => (uint)value;
    public static ulong FromLong(long value) => unchecked((ulong)value);
    public static ulong FromFloat(float value) => float.IsNaN(value) ? CanonicalNaN32 : BitConverter.SingleToUInt32Bits(value);
    public static ulong FromDouble(double value) => double.IsNaN(value) ? CanonicalNaN64 : BitConverter.DoubleToUInt64Bits(value);

    public static bool ToBool(ulong raw) => (uint)raw != 0;
    public static int ToInt(ulong raw) => unchecked((int)(uint)raw);
    public static long ToLong(ulong raw) => unchecked((long)raw);
    public static float ToFloat(ulong raw) => BitConverter.UInt32BitsToSingle((uint)raw);
    public static double ToDouble(ulong raw) => BitConverter.UInt64BitsToDouble(raw);

    // Boxed forms, for constants and the reflection fallback. The compiled adapters never come here.
    public static object? Read(ScriptType type, ReadOnlySpan<ulong> lanes, ScriptContextBase? context)
    {
        switch (type.Kind)
        {
            case ScriptTypeKind.Bool: return ToBool(lanes[0]);
            case ScriptTypeKind.Int: return ToInt(lanes[0]);
            case ScriptTypeKind.Long: return ToLong(lanes[0]);
            case ScriptTypeKind.Float: return ToFloat(lanes[0]);
            case ScriptTypeKind.Double: return ToDouble(lanes[0]);
            case ScriptTypeKind.Vector:
            {
                var boxed = Activator.CreateInstance(type.ClrType)!;
                for (int i = 0; i < type.Leaves.Length; i++)
                    type.Leaves[i].Set(boxed, type.Leaves[i].FromRaw(lanes[i]));
                return boxed;
            }
            case ScriptTypeKind.Coded:
                return type.Codec!.Unpack(lanes);
            default:
            {
                var value = context?.GetLive(ToInt(lanes[0]));
                return value != null && type.ClrType.IsInstanceOfType(value) ? value : null;
            }
        }
    }

    public static void Write(ScriptType type, object? value, Span<ulong> lanes, ScriptContextBase? context)
    {
        switch (type.Kind)
        {
            case ScriptTypeKind.Bool: lanes[0] = FromBool(value is true); break;
            case ScriptTypeKind.Int: lanes[0] = FromInt(value is int integer ? integer : 0); break;
            case ScriptTypeKind.Long: lanes[0] = FromLong(value is long wide ? wide : 0); break;
            case ScriptTypeKind.Float: lanes[0] = FromFloat(value is float single ? single : 0f); break;
            case ScriptTypeKind.Double: lanes[0] = FromDouble(value is double precise ? precise : 0d); break;
            case ScriptTypeKind.Vector:
            {
                var boxed = value ?? Activator.CreateInstance(type.ClrType)!;
                for (int i = 0; i < type.Leaves.Length; i++)
                    lanes[i] = type.Leaves[i].ToRaw(type.Leaves[i].Get(boxed));
                break;
            }
            case ScriptTypeKind.Coded:
                type.Codec!.Pack(value, lanes);
                break;
            default:
                lanes[0] = FromInt(value == null || context == null ? 0 : context.AddHandle(value));
                break;
        }
    }
}
