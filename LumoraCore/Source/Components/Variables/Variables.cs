// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Variables;

// Path parsing, name validation, scope lookup, and the read/write/create front door for named
// variables. Everything a caller needs to touch a variable without knowing which component holds
// it lives here.
public static class Variables
{
    // Empty is valid and means "bind nothing", so a freshly attached component with a blank name is
    // not an error, it is simply inert.
    //
    // The character set is deliberately narrow. '/' would collide with the scope separator, and the
    // rest of punctuation is reserved so a future path syntax has room without invalidating names
    // people already saved. -xlinka
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        foreach (char c in name!)
        {
            if (c == ' ' || c == '-' || c == '.' || c == '_')
                continue;
            if (!char.IsLetterOrDigit(c))
                return false;
        }
        return true;
    }

    // Null for a name that is not spellable, so an illegal name binds nothing rather than binding
    // something the author did not type.
    public static string? ProcessName(string? name)
    {
        if (!IsValidName(name))
            return null;
        return name?.Trim();
    }

    // Split "Scope/Name" into its halves. Without a '/' the whole path is the variable name and the
    // scope is left unnamed, which means "nearest scope that accepts unnamed binding".
    public static void ParsePath(string? path, out string? scopeName, out string? variableName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            scopeName = null;
            variableName = null;
            return;
        }

        int separator = path!.IndexOf('/');
        if (separator < 0)
        {
            scopeName = null;
            variableName = ProcessName(path);
            return;
        }

        scopeName = ProcessName(path.Substring(0, separator));
        variableName = ProcessName(path.Substring(separator + 1));
    }

    // Resolve the scope a path binds to: the nearest ancestor (this slot included) that matches.
    //
    // Named lookup matches the scope's name exactly. Unnamed lookup skips scopes marked
    // DirectBindingOnly, which is the whole point of that flag: an avatar can carry a scope full of
    // rig knobs without every stray variable dropped into the avatar accidentally landing in it.
    public static VariableScope? FindScope(this Slot? slot, string? scopeName)
    {
        if (slot == null || slot.IsDestroyed)
            return null;

        if (string.IsNullOrWhiteSpace(scopeName))
            return slot.GetComponentInParents<VariableScope>(s => !s.DirectBindingOnly.Value);

        return slot.GetComponentInParents<VariableScope>(
            s => string.Equals(s.ActiveName, scopeName, StringComparison.Ordinal));
    }

    // False when nothing readable is bound.
    public static bool ReadVariable<T>(this Slot? slot, string? path, out T value)
    {
        ParsePath(path, out var scopeName, out var variableName);
        if (string.IsNullOrWhiteSpace(variableName))
        {
            value = Networking.Sync.SyncCoder.GetDefault<T>();
            return false;
        }

        var scope = slot.FindScope(scopeName);
        if (scope == null)
        {
            value = Networking.Sync.SyncCoder.GetDefault<T>();
            return false;
        }

        return scope.TryRead(variableName, out value);
    }

    public static VariableWriteResult WriteVariable<T>(this Slot? slot, string? path, T value)
    {
        ParsePath(path, out var scopeName, out var variableName);
        if (string.IsNullOrWhiteSpace(variableName))
            return VariableWriteResult.Invalid;

        var scope = slot.FindScope(scopeName);
        if (scope == null)
            return VariableWriteResult.NotFound;

        return scope.TryWrite(variableName, value);
    }

    // Attach a variable of the right shape for T and give it a starting value. Values go to a
    // ValueVariable<T>, world elements to a ReferenceVariable<T>. False when T is neither, which is
    // the honest answer for a type that could not be saved or replicated anyway.
    public static bool CreateVariable<T>(this Slot slot, string name, T value, bool persistent = true)
    {
        if (slot == null)
            throw new ArgumentNullException(nameof(slot));

        if (IsSupportedValueType(typeof(T)))
        {
            var variable = slot.AttachComponent<ValueVariable<T>>();
            variable.VariableName.Value = name;
            variable.Value.Value = value;
            variable.Persistent = persistent;
            variable.RefreshBinding();
            return true;
        }

        if (typeof(IWorldElement).IsAssignableFrom(typeof(T)))
        {
            // T is only known to be a world element at runtime, so the constrained generic has to be
            // reached reflectively.
            var method = typeof(Variables)
                .GetMethod(nameof(CreateReferenceVariable), BindingFlags.Static | BindingFlags.Public)!
                .MakeGenericMethod(typeof(T));
            method.Invoke(null, new object?[] { slot, name, value, persistent });
            return true;
        }

        return false;
    }

    public static ReferenceVariable<T> CreateReferenceVariable<T>(this Slot slot, string name, T? target, bool persistent = true)
        where T : class, IWorldElement
    {
        if (slot == null)
            throw new ArgumentNullException(nameof(slot));

        var variable = slot.AttachComponent<ReferenceVariable<T>>();
        variable.VariableName.Value = name;
        variable.Reference.Target = target!;
        variable.Persistent = persistent;
        variable.RefreshBinding();
        return variable;
    }

    // Persistence is the binding constraint: a variable whose value cannot be written to a data tree
    // would attach happily and then throw on the first save, so the guard is the save coder itself
    // rather than a hand-kept list that would drift away from it. -xlinka
    public static bool IsSupportedValueType(Type type)
        => type != null && DataTreeCoder.IsSupported(type);
}
