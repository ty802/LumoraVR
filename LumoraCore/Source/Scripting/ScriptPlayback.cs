// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components;
using Lumora.Core.Components.Audio;
using Lumora.Core.Components.Media;
using Lumora.Core.Media;

namespace Lumora.Core.Scripting;

// The rest of what the media nodes change on a player. A sound keeps its loop and speed as fields of its own
// that its transport follows, so those fields are what the gate is asked about and what is written there;
// every other player keeps both on the transport itself. Positions a player cannot hold are settled here:
// a player that does not loop is kept between its start and its end, where a head parked past either end
// would sit there silent for the overshoot. -xlinka
internal static class ScriptPlayback
{
    public static void SetLoop(ScriptContext ctx, IPlayable playable, Slot? slot, bool loop)
    {
        if (ClipOf(playable) is { } clip)
        {
            if (clip.Loop.Value != loop)
                ScriptWrites.Write(ctx, clip.Loop, loop);
            else
                ScriptWrites.Check(ctx, clip.Loop);
            return;
        }
        ScriptMedia.Control(ctx, slot, player =>
        {
            if (player.Loop != loop)
                player.Loop = loop;
        });
    }

    public static void SetSpeed(ScriptContext ctx, IPlayable playable, Slot? slot, float speed)
    {
        if (!float.IsFinite(speed))
            throw new ArgumentException("speed is not a finite number");
        if (ClipOf(playable) is { } clip)
        {
            if (clip.Speed.Value != speed)
                ScriptWrites.Write(ctx, clip.Speed, speed);
            else
                ScriptWrites.Check(ctx, clip.Speed);
            return;
        }
        ScriptMedia.Control(ctx, slot, player =>
        {
            if (player.Speed != speed)
                player.Speed = speed;
        });
    }

    // The clip's length in seconds, or -1 while it is not known, a live stream included.
    public static float Length(IPlayable playable)
    {
        double length = playable.ClipLength;
        return double.IsFinite(length) && length > 0.0 ? (float)length : -1f;
    }

    public static float Settle(IPlayable playable, float position)
    {
        if (playable.Loop)
            return position;
        float length = Length(playable);
        if (position < 0f)
            return 0f;
        return length > 0f && position > length ? length : position;
    }

    private static AudioClipPlayer? ClipOf(IPlayable playable) => playable switch
    {
        AudioClipPlayer clip => clip,
        AudioPlayer player => player.Transport,
        _ => null,
    };
}
