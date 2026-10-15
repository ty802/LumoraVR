// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Input;

// The first press is handled by the caller. Repeats require that same press to stay down;
// focusing a field with a key already held must never start deleting its contents.
public struct KeyRepeat
{
    public const float DefaultDelay = 0.45f;
    public const float DefaultInterval = 0.05f;
    private bool _armed;
    private float _remaining;

    public void Begin(float delay = DefaultDelay)
    {
        _armed = true;
        _remaining = float.IsFinite(delay) ? MathF.Max(0f, delay) : DefaultDelay;
    }

    public void Reset() => _armed = false;

    public int Advance(bool held, float delta, float interval = DefaultInterval)
    {
        if (!held) Reset();
        if (!_armed || !float.IsFinite(delta) || delta <= 0f) return 0;
        interval = float.IsFinite(interval) ? MathF.Max(0.01f, interval) : DefaultInterval;
        _remaining -= delta;
        int count = 0;
        // Catch up a short hitch, but never erase an entire field after a long stall.
        while (_remaining <= 0f && count < 4) { count++; _remaining += interval; }
        if (_remaining <= 0f) _remaining = interval;
        return count;
    }
}
