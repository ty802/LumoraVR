// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Formats one value into a text field.
//
// Works only when something it reads changed: its own members, the source field, or somebody writing over
// the text it drives. A world with a thousand of these paid a thousand calls a frame to find nothing had
// moved. The formatting still runs only when the value or the format string moved, so a counter does not
// allocate a new string and re-tessellate its mesh for nothing. -xlinka
[ComponentCategory("Utility/Text")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueTextFormatDriver<T> : Component
{
    // A format that expands into megabytes is a hang, not a label.
    private const int MaxLength = 262144;

    public readonly SyncRef<IField<T>> Source;

    // Standard composite format; the value is argument zero.
    public readonly Sync<string> Format;

    public readonly FieldDrive<string> Text;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    private T _lastValue = default!;
    private string? _lastFormat;
    private string? _formatted;
    private bool _hasFormatted;
    private ChangeFollow? _follow;

    public ValueTextFormatDriver()
    {
        Source = new SyncRef<IField<T>>(this);
        Format = new Sync<string>(this, "{0}");
        Text = new FieldDrive<string>(this) { LocalValueOnly = true };
    }

    public override void OnDestroy()
    {
        _follow?.Clear();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var follow = _follow ??= new ChangeFollow(this);
        follow.Begin();
        follow.AddRef(Source);
        follow.Add(Text.Target);
        follow.End();

        if (!Text.IsLinkValid)
            return;

        var source = Source.Target;
        // Formatting a field into itself is a loop: the output becomes the next frame's input.
        if (ReferenceEquals(source, Text.Target))
            return;

        T value = source != null ? source.Value : SyncCoder.GetDefault<T>();
        string format = Format.Value;

        if (!_hasFormatted
            || !SyncCoder.Equals(_lastValue, value)
            || !string.Equals(_lastFormat, format, StringComparison.Ordinal))
        {
            _lastValue = value;
            _lastFormat = format;
            _formatted = Render(format, value);
            _hasFormatted = true;
        }

        follow.BeginQuiet();
        try
        {
            Text.SetValue(_formatted!);
        }
        finally
        {
            follow.EndQuiet();
        }
    }

    private static string? Render(string? format, T value)
    {
        if (format == null)
            return null;
        try
        {
            string text = string.Format(format, value);
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength);
        }
        catch (FormatException)
        {
            // A half-typed format string is normal while somebody is editing one.
            return null;
        }
    }
}
