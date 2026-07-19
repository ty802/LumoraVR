// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Utility")]
public class MultiValueTextFormatDriver : Component
{
    public readonly SyncRefList<IField> Sources;
    public readonly Sync<string> Format;
    // Target and drive are one member: pointing this at a string field is what establishes the drive,
    // on every peer and after a load.
    public readonly FieldDrive<string> Target;

    private IField<string>? _linkedTarget;
    private object?[] _args = Array.Empty<object?>();
    private object?[] _lastArgs = Array.Empty<object?>();
    private string? _lastFormat;
    private string? _lastResult;

    public MultiValueTextFormatDriver()
    {
        Sources = new SyncRefList<IField>(this);
        Format = new Sync<string>(this, "{0}");
        Target = new FieldDrive<string>(this);
    }

    // SyncRefList only relays membership changes, not value changes on the referenced
    // fields, so OnChanges won't fire when a source value moves. Poll instead and only
    // reformat when something actually changed. - xlinka
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        // A retarget invalidates the cached result so the new field gets written even when the format
        // output happens to be unchanged.
        var linked = Target.Target;
        if (!ReferenceEquals(linked, _linkedTarget))
        {
            _linkedTarget = linked;
            _lastResult = null;
        }

        if (!Target.IsLinkValid)
            return;

        int n = Sources.Count;
        if (_args.Length != n)
        {
            _args = new object?[n];
            _lastArgs = new object?[n];
            _lastResult = null;
        }

        bool changed = !string.Equals(_lastFormat, Format.Value, StringComparison.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var field = Sources[i];
            object? value = field != null && !ReferenceEquals(field, _linkedTarget) ? field.BoxedValue : null;
            _args[i] = value;
            if (!Equals(value, _lastArgs[i]))
                changed = true;
        }

        if (!changed)
            return;

        _lastFormat = Format.Value;
        Array.Copy(_args, _lastArgs, n);

        string result;
        try
        {
            result = Format.Value != null ? string.Format(Format.Value, _args) : string.Empty;
        }
        catch
        {
            result = string.Empty;
        }

        if (!string.Equals(result, _lastResult, StringComparison.Ordinal))
        {
            _lastResult = result;
            Target.SetValue(result);
        }
    }
}
