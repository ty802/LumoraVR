// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.UI;

// The dial a value item opens on the context menu: a ring round the hub with a gap at the bottom. Nought
// sits at the lower left end of the gap and the whole value at the lower right, clockwise over the top, so
// half is straight up. Angles are degrees clockwise from three o'clock, as everywhere on the ring.
//
// An input only moves the value once it has been through the middle, so a dial that opens under a pointer
// already out on the ring never jumps. From there the value follows it round. A pointer swung across the
// gap stops at the end it left, the way a knob stops, and lets go once it comes back to that end; and each
// input writes only while it moves, so a laser resting on the ring never fights the stick. -xlinka
public static class ContextMenuDial
{
    public const float Start = 120f;
    public const float Sweep = 300f;
    // Within this of an end the value is the end.
    public const float SnapReach = 0.03f;
    // A pointer stopped at an end lets go once it is back this close to it.
    public const float ReleaseReach = 0.12f;
    // A rested input takes the value back from another after moving this far.
    public const float TakeOver = 0.02f;
    // More than this in one step can only be a swing over the gap.
    private const float Jump = 0.5f;

    public static float Clamp01(float value) => float.IsNaN(value) ? 0f : System.Math.Clamp(value, 0f, 1f);

    // Where on the ring a value sits.
    public static float AngleOf(float value) => Wrap(Start + Sweep * Clamp01(value));

    // The value an angle points at; in the gap, the nearer end.
    public static float ValueAt(float angle)
    {
        float along = Wrap(angle - Start);
        if (along <= Sweep)
            return along / Sweep;
        return along - Sweep < (360f - Sweep) * 0.5f ? 1f : 0f;
    }

    public static float Snap(float value)
    {
        value = Clamp01(value);
        if (value <= SnapReach)
            return 0f;
        if (value >= 1f - SnapReach)
            return 1f;
        return value;
    }

    // The angle of a point about the middle, canvas or stick: x right, y up.
    public static float AngleAt(float x, float y) => Wrap(MathF.Atan2(-y, x) * (180f / MathF.PI));

    public static float Wrap(float degrees) => degrees - MathF.Floor(degrees / 360f) * 360f;

    public static string Percent(float value) => $"{MathF.Round(Clamp01(value) * 100f):0}%";

    // One input turning the dial: the pointer, or the stick.
    public sealed class Follower
    {
        private bool _armed;
        private float? _last;
        private float? _stop;
        private bool _owns = true;
        private float _anchor;

        public bool IsArmed => _armed;

        // Held at an end after a swing over the gap.
        public bool IsStopped => _stop != null;

        // In the middle: armed, with nowhere it was pointing.
        public void Centre()
        {
            _armed = true;
            _last = null;
            _stop = null;
            _owns = true;
        }

        // Pointing at this angle: the value to write, or null to leave it as it is.
        public float? Point(float angle)
        {
            if (!_armed)
                return null;
            float raw = ValueAt(angle);
            float wanted;
            if (_last is not float last)
            {
                wanted = raw;
                _owns = true;
            }
            else if (_stop is float end)
            {
                if (MathF.Abs(raw - end) <= ReleaseReach)
                {
                    _stop = null;
                    wanted = raw;
                }
                else
                {
                    wanted = end;
                }
            }
            else if (MathF.Abs(raw - last) > Jump)
            {
                _stop = last >= 0.5f ? 1f : 0f;
                wanted = _stop.Value;
            }
            else
            {
                wanted = raw;
            }
            _last = raw;
            if (!_owns)
            {
                if (MathF.Abs(raw - _anchor) < TakeOver)
                    return null;
                _owns = true;
            }
            return Snap(wanted);
        }

        // Another input wrote: this one keeps quiet until it moves a step of its own.
        public void Yield()
        {
            if (!_owns)
                return;
            _owns = false;
            _anchor = _last ?? 0f;
        }
    }
}
