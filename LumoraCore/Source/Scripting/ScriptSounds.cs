// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Assets;
using Lumora.Core.Audio;
using Lumora.Core.Components.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// A clip played once at a point: a short-lived slot with a player, an output and a OneShotSound that
// takes it away when the clip is done. A clip whose load has failed faults the script with the reason.
// Shared, it is world content everyone hears, made as the world whoever the author is, since nothing in
// it is the author's to choose but the clip, where it plays and how it sounds; a user's script needs the
// Spawn permission for it. Local, it is a local slot under the script's own object, heard on that
// machine only.
//
// It sounds as the node was given it, the output clamping what it must: a blend of zero is heard the
// same everywhere, and nothing caps how far it carries. -xlinka
internal static class ScriptSounds
{
    // How a sound plays, as the node was given it. Speed is a multiple of the clip's own rate, pitch moving
    // with it. PlayOnce checks every number is a number. A MaxScale of 0 holds the scale to no upper limit.
    public readonly record struct Sound(float Volume, float Speed, float SpatialBlend, float Doppler, float MinDistance,
        float MaxDistance, int Priority, AudioRolloffCurve Rolloff = AudioRolloffCurve.LogarithmicFadeOff,
        AudioTypeGroup Group = AudioTypeGroup.SoundEffect, AudioDistanceSpace Space = AudioDistanceSpace.Local,
        float MinScale = 0f, float MaxScale = 0f, bool IgnoreEffects = false, bool Global = false);

    // With a parent the sound follows it, point in the parent's space: for a Shared script a parent of world
    // content outside anyone's body, for a Local one its own object or what it made there.
    public static Slot? PlayOnce(ScriptContext ctx, IAssetProvider<AudioClip>? clip, float3 point, in Sound sound, Slot? parent = null)
    {
        if (clip is not Component provider || !ScriptReads.Visible(ctx, provider))
            return null;
        // A clip already known not to load says why, rather than playing silence.
        if (clip is IUrlAssetProvider { LoadFailure: { } why })
            throw new InvalidOperationException("the clip does not load: " + why);
        ScriptWrites.Finite(point, "point");
        Check(sound);
        if (parent != null && !ScriptReads.Visible(ctx, parent))
            return null;

        if (ctx.IsLocal)
        {
            var home = ctx.Self ?? throw new InvalidOperationException("a Local script plays sounds under its own object");
            if (parent != null && !ReferenceEquals(parent, home) && !parent.IsLocalElement)
                throw new UnauthorizedAccessException("a Local script plays sounds under its own object");
            ctx.ReserveMade(1);
            var local = (parent ?? home).AddLocalSlot("One Shot");
            Build(local, clip, point, sound, parent != null);
            ctx.Made(local, 1);
            return local;
        }

        var world = ctx.World;
        if (!ctx.Principal.IsWorldContent
            && (!world.AllowsItemSpawning || world.DataModelPermissions?.AllowsDomain(ctx.Principal.Author, DataModelPermissionDomain.Spawn) != true))
            throw new UnauthorizedAccessException("its author may not bring things into this world");
        if (parent != null && (parent.IsLocalElement || parent.ActiveUserRoot != null
            || (!parent.IsRootSlot && parent.ReferenceID.GetUserByte() != RefIDConstants.AUTHORITY_BYTE)))
            throw new UnauthorizedAccessException("a sound plays under world content outside anyone's body");
        ctx.ReserveMade(1);
        Slot slot;
        using (world.DataModelPermissions?.EnterActor(ScriptPrincipal.WorldContent))
        {
            slot = (parent ?? world.RootSlot).AddSlot("One Shot");
            Build(slot, clip, point, sound, parent != null);
        }
        ctx.Made(slot, 1);
        return slot;
    }

    private static void Check(in Sound sound)
    {
        Finite(sound.Volume, "volume");
        Finite(sound.Speed, "speed");
        Finite(sound.SpatialBlend, "spatial blend");
        Finite(sound.Doppler, "doppler");
        Finite(sound.MinDistance, "min distance");
        Finite(sound.MaxDistance, "max distance");
        Finite(sound.MinScale, "min scale");
        Finite(sound.MaxScale, "max scale");
    }

    private static void Finite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentException($"{name} is not a finite number");
    }

    private static void Build(Slot slot, IAssetProvider<AudioClip> clip, float3 point, in Sound sound, bool parented = false)
    {
        slot.Persistent.Value = false;
        if (parented)
            slot.LocalPosition.Value = point;
        else
            slot.GlobalPosition = point;
        var player = slot.AttachComponent<AudioClipPlayer>();
        player.Clip.Target = clip;
        player.Speed.Value = sound.Speed;
        player.Playback.Speed = sound.Speed;
        var output = slot.AttachComponent<AudioOutput>();
        output.Source.Target = player;
        output.Volume.Value = sound.Volume;
        output.AudioTypeGroup.Value = sound.Group;
        output.Spatialize.Value = sound.SpatialBlend > 0f;
        output.SpatialBlend.Value = sound.SpatialBlend;
        output.MinDistance.Value = sound.MinDistance;
        output.MaxDistance.Value = sound.MaxDistance;
        output.RolloffMode.Value = sound.Rolloff;
        output.DopplerLevel.Value = sound.Doppler;
        output.Priority.Value = sound.Priority;
        output.DistanceSpace.Value = sound.Space;
        output.MinScale.Value = System.Math.Max(sound.MinScale, 0f);
        output.MaxScale.Value = sound.MaxScale > 0f ? sound.MaxScale : float.PositiveInfinity;
        output.IgnoreAudioEffects.Value = sound.IgnoreEffects;
        output.Global.Value = sound.Global;
        slot.AttachComponent<OneShotSound>();
        player.Play();
    }
}
