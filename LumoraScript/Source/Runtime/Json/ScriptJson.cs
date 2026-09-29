// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A JSON value a script holds: null, true or false, a number, text, an array or an object. A value never
// changes. An edit gives a new one that shares with the old whatever it did not touch, so a value can be
// kept, handed on or put inside another and nothing changes under anyone.
//
// Each counts against the instance's collection budget, like a list. It nests at most MaxDepth deep:
// reading, writing and comparing walk it on the stack, and a stack that runs out takes the process with
// it. A part shared twice counts twice in Size, as it does for anything that walks the value. Two values
// are equal when they hold the same thing: numbers by value, objects whatever the order of their keys.
// -xlinka
public abstract class ScriptJson : ScriptCollection, IEquatable<ScriptJson>
{
    public const int MaxDepth = 64;

    private int _hash;

    private protected ScriptJson(ScriptCollectionBudget budget) : base(budget)
    {
    }

    public abstract ScriptJsonKind Kind { get; }

    // Every value inside, itself included.
    public abstract long Size { get; }

    // Arrays and objects deep: 0 for a plain value.
    public abstract int Depth { get; }

    public bool Equals(ScriptJson? other)
        => other is not null && (ReferenceEquals(this, other) || (other.Kind == Kind && other.GetHashCode() == GetHashCode() && Same(other)));

    public override bool Equals(object? obj) => obj is ScriptJson other && Equals(other);

    public override int GetHashCode()
    {
        if (_hash == 0)
        {
            int hash = Hash();
            _hash = hash == 0 ? 1 : hash;
        }
        return _hash;
    }

    // As compact JSON, cut short past a screenful; To Text is the node for all of it.
    public override string ToString() => ScriptJsonWriter.Preview(this, 1024);

    // Kind is already the same.
    private protected abstract bool Same(ScriptJson other);

    private protected abstract int Hash();

    // What a value is, the way a sentence says it.
    public static string Describe(ScriptJson? json) => json?.Kind switch
    {
        ScriptJsonKind.Object => "an object",
        ScriptJsonKind.Array => "an array",
        ScriptJsonKind.String => "text",
        ScriptJsonKind.Number => "a number",
        ScriptJsonKind.Bool => "true or false",
        ScriptJsonKind.Null => "null",
        _ => "nothing",
    };

    internal static void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
            throw new ScriptLimitException($"JSON nests at most {MaxDepth} deep");
    }
}
