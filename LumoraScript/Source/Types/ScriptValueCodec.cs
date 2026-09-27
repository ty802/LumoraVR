// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Lumora.Wasm;

namespace Lumora.Script;

// How a coded type travels and how it is written down: its lanes, its text form (typed on a card, kept
// in the synced store) and its document form. Text is always invariant, so a value reads back the same on
// every machine. -xlinka
public abstract class ScriptValueCodec
{
    private static readonly MethodInfo UnpackAtMethod = typeof(ScriptValueCodec).GetMethod(nameof(UnpackAt))!;
    private static readonly MethodInfo PackAtMethod = typeof(ScriptValueCodec).GetMethod(nameof(PackAt))!;

    protected ScriptValueCodec(Type clrType, params WasmValueType[] lanes)
    {
        if (!clrType.IsValueType)
            throw new ArgumentException($"{clrType} is not a value type", nameof(clrType));
        if (lanes.Length == 0)
            throw new ArgumentException("a coded value needs at least one lane", nameof(lanes));
        ClrType = clrType;
        Lanes = lanes;
    }

    public Type ClrType { get; }
    public WasmValueType[] Lanes { get; }

    // Null is the type's default.
    public abstract object Unpack(ReadOnlySpan<ulong> lanes);
    public abstract void Pack(object? value, Span<ulong> lanes);

    public abstract bool TryParse(string text, out object? value);
    public abstract string Format(object value);

    // A document constant: a string in the text form, or a plain number where the type takes one.
    public virtual bool TryFromGraph(GraphValue value, out object? result)
    {
        result = null;
        return value.Kind switch
        {
            GraphValueKind.String => TryParse(value.String!, out result),
            GraphValueKind.Number => TryParse(value.Number.ToString("R", CultureInfo.InvariantCulture), out result),
            _ => false,
        };
    }

    public virtual GraphValue ToGraph(object value) => GraphValue.Of(Format(value));

    public object UnpackAt(ulong[] lanes, int offset) => Unpack(lanes.AsSpan(offset, Lanes.Length));

    public void PackAt(object? value, ulong[] lanes, int offset) => Pack(value, lanes.AsSpan(offset, Lanes.Length));

    // The compiled adapters' forms. The boxed path here is correct for any codec; a codec with a faster
    // typed path overrides these.
    internal virtual Expression Decode(ParameterExpression lanes, int offset)
        => Expression.Convert(Expression.Call(Expression.Constant(this), UnpackAtMethod, lanes, Expression.Constant(offset)), ClrType);

    internal virtual void Encode(Expression value, ParameterExpression results, int offset, List<Expression> body)
        => body.Add(Expression.Call(Expression.Constant(this), PackAtMethod, Expression.Convert(value, typeof(object)), results,
            Expression.Constant(offset)));
}
