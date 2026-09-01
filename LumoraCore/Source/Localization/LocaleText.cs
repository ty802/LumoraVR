// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Localization;

// A piece of user-facing text that may or may not be translatable.
//
// Two shapes, one type. A plain string converts implicitly and resolves straight back to itself, so
// every existing raw-string call site keeps working untouched and costs nothing extra. A KEYED value
// carries the lookup key plus the English text written at the call site, and resolves through the
// locale tables.
//
// The English fallback is not decoration. It is what the screen shows when a key is missing from
// every table, which is the normal state for a string somebody has just added and not yet put in
// en.json. A label must never come back empty. -xlinka
public readonly struct LocaleText : IEquatable<LocaleText>
{
    public static readonly LocaleText Empty = default;

    // Null/empty means "not translatable, use Fallback verbatim".
    public readonly string? Key;

    // Literal text for a plain value; the English source string for a keyed one.
    public readonly string? Fallback;

    // Positional string.Format arguments. Null for the overwhelming majority of strings.
    public readonly object[]? Args;

    public LocaleText(string? key, string? fallback, object[]? args)
    {
        Key = string.IsNullOrEmpty(key) ? null : key;
        Fallback = fallback;
        Args = args != null && args.Length > 0 ? args : null;
    }

    public bool IsKey => Key != null;

    public bool IsEmpty => Key == null && string.IsNullOrEmpty(Fallback);

    public static LocaleText Keyed(string key, string english, params object[] args)
        => new LocaleText(key, english, args);

    public static LocaleText Plain(string? text) => new LocaleText(null, text, null);

    // Same key, different arguments. Handy for a label whose numbers change but whose wording does not.
    public LocaleText WithArgs(params object[] args) => new LocaleText(Key, Fallback, args);

    public string Resolve() => LocaleManager.Resolve(in this);

    public override string ToString() => Resolve();

    public bool Equals(LocaleText other)
    {
        if (!string.Equals(Key, other.Key, StringComparison.Ordinal)
            || !string.Equals(Fallback, other.Fallback, StringComparison.Ordinal))
        {
            return false;
        }
        if (ReferenceEquals(Args, other.Args))
            return true;
        if (Args == null || other.Args == null || Args.Length != other.Args.Length)
            return false;
        for (int i = 0; i < Args.Length; i++)
        {
            if (!Equals(Args[i], other.Args[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is LocaleText other && Equals(other);

    public override int GetHashCode()
    {
        var comparer = EqualityComparer<string?>.Default;
        int hash = comparer.GetHashCode(Key!) * 397;
        hash ^= comparer.GetHashCode(Fallback!);
        return Args == null ? hash : hash * 397 ^ Args.Length;
    }

    public static bool operator ==(in LocaleText left, in LocaleText right) => left.Equals(right);

    public static bool operator !=(in LocaleText left, in LocaleText right) => !left.Equals(right);

    // The whole incremental-adoption story lives on this line: anywhere that took a string still takes
    // one, and it stays a plain untranslated value until somebody gives it a key.
    public static implicit operator LocaleText(string? text) => new LocaleText(null, text, null);
}

public static class LocaleTextExtensions
{
    // "Settings.Category.General".AsLocale("General")
    public static LocaleText AsLocale(this string key, string english, params object[] args)
        => new LocaleText(key, english, args);
}
