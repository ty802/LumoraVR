// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input;

// Press above one threshold, release below a lower one, nothing in between. An analog that rests
// inside the band never flips the state, which is what stops a grip sitting near a single cut from
// chattering a grab on and off. -xlinka
public struct PressLatch
{
    public bool Held { get; private set; }

    public bool Update(float value, float pressAt, float releaseAt)
    {
        if (Held)
        {
            if (value <= releaseAt)
                Held = false;
        }
        else if (value >= pressAt)
        {
            Held = true;
        }
        return Held;
    }

    public void Reset() => Held = false;
}

// One button carrying two jobs on controllers that have no dedicated dash button: a tap is the
// context menu, a hold charges and pops the dash once at full charge. A release after the dash has
// fired is blocked from also counting as a tap, and a release in the gap between the tap limit and
// full charge does nothing at all. -xlinka
public struct MenuChargeLatch
{
    // Charge per second held; full charge at half a second.
    public const float ChargeRate = 2f;

    // A release at or below this charge is a tap.
    public const float TapLimit = 0.75f;

    public float Charge { get; private set; }

    // Set for exactly one update each.
    public bool HoldFired { get; private set; }
    public bool TapFired { get; private set; }

    private bool _wasHeld;
    private bool _fired;
    private bool _blockTap;

    public void Update(bool held, float deltaTime, bool otherHandCharging)
    {
        HoldFired = false;
        TapFired = false;

        if (held)
        {
            if (!_fired)
            {
                Charge += deltaTime * ChargeRate;
                if (Charge >= 1f)
                {
                    _fired = true;
                    _blockTap = true;
                    // With the other hand charging too, this one stays at full instead of firing,
                    // so the other hand sees it and holds back as well: a two-hand hold pops nothing.
                    if (otherHandCharging)
                    {
                        Charge = 1f;
                    }
                    else
                    {
                        HoldFired = true;
                        Charge = 0f;
                    }
                }
            }
        }
        else if (_wasHeld)
        {
            TapFired = !_blockTap && Charge <= TapLimit;
            Charge = 0f;
            _fired = false;
            _blockTap = false;
        }

        _wasHeld = held;
    }

    public void Reset()
    {
        Charge = 0f;
        HoldFired = false;
        TapFired = false;
        _wasHeld = false;
        _fired = false;
        _blockTap = false;
    }
}
