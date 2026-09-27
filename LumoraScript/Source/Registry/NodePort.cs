// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A data port. Port names are the C# parameter names with the first letter lowered, so a payload
// property "Presser" and a parameter "presser" name the same port.
public sealed class NodePort
{
    public const int MaxNameLength = 64;

    public string Name { get; }
    public ScriptType Type { get; }
    public object? Default { get; }
    public bool HasDefault { get; }

    public NodePort(string name, ScriptType type, object? defaultValue, bool hasDefault)
    {
        Name = name;
        Type = type;
        Default = defaultValue;
        HasDefault = hasDefault;
    }

    public static string NormalizeName(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException($"'{name}' is not a usable port name");
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || !char.IsAsciiLetter(name[0]))
            return false;
        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }
        return true;
    }

    public override string ToString() => $"{Name}:{Type.Name}";
}
