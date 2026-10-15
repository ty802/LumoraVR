// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Input;

// The trigger read at two depths. The press is a latch at half travel with a narrow band under it,
// there only so a finger resting near the cut cannot chatter: a wide band traps a rest that sits
// between the cuts, and the next click never lands. The deep level is the click at the end of travel
// on a controller that has one, and a second latch near the top on one that does not.
//
// A finger resting past half holds the press the whole time, so a full squeeze from that rest crosses
// no cut and clicks nothing. The deep edges are for that finger. Going deep counts as a fresh press
// when the pull eased off the deep level since the last one, or sat still for a moment first; a
// squeeze from rest goes deep with neither, and the press edge already carried it, so it acts once.
// Both deep edges only fire under a press that stays held across the update; letting go is the plain
// release. -xlinka
public struct TriggerLatch
{
    public const float PressAt = 0.5f;
    public const float ReleaseAt = 0.42f;
    public const float DeepPressAt = 0.9f;
    public const float DeepReleaseAt = 0.75f;

    // A pull that stays within RestBand of where it settled for RestSeconds is a rest, and arms the
    // next deep squeeze. Shorter, and a squeeze that pauses on the way in acts twice.
    public const float RestBand = 0.05f;
    public const float RestSeconds = 0.4f;

    // A hitch counts as one ordinary frame toward the rest, so a stall straight after a press (the
    // press opened something heavy) cannot read as a finger holding still.
    private const float RestStepLimit = 0.05f;

    public bool Held { get; private set; }
    public bool Deep { get; private set; }

    // Set for exactly one update each.
    public bool DeepPressed { get; private set; }
    public bool DeepReleased { get; private set; }

    private PressLatch _press;
    private PressLatch _deepByValue;
    private bool _armed;
    private float _restValue;
    private float _restSeconds;

    // useClick takes the deep level off the click instead of the value. hasDeep is off for a pinch,
    // which has no second level.
    public void Update(float value, bool click, bool useClick, bool hasDeep, float deltaTime)
    {
        DeepPressed = false;
        DeepReleased = false;

        bool wasHeld = Held;
        bool wasDeep = Deep;
        Held = _press.Update(value, PressAt, ReleaseAt);
        bool deepByValue = _deepByValue.Update(value, DeepPressAt, DeepReleaseAt);
        Deep = Held && hasDeep && (useClick ? click : deepByValue);

        // Nothing deep happens off a released trigger or a pinch, and the press edge carries the
        // squeeze that made it, however deep that squeeze went.
        if (!Held || !wasHeld || !hasDeep)
        {
            _armed = false;
            _restValue = value;
            _restSeconds = 0f;
            return;
        }

        if (Deep && !wasDeep)
        {
            DeepPressed = _armed;
            _armed = false;
        }
        else if (!Deep && wasDeep)
        {
            DeepReleased = true;
            _armed = true;
        }

        if (Deep || MathF.Abs(value - _restValue) > RestBand)
        {
            _restValue = value;
            _restSeconds = 0f;
            return;
        }

        _restSeconds += MathF.Min(deltaTime, RestStepLimit);
        if (_restSeconds >= RestSeconds)
            _armed = true;
    }

    public void Reset()
    {
        _press.Reset();
        _deepByValue.Reset();
        Held = false;
        Deep = false;
        DeepPressed = false;
        DeepReleased = false;
        _armed = false;
        _restValue = 0f;
        _restSeconds = 0f;
    }
}
