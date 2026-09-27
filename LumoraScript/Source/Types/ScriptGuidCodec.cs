// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers.Binary;
using Lumora.Wasm;

namespace Lumora.Script;

// A guid is sixteen bytes, carried as two i64 lanes in its own byte order.
public sealed class ScriptGuidCodec : ScriptValueCodec
{
    public ScriptGuidCodec() : base(typeof(Guid), WasmValueType.I64, WasmValueType.I64)
    {
    }

    public override object Unpack(ReadOnlySpan<ulong> lanes)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, lanes[0]);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], lanes[1]);
        return new Guid(bytes);
    }

    public override void Pack(object? value, Span<ulong> lanes)
    {
        Span<byte> bytes = stackalloc byte[16];
        (value is Guid guid ? guid : Guid.Empty).TryWriteBytes(bytes);
        lanes[0] = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        lanes[1] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
    }

    public override bool TryParse(string text, out object? value)
    {
        value = null;
        if (!Guid.TryParse(text?.Trim(), out var guid))
            return false;
        value = guid;
        return true;
    }

    public override string Format(object value) => (value is Guid guid ? guid : Guid.Empty).ToString("D");
}
