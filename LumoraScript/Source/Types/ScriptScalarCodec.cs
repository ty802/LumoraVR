// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Lumora.Wasm;

namespace Lumora.Script;

// A coded value in one lane, stated by a pair of conversions and a text form. A number typed where the
// type takes numbers is written to the document as a number; anything else goes as its text.
public sealed class ScriptScalarCodec<T> : ScriptValueCodec where T : struct
{
    public delegate bool Parser(string text, out T value);

    private readonly Func<ulong, T> _fromLane;
    private readonly Func<T, ulong> _toLane;
    private readonly Parser _parse;
    private readonly Func<T, string> _format;
    private readonly Func<T, double?>? _asNumber;

    public ScriptScalarCodec(WasmValueType lane, Func<ulong, T> fromLane, Func<T, ulong> toLane, Parser parse, Func<T, string> format,
        Func<T, double?>? asNumber = null)
        : base(typeof(T), lane)
    {
        _fromLane = fromLane;
        _toLane = toLane;
        _parse = parse;
        _format = format;
        _asNumber = asNumber;
    }

    public override object Unpack(ReadOnlySpan<ulong> lanes) => _fromLane(lanes[0]);

    public override void Pack(object? value, Span<ulong> lanes) => lanes[0] = _toLane(value is T typed ? typed : default);

    public override bool TryParse(string text, out object? value)
    {
        value = null;
        if (text == null || !_parse(text.Trim(), out var parsed))
            return false;
        value = parsed;
        return true;
    }

    public override string Format(object value) => _format(value is T typed ? typed : default);

    public override GraphValue ToGraph(object value)
    {
        var typed = value is T t ? t : default;
        if (_asNumber?.Invoke(typed) is double number && double.IsFinite(number))
            return GraphValue.Of(number);
        return GraphValue.Of(_format(typed));
    }

    internal override Expression Decode(ParameterExpression lanes, int offset)
        => Expression.Invoke(Expression.Constant(_fromLane), Expression.ArrayIndex(lanes, Expression.Constant(offset)));

    internal override void Encode(Expression value, ParameterExpression results, int offset, List<Expression> body)
        => body.Add(Expression.Assign(Expression.ArrayAccess(results, Expression.Constant(offset)),
            Expression.Invoke(Expression.Constant(_toLane), value)));
}
