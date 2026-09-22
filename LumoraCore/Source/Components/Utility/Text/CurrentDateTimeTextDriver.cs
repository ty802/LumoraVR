// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

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
