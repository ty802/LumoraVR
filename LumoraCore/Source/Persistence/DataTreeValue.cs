// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

// URLs are stored with a leading '@' so asset references can be found when bundling a save;
// raw strings that happen to start with '@' are escaped to '@@'.
public sealed class DataTreeValue : DataTreeNode
{
    public override DataTreeNodeType NodeType => DataTreeNodeType.Value;

    public IConvertible? Value { get; private set; }

    public bool IsNull => Value == null;

    public bool IsUrl => Value is string { Length: > 1 } s && s[0] == '@' && s[1] != '@';

    private DataTreeValue() { }

    public DataTreeValue(IConvertible? value) => Value = Preprocess(value);

    public DataTreeValue(string? value) => Value = PreprocessString(value);

    // As written, never ToString: that lowercases the host, and a local:// machine id is case-sensitive.
    public DataTreeValue(Uri? url) => Value = url == null ? null : "@" + url.OriginalString;

    // Skips the '@' escaping: a leading '@' then reads back as a URL.
    public static DataTreeValue RawString(string? value) => new() { Value = value };

    public static DataTreeValue FromEnum<E>(E value) where E : struct, Enum => new(value.ToString());

    public T Extract<T>()
    {
        Type type = typeof(T);

        if (type.IsEnum)
            return (T)Enum.Parse(type, Extract<string>());

        if (type == typeof(string))
        {
            if (Value is not string text)
                return default!;
            // De-escape a leading '@@' back to '@'; reject genuine URLs.
            if (text.Length > 1 && text[0] == '@')
            {
                if (text[1] == '@')
                    return (T)(object)text.Substring(1);
                throw new InvalidOperationException("DataTreeValue holds a URL, not a raw string.");
            }
            return (T)(object)text;
        }

        if (type == typeof(Uri))
            return (T)(object)ExtractUrl()!;

        if (Value == null)
            return default!;

        return (T)Value.ToType(type, null);
    }

    public Uri? ExtractUrl()
    {
        if (!IsUrl)
            return null;
        return new Uri(((string)Value!).Substring(1));
    }

    private static IConvertible? Preprocess(IConvertible? value)
        => value is string s ? PreprocessString(s) : value;

    private static string? PreprocessString(string? value)
        => value != null && value.Length > 1 && value[0] == '@' ? "@" + value : value;

    public override string ToString() => $"DataTreeValue: {Value}";
}
