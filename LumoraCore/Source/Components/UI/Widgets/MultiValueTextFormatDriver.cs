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
    public readonly SyncRef<IField<string>> Target;

    private FieldDrive<string>? _drive;
    private IField<string>? _linkedTarget;
    private object?[] _args = Array.Empty<object?>();
    private object?[] _lastArgs = Array.Empty<object?>();
    private string? _lastFormat;
    private string? _lastResult;

    public MultiValueTextFormatDriver()
    {
        Sources = new SyncRefList<IField>(this);
        Format = new Sync<string>(this, "{0}");
        Target = new SyncRef<IField<string>>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        _drive = new FieldDrive<string>(World);
    }

    public override void OnDestroy()
    {
        _drive?.Release();
        _drive = null;
        _linkedTarget = null;
        base.OnDestroy();
    }

    // SyncRefList only relays membership changes, not value changes on the referenced
    // fields, so OnChanges won't fire when a source value moves. Poll instead and only
    // reformat when something actually changed. - xlinka
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (!EnsureDrive())
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
            _drive!.SetValue(result);
        }
    }

    private bool EnsureDrive()
    {
        if (_drive == null)
            return false;

        var target = Target.Target;
        if (ReferenceEquals(target, _linkedTarget))
            return _drive.IsLinkValid;

        _drive.ReleaseLink();
        _linkedTarget = null;
        _lastResult = null;

        if (target == null)
            return false;

        _drive.DriveTarget(target);
        _linkedTarget = target;
        return _drive.IsLinkValid;
    }
}
