// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;

namespace Lumora.Core.Components.Variables;

// Declares a named variable holding a Type, for content that picks a component or a value type by
// name and hands it around.
//
// The type travels as its full name, not as a runtime handle: a handle means nothing to another
// peer or to a save file. That also means the value can come back unresolved on a client missing
// the assembly, which reads as an empty variable rather than a crash. -xlinka
[ComponentCategory("Data/Variables")]
public class TypeVariable : VariableBase<Type>
{
    public readonly Sync<string> TypeName;

    // Adopt the identity's existing type on binding, or replace it with this one.
    public readonly Sync<bool> OverrideOnBind;

    private string? _resolvedName;
    private Type? _resolvedType;

    public TypeVariable()
    {
        TypeName = new Sync<string>(this, "");
        OverrideOnBind = new Sync<bool>(this, false);
    }

    public override bool IsWriteOnly => false;

    public override bool OverridesOnBind => OverrideOnBind.Value;

    public override bool AcceptsWrites => !TypeName.IsDriven;

    protected override bool HasLocalValue => true;

    // Null when the name is empty or names nothing this build has.
    public Type? Type => Resolve(TypeName.Value);

    protected override Type LocalValue
    {
        get => Resolve(TypeName.Value)!;
        set => TypeName.Value = value?.FullName ?? "";
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        string name = TypeName.Value ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            InspectorStats.AddRow(ui, "Type", "<none>");
            return;
        }
        InspectorStats.AddRow(ui, "Type", Resolve(name) != null ? name : $"UNRESOLVED ({name})");
    }

    // Cached because the getter is read on every changes pass, and resolving by name walks every
    // loaded assembly on a miss. Deliberately quiet on failure: an unresolved type here is a client
    // that lacks the assembly, which the inspector row reports, not something to spam the log with.
    private Type? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _resolvedName = name;
            _resolvedType = null;
            return null;
        }

        if (_resolvedType != null && string.Equals(name, _resolvedName, StringComparison.Ordinal))
            return _resolvedType;

        _resolvedName = name;
        _resolvedType = System.Type.GetType(name!);
        if (_resolvedType == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _resolvedType = assembly.GetType(name!);
                if (_resolvedType != null)
                    break;
            }
        }
        return _resolvedType;
    }
}
