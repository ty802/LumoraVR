// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Assets;

namespace Lumora.Core.Components;

// What a kind of ground sounds like underfoot: wood, carpet, metal grating. Surfaces name one through
// FootstepSurface, often through a variable so a whole world's floors can be re-voiced from one place.
//
// A step plays one of Steps at random, never the same one twice running when there is a choice. Coming
// down after a jump or a fall plays one of Landings, or a step when there are none. Volume and Pitch are
// nudged by up to their variation each time so a corridor does not sound like a loop. Heard from where
// the foot came down, full volume inside MinDistance and gone at MaxDistance. -xlinka
[ComponentCategory("Users/Locomotion")]
public sealed class FootstepSoundSet : Component
{
    public readonly SyncAssetList<AudioClip> Steps;
    public readonly SyncAssetList<AudioClip> Landings;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Volume;
    [Range(0f, 0.5f, "0.00")]
    public readonly Sync<float> VolumeVariation;
    [Range(0.25f, 4f, "0.00")]
    public readonly Sync<float> Pitch;
    [Range(0f, 0.5f, "0.00")]
    public readonly Sync<float> PitchVariation;

    public readonly Sync<float> MinDistance;
    public readonly Sync<float> MaxDistance;
    public readonly Sync<AudioRolloffCurve> Rolloff;

    private int _lastStep = -1;
    private int _lastLanding = -1;

    public FootstepSoundSet()
    {
        Steps = new SyncAssetList<AudioClip>(this);
        Landings = new SyncAssetList<AudioClip>(this);
        Volume = new Sync<float>(this, 0.6f);
        VolumeVariation = new Sync<float>(this, 0.1f);
        Pitch = new Sync<float>(this, 1f);
        PitchVariation = new Sync<float>(this, 0.05f);
        MinDistance = new Sync<float>(this, 1f);
        MaxDistance = new Sync<float>(this, 12f);
        Rolloff = new Sync<AudioRolloffCurve>(this, AudioRolloffCurve.LogarithmicFadeOff);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        FootstepPlayback.SetAdded(this);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        FootstepPlayback.SetChanged(this);
    }

    public override void OnDestroy()
    {
        FootstepPlayback.SetRemoved(this);
        base.OnDestroy();
    }

    // How far a step in this set can be heard, for skipping feet nobody here could hear.
    internal float AudibleRange
    {
        get
        {
            float max = MaxDistance.Value;
            return float.IsFinite(max) && max > 0f ? max : 0f;
        }
    }

    internal IAssetProvider<AudioClip>? PickClip(bool landing, Random random)
    {
        if (landing && Pick(Landings, random, ref _lastLanding) is { } landed)
            return landed;
        return Pick(Steps, random, ref _lastStep);
    }

    private static IAssetProvider<AudioClip>? Pick(SyncAssetList<AudioClip> clips, Random random, ref int last)
    {
        int count = clips.Count;
        if (count == 0)
            return null;
        int start = random.Next(count);
        if (count > 1 && start == last)
            start = (start + 1 + random.Next(count - 1)) % count;
        for (int i = 0; i < count; i++)
        {
            int index = (start + i) % count;
            if (clips.GetElement(index).Target is { IsDestroyed: false } clip)
            {
                last = index;
                return clip;
            }
        }
        return null;
    }
}
