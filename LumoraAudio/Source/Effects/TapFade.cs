// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Audio;

// A delay line's read head that moves by crossfading from the old tap to the new one instead of sliding along,
// so a change of delay time neither clicks nor bends the pitch. One move at a time: a target that changes
// during a crossfade is taken up once it is done.
internal struct TapFade
{
    public int Tap;
    public int Next;
    public int Target;
    public float Fade;
    public float Increment;
    public bool Fading;

    public readonly int Longest => Math.Max(Tap, Math.Max(Next, Target));

    public readonly bool Settled => !Fading && Tap == Target;

    // At control rate.
    public void Begin()
    {
        if (Fading || Tap == Target)
            return;
        Next = Target;
        Fade = 0f;
        Fading = true;
    }

    public void Snap()
    {
        Tap = Target;
        Next = Target;
        Fade = 0f;
        Fading = false;
    }

    // Once per sample while fading, after both taps have been read and blended by Fade.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance()
    {
        float t = Fade + Increment;
        if (t >= 1f)
        {
            Tap = Next;
            Fading = false;
            t = 0f;
        }
        Fade = t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(int write, int tap, int size)
    {
        int read = write - tap;
        return read < 0 ? read + size : read;
    }
}
