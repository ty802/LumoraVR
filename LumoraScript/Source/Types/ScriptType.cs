// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Wasm;

namespace Lumora.Script;

// One type in the closed vocabulary a graph may use. Nothing outside ScriptTypes creates these, so a
// port type is always one the compiler and the adapters know how to carry. -xlinka
public sealed class ScriptType
{
    public string Name { get; }
    public Type ClrType { get; }
    public ScriptTypeKind Kind { get; }
    public WasmValueType[] Lanes { get; }

    // A coded type's codec; null for every other kind.
    public ScriptValueCodec? Codec { get; }

    // A generic family's name and arguments: "list" and [int] for list<int>. Empty for a plain type.
    public string? Family { get; }
    public IReadOnlyList<ScriptType> Arguments { get; }

    internal ScriptLeaf[] Leaves { get; }

    internal ScriptType(string name, Type clrType, ScriptTypeKind kind, ScriptLeaf[]? leaves = null, ScriptValueCodec? codec = null,
        string? family = null, IReadOnlyList<ScriptType>? arguments = null)
    {
        Name = name;
        ClrType = clrType;
        Kind = kind;
        Leaves = leaves ?? Array.Empty<ScriptLeaf>();
        Codec = codec;
        Family = family;
        Arguments = arguments ?? Array.Empty<ScriptType>();
        Lanes = kind switch
        {
            ScriptTypeKind.Long => new[] { WasmValueType.I64 },
            ScriptTypeKind.Float => new[] { WasmValueType.F32 },
            ScriptTypeKind.Double => new[] { WasmValueType.F64 },
            ScriptTypeKind.Vector => Leaves.Select(l => l.Lane).ToArray(),
            ScriptTypeKind.Coded => codec!.Lanes,
            _ => new[] { WasmValueType.I32 },
        };
    }

    public int LaneCount => Lanes.Length;

    public bool IsHandle => Kind == ScriptTypeKind.Handle;

    // A struct whose every lane is a float: what the float-only paths (a search's query, a color) take.
    public bool IsFloatVector => Kind == ScriptTypeKind.Vector && Lanes.All(l => l == WasmValueType.F32);

    // What an unwired, unset input of this type reads as.
    public object? DefaultValue => Kind == ScriptTypeKind.Handle ? null : Activator.CreateInstance(ClrType);

    public override string ToString() => Name;
}
