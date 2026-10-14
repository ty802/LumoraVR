// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Audio;
using Lumora.Core.Components.Media;
using Lumora.Core.Media;

namespace Lumora.Core.Scripting;

// What the media nodes work: the first player on a slot, a video, a sound or an animation, and the
// synced transport behind it, which is what the gate is asked about before anything moves. -xlinka
internal static class ScriptMedia
{
    public static IPlayable? Find(ScriptContext ctx, Slot? slot)
    {
        if (!ScriptReads.Reached(ctx, slot))
            return null;
        foreach (var component in slot!.Components)
        {
            if (component is IPlayable playable && !component.IsDestroyed)
                return playable;
        }
        ctx.Fail($"nothing on {ScriptReads.Named(slot)} plays");
        return null;
    }

    public static void Control(ScriptContext ctx, Slot? slot, Action<IPlayable> act)
    {
        if (Find(ctx, slot) is not { } playable)
            return;
        if (TransportOf(playable) is not { } transport)
        {
            ctx.Fail($"the player on {ScriptReads.Named(slot)} cannot be controlled");
            return;
        }
        ScriptWrites.Check(ctx, transport);
        act(playable);
    }

    private static SyncPlayback? TransportOf(IPlayable playable) => playable switch
    {
        Animator animator => animator.Playback,
        VideoTextureProvider video => video.Playback,
        AudioClipPlayer clip => clip.Playback,
        VideoPlayer player => player.Provider?.Playback,
        AudioPlayer player => player.Transport?.Playback,
        _ => null,
    };
}
