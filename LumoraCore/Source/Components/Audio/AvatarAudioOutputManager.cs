// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Audio;
using Lumora.Core.Audio;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Audio;

// The thing that actually decides how a voice sounds this frame.
//
// Everything it writes goes through FieldDrives, and every one of them is local-value-only: the inputs
// (the speaker's mode, the avatar's configs, my own settings) already replicate, so each peer computes
// the same answer for the parts that are shared and its OWN answer for the parts that are not. A
// whisper heard from three metres away is not a fact about the world, it is a fact about where I am
// standing, and broadcasting my answer for it would fight everybody else's. -xlinka
[ComponentCategory("Audio")]
public class AvatarAudioOutputManager : Component
{
    public readonly SyncRef<Lumora.Core.Components.Audio.AudioOutput> AudioOutput;

    // A view voice is the copy a camera or a mirror hears. A whisper never reaches one: leaning in to
    // somebody is not a thing a recording is entitled to pick up.
    public readonly Sync<bool> IsViewVoice;

    public readonly VoiceModeConfig Whisper = new();
    public readonly VoiceModeConfig Normal = new();
    public readonly VoiceModeConfig Shout = new();
    public readonly VoiceModeConfig Broadcast = new();

    public readonly FieldDrive<bool> EnabledDrive;
    public readonly FieldDrive<float> VolumeDrive;
    public readonly FieldDrive<float> DopplerDrive;
    public readonly FieldDrive<bool> IgnoreAudioEffectsDrive;
    public readonly FieldDrive<bool> SpatializeDrive;
    public readonly FieldDrive<float> SpatialBlendDrive;
    public readonly FieldDrive<float> MinDistanceDrive;
    public readonly FieldDrive<float> MaxDistanceDrive;
    public readonly FieldDrive<AudioRolloffCurve> RolloffDrive;
    public readonly FieldDrive<AudioDistanceSpace> DistanceSpaceDrive;
    public readonly FieldDrive<float> MinScaleDrive;
    public readonly FieldDrive<float> MaxScaleDrive;

    // The local user's own emitter is the one the voice limiter must never drop, because it is the one
    // feeding their own monitoring and their own exclusions. The limiter keeps the highest, so this is the top.
    private const int LocalVoicePriority = int.MaxValue;

    private static readonly List<AudioListener> _listenerScratch = new();

    private bool _seeded;
    private bool _boundDrives;

