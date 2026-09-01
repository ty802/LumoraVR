// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Formats one value into a text field.
//
// The formatting only runs when the value or the format string actually moved. That guard is the whole
// point: a text field is a string, so an unguarded driver would allocate a new one every frame for
// every counter in the world, and the mesh behind it would re-tessellate on each one. -xlinka
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

    public ValueTextFormatDriver()
    {
        Source = new SyncRef<IField<T>>(this);
        Format = new Sync<string>(this, "{0}");
        Text = new FieldDrive<string>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
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

        Text.SetValue(_formatted!);
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

// Drives a text field with the current date and time.
//
// The clock reading is this machine's, in this machine's zone, so the drive is local-value-only: two
// peers in different time zones legitimately see different strings, and a driven field is excluded from
// value sync in both directions, so neither can push its reading onto the other. -xlinka
[ComponentCategory("Utility/Text")]
[DefaultUpdateOrder(-100)]
public class CurrentDateTimeTextDriver : Component
{
    public readonly Sync<string> Format;

    public readonly Sync<bool> UseUTC;

    // Seconds between re-formats. A clock showing seconds needs a fraction of one; a clock showing the
    // date does not need it at all.
    public readonly Sync<float> UpdateInterval;

    public readonly FieldDrive<string> Target;

    private string? _formatted;
    private double _nextUpdate;

    public CurrentDateTimeTextDriver()
    {
        Format = new Sync<string>(this, "HH:mm:ss");
        UseUTC = new Sync<bool>(this, false);
        UpdateInterval = new Sync<float>(this, 0.25f);
        Target = new FieldDrive<string>(this) { LocalValueOnly = true };
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // An edited format has to show up now, not at the end of the current interval.
        _nextUpdate = 0d;
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;

        double now = UtilityClock.Seconds(World);
        if (now >= _nextUpdate)
        {
            float interval = UpdateInterval.Value;
            _nextUpdate = now + (interval > 0f ? interval : 0.25f);
            _formatted = Render();
        }

        Target.SetValue(_formatted!);
    }

    private string? Render()
    {
        var stamp = UseUTC.Value ? DateTime.UtcNow : DateTime.Now;
        string format = Format.Value;
        if (string.IsNullOrEmpty(format))
            return stamp.ToString();
        try
        {
            return stamp.ToString(format);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

// Counts down to a shared instant and drives the remaining time into a text field.
//
// The deadline is an instant on the session clock rather than a number of seconds ticked down per
// frame, so every peer reads the same time left, a late joiner is immediately correct, and a stalled
// frame cannot make one person's timer drift behind everyone else's. -xlinka
[ComponentCategory("Utility/Text")]
[DefaultUpdateOrder(-100)]
public class TextCountdownClock : Component
{
    // The instant the countdown reaches zero.
    public readonly SyncTimeAnchor Deadline = new();

    // What Restart counts down from, in seconds.
    public readonly Sync<double> Duration;

    // Keep counting past zero, into negative time.
    public readonly Sync<bool> AllowNegative;

    public readonly Sync<bool> ShowTenths;

    public readonly FieldDrive<string> Text;

    private string? _formatted;
    private long _lastTick = long.MinValue;

    public TextCountdownClock()
    {
        Duration = new Sync<double>(this, 60d);
        AllowNegative = new Sync<bool>(this, false);
        ShowTenths = new Sync<bool>(this, false);
        Text = new FieldDrive<string>(this) { LocalValueOnly = true };
    }

    // Seconds left. Counts the full duration while the deadline is unset, so a countdown that has never
    // been started reads as ready rather than as finished.
    public double Remaining
    {
        get
        {
            if (!Deadline.IsSet)
                return Duration.Value;
            double left = -Deadline.Elapsed;
            return AllowNegative.Value || left > 0d ? left : 0d;
        }
    }

    public bool IsFinished => Deadline.IsSet && Deadline.Elapsed >= 0d;

    [SyncMethod]
    public void Restart()
    {
        // The anchor replicates, so one peer sets it and everyone counts from it. Two peers restarting
        // at once would otherwise write two different instants.
        if (World?.IsAuthority == true)
            Deadline.SetIn(Duration.Value);
    }

    [SyncMethod]
    public void Stop()
    {
        if (World?.IsAuthority == true)
            Deadline.Clear();
    }

    public override void OnUpdate(float delta)
    {
        if (!Text.IsLinkValid)
            return;

        double remaining = Remaining;
        // Rebuild only when the DISPLAYED value moves. At tenths that is ten strings a second instead of
        // one per frame, and at whole seconds it is one.
        double resolution = ShowTenths.Value ? 10d : 1d;
        long tick = (long)System.Math.Floor(remaining * resolution);
        if (tick != _lastTick)
        {
            _lastTick = tick;
            _formatted = Render(remaining);
        }

        Text.SetValue(_formatted!);
    }

    private string Render(double remaining)
    {
        bool negative = remaining < 0d;
        double magnitude = negative ? -remaining : remaining;

        int hours = (int)(magnitude / 3600d);
        int minutes = (int)(magnitude / 60d) % 60;
        int seconds = (int)magnitude % 60;
        string sign = negative ? "-" : string.Empty;

        string body = hours > 0
            ? $"{sign}{hours}:{minutes:00}:{seconds:00}"
            : $"{sign}{minutes}:{seconds:00}";

        if (!ShowTenths.Value)
            return body;

        int tenths = (int)(magnitude * 10d) % 10;
        return $"{body}.{tenths}";
    }
}
