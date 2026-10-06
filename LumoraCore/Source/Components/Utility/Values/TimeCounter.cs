// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// A count of seconds that every machine agrees on, at a speed that can change without the count jumping.
// Read through Value: a script reading this component reads that.
//
// The count is the session clock times Speed plus Offset. When Speed changes the authority moves Offset so
// the count carries on from where it was, then every machine works the same number out for itself and
// writes it locally; nothing but a speed change ever goes on the wire. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class TimeCounter : Component
{
    // Seconds counted per second. Negative counts down.
    public readonly Sync<float> Speed;

    public readonly Sync<double> Offset;

    public readonly Sync<double> Value;

    // The speed Offset was last worked out for.
    private readonly Sync<float> _countedSpeed;

    private readonly FieldDrive<double> _value;

    public TimeCounter()
    {
        Speed = new Sync<float>(this, 1f);
        Offset = new Sync<double>(this, 0d);
        Value = new Sync<double>(this, 0d);
        _countedSpeed = new Sync<float>(this, 1f);
        _value = new FieldDrive<double>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        if (_value.ShouldApplyDefault)
            _value.DriveTarget(Value);
    }

    public override void OnUpdate(float delta)
    {
        var world = World;
        if (world == null)
            return;

        double now = UtilityClock.Seconds(world);
        if (world.IsAuthority && Speed.Value != _countedSpeed.Value)
        {
            double count = Offset.Value + now * _countedSpeed.Value;
            _countedSpeed.Value = Speed.Value;
            Offset.Value = count - now * Speed.Value;
        }
        _value.SetValue(Offset.Value + now * _countedSpeed.Value);
    }
}