    public AvatarAudioOutputManager()
    {
        AudioOutput = new SyncRef<Lumora.Core.Components.Audio.AudioOutput>(this);
        IsViewVoice = new Sync<bool>(this, false);

        EnabledDrive = new FieldDrive<bool>(this) { LocalValueOnly = true };
        VolumeDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        DopplerDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        IgnoreAudioEffectsDrive = new FieldDrive<bool>(this) { LocalValueOnly = true };
        SpatializeDrive = new FieldDrive<bool>(this) { LocalValueOnly = true };
        SpatialBlendDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        MinDistanceDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        MaxDistanceDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        RolloffDrive = new FieldDrive<AudioRolloffCurve>(this) { LocalValueOnly = true };
        DistanceSpaceDrive = new FieldDrive<AudioDistanceSpace>(this) { LocalValueOnly = true };
        MinScaleDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
        MaxScaleDrive = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    // The presets are seeded on the machine that MADE the component: everyone else gets the values with it.
    // Seeding in OnAwake ran on every machine that decoded one, so each joiner wrote the presets over the
    // owner's voice settings, refused, and would have wiped any retuning had it been let through. -xlinka
    public override void OnInit()
    {
        base.OnInit();
        if (IsMintedHere)
            SeedPresets();
    }

    // A save written before a mode existed leaves it at the plain field defaults; that mode gets its preset.
    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);
        if (!Whisper.ValueCameFromData)
            Whisper.ApplyPreset(VoiceMode.Whisper);
        if (!Normal.ValueCameFromData)
            Normal.ApplyPreset(VoiceMode.Normal);
        if (!Shout.ValueCameFromData)
            Shout.ApplyPreset(VoiceMode.Shout);
        if (!Broadcast.ValueCameFromData)
            Broadcast.ApplyPreset(VoiceMode.Broadcast);
    }

    private void SeedPresets()
    {
        if (_seeded)
            return;
        _seeded = true;
        Whisper.ApplyPreset(VoiceMode.Whisper);
        Normal.ApplyPreset(VoiceMode.Normal);
        Shout.ApplyPreset(VoiceMode.Shout);
        Broadcast.ApplyPreset(VoiceMode.Broadcast);
    }

    public VoiceModeConfig GetOwnConfig(VoiceMode mode) => mode switch
    {
        VoiceMode.Whisper => Whisper,
        VoiceMode.Shout => Shout,
        VoiceMode.Broadcast => Broadcast,
        _ => Normal,
    };

    // Points every drive at the matching field on the output. Called by whoever built the pair (the user
    // root does it on the authority); the refs replicate from there and each peer grants its own links.
    public void BindTo(Lumora.Core.Components.Audio.AudioOutput output)
    {
        if (output == null || output.IsDestroyed)
            return;

        AudioOutput.Target = output;
        BindDrives(output);
    }

    private void BindDrives(Lumora.Core.Components.Audio.AudioOutput output)
    {
        EnabledDrive.DriveTarget(output.Enabled);
        VolumeDrive.DriveTarget(output.Volume);
        DopplerDrive.DriveTarget(output.DopplerLevel);
        IgnoreAudioEffectsDrive.DriveTarget(output.IgnoreAudioEffects);
        SpatializeDrive.DriveTarget(output.Spatialize);
        SpatialBlendDrive.DriveTarget(output.SpatialBlend);
        MinDistanceDrive.DriveTarget(output.MinDistance);
        MaxDistanceDrive.DriveTarget(output.MaxDistance);
        RolloffDrive.DriveTarget(output.RolloffMode);
        DistanceSpaceDrive.DriveTarget(output.DistanceSpace);
        MinScaleDrive.DriveTarget(output.MinScale);
        MaxScaleDrive.DriveTarget(output.MaxScale);
        _boundDrives = true;
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        var output = AudioOutput.Target;
        if (output == null || output.IsDestroyed)
            return;

        // A peer that RECEIVED this component gets the target ref over the wire but not the drive
        // targets, because a drive's target is its own member and the pairing is deterministic. Rebind
        // once rather than trusting the arrival order of twelve refs.
        if (!_boundDrives)
            BindDrives(output);

        var user = Slot?.ActiveUserRoot?.ActiveUser;
        if (user == null)
        {
            Silence();
            return;
        }

        var localUser = World?.LocalUser;
        bool isLocal = localUser != null && ReferenceEquals(user, localUser);

        if (isLocal)
            ApplyLocalOutputRules(output, localUser!);

        var mode = user.ActiveVoiceMode;
        if (IsViewVoice.Value && mode == VoiceMode.Whisper)
            mode = VoiceMode.Mute;

        if (mode == VoiceMode.Mute)
        {
            Silence();
            return;
        }

        float distance = user.DistanceToLocalUserHead;

        // A remote whisperer past the whisper range is not quiet, they are inaudible. Dropping the
        // emitter rather than driving its volume to zero also takes the voice off the mixer entirely.
        if (mode == VoiceMode.Whisper && !isLocal && distance > Whisper.MaxDistance.Value)
        {
            Silence();
            return;
        }

        var config = user.Root?.GetRegisteredComponent<AvatarAudioConfiguration>()?.TryGetConfig(mode)
                     ?? GetOwnConfig(mode);

        float attenuation = isLocal ? 1f : WhisperAttenuationFor(localUser, distance);
        float compensation = isLocal ? ScaleCompensation(output, user) : 1f;

        EnabledDrive.SetValue(true);
        VolumeDrive.SetValue(Sanitize(config.Volume.Value) * attenuation);
        DistanceSpaceDrive.SetValue(config.DistanceSpace.Value);
        MinDistanceDrive.SetValue(Scaled(config.MinDistance.Value, compensation));
        MaxDistanceDrive.SetValue(Scaled(config.MaxDistance.Value, compensation));
        RolloffDrive.SetValue(config.RollOffMode.Value);
        MinScaleDrive.SetValue(Unscaled(config.MinScale.Value, compensation));
        MaxScaleDrive.SetValue(Unscaled(config.MaxScale.Value, compensation));
        SpatializeDrive.SetValue(config.Spatialize.Value);
        SpatialBlendDrive.SetValue(Clamp01(config.SpatialBlend.Value));
        DopplerDrive.SetValue(Sanitize(config.Doppler.Value));
        IgnoreAudioEffectsDrive.SetValue(config.IgnoreAudioEffects.Value);
    }

    private void Silence()
    {
        EnabledDrive.SetValue(false);
        VolumeDrive.SetValue(0f);
    }

    // The other half of the whisper rule: while I am whispering, somebody far enough away from me is
    // turned down, fading in to full by the time they are close enough to be the person I am whispering
    // to. Nobody else's machine computes this, which is exactly why the drives are local-value-only.
    private float WhisperAttenuationFor(User? localUser, float distance)
    {
        if (localUser == null || localUser.ActiveVoiceMode != VoiceMode.Whisper)
            return 1f;

        var system = Engine.Current?.AudioSystem;
        float attenuation = system?.WhisperAttenuationVolume ?? 1f;

        float min = Whisper.MinDistance.Value;
        float max = Whisper.MaxDistance.Value;

        if (float.IsNaN(distance) || distance >= max)
            return attenuation;
        if (distance <= min || max <= min)
            return 1f;

        float t = (distance - min) / (max - min);
        return 1f + (attenuation - 1f) * t;
    }

    // Nobody hears themselves, and the voice limiter must never get the chance to drop the one emitter
    // that carries that exclusion. Both are local writes: the emitter is a replicated, host-allocated
    // element, so posting either would hand every peer an answer that is only true on this machine (and
    // would be a foreign write besides).
    private void ApplyLocalOutputRules(Lumora.Core.Components.Audio.AudioOutput output, User localUser)
    {
        if (output.Priority.Value != LocalVoicePriority)
            output.Priority.SetValueSilently(LocalVoicePriority, change: true);

        var manager = World?.Audio;
        if (manager == null)
            return;

        _listenerScratch.Clear();
        manager.CollectLocalUserListeners(_listenerScratch);
        for (int i = 0; i < _listenerScratch.Count; i++)
            output.SetLocallyExcluded(_listenerScratch[i], true);
        _listenerScratch.Clear();
    }

    // The emitter sits on a head that may be scaled relative to the user root (a shrunk or grown avatar).
    // The distances the config authored are about the ROOM, so they are divided back out of that scale,
    // and the scale clamps go the other way for the same reason.
    private static float ScaleCompensation(Lumora.Core.Components.Audio.AudioOutput output, User user)
    {
        var outputSlot = output.Slot;
        var rootSlot = user.Root?.Slot;
        if (outputSlot == null || rootSlot == null || ReferenceEquals(outputSlot, rootSlot))
            return 1f;

        var scale = outputSlot.LocalScaleToSpace(float3.One, rootSlot);
        float average = (scale.x + scale.y + scale.z) / 3f;
        if (float.IsNaN(average) || float.IsInfinity(average) || average <= 0.0001f)
            return 1f;
        return 1f / average;
    }

    private static float Scaled(float value, float compensation)
    {
        if (float.IsPositiveInfinity(value))
            return float.PositiveInfinity;
        return Sanitize(value) * compensation;
    }

    private static float Unscaled(float value, float compensation)
    {
        if (float.IsPositiveInfinity(value))
            return float.PositiveInfinity;
        if (compensation <= 0.0001f)
            return Sanitize(value);
        return Sanitize(value) / compensation;
    }

    private static float Sanitize(float value) => float.IsNaN(value) || value < 0f ? 0f : value;

    private static float Clamp01(float value)
        => float.IsNaN(value) ? 0f : (value < 0f ? 0f : (value > 1f ? 1f : value));
}
