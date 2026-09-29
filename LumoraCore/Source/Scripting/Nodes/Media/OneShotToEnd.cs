// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Audio;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Plays a clip once at a point the way Play One Shot does and carries on through next at once. ended runs
// once the clip has played through; failed when it does not load, or at once when the script cannot see the
// clip, with error saying why. Watched on the machine the script runs on.
[Node("Media/Play One Shot To End", Capability.Audio)]
[Answers("ended", "failed")]
public sealed class OneShotToEnd : AwaitNode<SoundEnded>
{
    public static void Start(ScriptContext ctx, AwaitTicket<SoundEnded> ticket, IAssetProvider<AudioClip>? clip, float3 point,
        float volume = 1f, float speed = 1f, float spatialBlend = 1f, float doppler = 0f, float minDistance = 1f,
        float maxDistance = 25f, int priority = 128)
    {
        Slot? sound;
        try
        {
            sound = ScriptSounds.PlayOnce(ctx, clip, point,
                new ScriptSounds.Sound(volume, speed, spatialBlend, doppler, minDistance, maxDistance, priority));
        }
        catch (System.InvalidOperationException ex)
        {
            ticket.Answer("failed", new SoundEnded(ex.Message));
            return;
        }
        catch
        {
            ticket.Abandon();
            throw;
        }
        if (sound == null)
        {
            ticket.Answer("failed", new SoundEnded("the script cannot see that clip"));
            return;
        }

        var player = sound.GetComponent<AudioClipPlayer>();
        string? error = null;
        ScriptWaits.Watch(ctx, ticket, () =>
        {
            if (player == null || player.IsDestroyed || sound.IsDestroyed)
                return error != null ? ("failed", new SoundEnded(error)) : ("ended", new SoundEnded(string.Empty));
            if (player.LoadError is { } why)
                error = "the clip does not load: " + why;
            if (player.IsFinished)
                return ("ended", new SoundEnded(string.Empty));
            return null;
        });
    }
}
