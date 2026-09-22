// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Joins a list of strings into one text field.
[ComponentCategory("Utility/Text")]
[DefaultUpdateOrder(-100)]
public class StringConcatenationDriver : Component
{
    public readonly SyncFieldList<string> Strings;

    public readonly Sync<string> Separator;

    // Drive nothing but null when every part is null, so an empty label stays empty rather than
    // becoming a row of separators.
    public readonly Sync<bool> NullWhenAllNull;

    public readonly FieldDrive<string> Target;

    private readonly List<string?> _last = new();
    private readonly StringBuilder _builder = new();
    private string? _joined;
    private bool _hasJoined;

    public StringConcatenationDriver()
    {
        Strings = new SyncFieldList<string>();
        Separator = new Sync<string>(this, string.Empty);
        NullWhenAllNull = new Sync<bool>(this, false);
        Target = new FieldDrive<string>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;

        // A list element's change never reaches its owning component - the element's parent is the list,
        // not the worker - so the parts are compared here instead. String comparisons on a handful of
        // parts cost nothing next to rebuilding the join every frame. -xlinka
        if (!HasChanged())
        {
            Target.SetValue(_joined!);
            return;
        }

        Snapshot();
        _joined = Join();
        _hasJoined = true;
        Target.SetValue(_joined!);
    }

    private bool HasChanged()
    {
        if (!_hasJoined || _last.Count != Strings.Count)
            return true;
        for (int i = 0; i < _last.Count; i++)
        {
            if (!string.Equals(_last[i], Strings[i], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void Snapshot()
    {
        _last.Clear();
        for (int i = 0; i < Strings.Count; i++)
            _last.Add(Strings[i]);
    }

    private string? Join()
    {
        if (NullWhenAllNull.Value)
        {
            bool allNull = true;
            for (int i = 0; i < Strings.Count; i++)
            {
                if (Strings[i] != null)
                {
                    allNull = false;
                    break;
                }
            }
            if (allNull)
                return null;
        }

        string separator = Separator.Value ?? string.Empty;
        _builder.Clear();
        for (int i = 0; i < Strings.Count; i++)
        {
            if (i > 0)
                _builder.Append(separator);
            _builder.Append(Strings[i] ?? string.Empty);
        }
        return _builder.ToString();
    }
}
