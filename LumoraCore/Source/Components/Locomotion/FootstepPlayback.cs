// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Assets;
using Lumora.Core.Audio;
using Lumora.Core.Components.Audio;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Footsteps, played on each machine for the feet it poses.
//
// Every machine runs every avatar's gait (AvatarIK), so every machine already knows the instant anyone's
// foot comes down, and each one plays what it hears itself. Nothing crosses the wire per step; a sound
// made in shared world space instead would cost a slot built and torn down for everyone at every step of
// every walker in the room.
//
// The cost follows steps, never frames and never surfaces. A world with no FootstepSoundSet ignores steps
// outright. Otherwise a step farther from this machine's ears than the loudest set carries is dropped, and
// a near one casts one short ray down, looks for a FootstepSurface on the collider it hit, and plays from
// a few local voices that switch themselves off between steps. -xlinka
internal static class FootstepPlayback
{
    private const int MaxVoices = 6;
    private const float ProbeUp = 0.25f;
    private const float ProbeLength = 0.6f;
    private const float ParkMargin = 0.25f;
    private const float UnknownLength = 2f;
    private const int VoicePriority = 64;

    private sealed class Voice
    {
        public Slot Slot = null!;
        public AudioClipPlayer Player = null!;
        public AudioOutput Output = null!;
        public Action Park = null!;
    }

    private sealed class WorldSteps
    {
        public readonly World World;
        public readonly List<FootstepSoundSet> Sets = new();
        public readonly List<Voice> Voices = new();
        public readonly List<Slot> Exclude = new(1);
        public readonly Random Random = new();
        public Slot? Holder;
        public bool Building;
        public float Range;
        public bool RangeDirty = true;
        public int Next;

        public WorldSteps(World world) => World = world;
    }

    private static readonly ConditionalWeakTable<World, WorldSteps> PerWorld = new();
    private static readonly object Gate = new();
    private static bool _listening;

    public static void SetAdded(FootstepSoundSet set)
    {
        var world = set.World;
        if (world == null)
            return;
        var state = PerWorld.GetValue(world, static w => new WorldSteps(w));
        if (!state.Sets.Contains(set))
            state.Sets.Add(set);
        state.RangeDirty = true;
        Listen();
        EnsureVoices(state);
    }

    public static void SetChanged(FootstepSoundSet set)
    {
        if (set.World is { } world && PerWorld.TryGetValue(world, out var state))
            state.RangeDirty = true;
    }

    public static void SetRemoved(FootstepSoundSet set)
    {
        if (set.World is not { } world || !PerWorld.TryGetValue(world, out var state))
            return;
        state.Sets.Remove(set);
        state.RangeDirty = true;
    }

    private static void Listen()
    {
        lock (Gate)
        {
            if (_listening)
                return;
            _listening = true;
            AvatarIK.Footstep += Heard;
        }
    }

    private static void Heard(AvatarIK ik, AvatarFootstep step)
    {
        var world = ik.World;
        if (world == null || world.IsDisposed || !PerWorld.TryGetValue(world, out var state) || state.Sets.Count == 0)
            return;
        if (world.Focus == World.WorldFocus.Background)
            return;
        if (ik.UserRoot.Target is not { IsDestroyed: false } root || root.Slot == null)
            return;

        float range = AudibleRange(state);
        var audio = world.Audio;
        if (range <= 0f || (audio != null && audio.HasPrimaryEar
            && (step.Position - audio.LastPrimaryEarPosition).LengthSquared > range * range))
            return;

        float scale = MathF.Max(root.GlobalScale, 0.01f);
        state.Exclude.Clear();
        state.Exclude.Add(root.Slot);
        var origin = step.Position + float3.Up * (ProbeUp * scale);
        var down = float3.Down;
        bool struck = world.Physics.Raycast(in origin, in down, ProbeLength * scale, state.Exclude, out var hit);
        state.Exclude.Clear();
        if (!struck || hit.Slot is not { IsDestroyed: false } ground)
            return;

        var surface = ground.GetComponent<FootstepSurface>();
        if (surface == null || !surface.Enabled.Value || surface.CurrentSet is not { } set)
            return;
        var clip = set.PickClip(step.Landing, state.Random);
        if (clip != null)
            Play(state, set, clip, hit.Point);
    }

    private static float AudibleRange(WorldSteps state)
    {
        if (!state.RangeDirty)
            return state.Range;
        float range = 0f;
        for (int i = state.Sets.Count - 1; i >= 0; i--)
        {
            var set = state.Sets[i];
            if (set.IsDestroyed)
            {
                state.Sets.RemoveAt(i);
                continue;
            }
            range = MathF.Max(range, set.AudibleRange);
        }
        state.Range = range;
        state.RangeDirty = false;
        return range;
    }

    private static void Play(WorldSteps state, FootstepSoundSet set, IAssetProvider<AudioClip> clip, float3 point)
    {
        if (state.Holder == null || state.Holder.IsDestroyed)
        {
            EnsureVoices(state);
            return;
        }
        var voice = NextVoice(state);
        if (voice == null)
            return;

        float volume = set.Volume.Value * (1f + Spread(state.Random, set.VolumeVariation.Value));
        float pitch = set.Pitch.Value * (1f + Spread(state.Random, set.PitchVariation.Value));
        if (!float.IsFinite(volume) || !float.IsFinite(pitch))
            return;
        volume = System.Math.Clamp(volume, 0f, 1f);
        pitch = System.Math.Clamp(pitch, 0.05f, 4f);

        voice.Slot.GlobalPosition = point;
        voice.Output.Volume.Value = volume;
        voice.Output.MinDistance.Value = MathF.Max(set.MinDistance.Value, 0f);
        voice.Output.MaxDistance.Value = MathF.Max(set.MaxDistance.Value, 0.01f);
        voice.Output.RolloffMode.Value = set.Rolloff.Value;
        voice.Player.Clip.Target = clip;
        voice.Player.Speed.Value = pitch;
        voice.Player.Playback.Speed = pitch;
        voice.Slot.ActiveSelf.Value = true;
        voice.Player.Play();

        double length = clip.Asset is { IsReady: true } loaded ? loaded.Duration : UnknownLength;
        state.World.RunInSeconds((float)(length / pitch) + ParkMargin, voice.Park);
    }

    private static float Spread(Random random, float variation)
    {
        if (!(variation > 0f))
            return 0f;
        return ((float)random.NextDouble() * 2f - 1f) * variation;
    }

    // A quiet voice if there is one, else the one that has played longest.
    private static Voice? NextVoice(WorldSteps state)
    {
        int count = state.Voices.Count;
        if (count == 0)
            return null;
        for (int i = 0; i < count; i++)
        {
            int index = (state.Next + i) % count;
            var voice = state.Voices[index];
            if (voice.Slot.IsDestroyed)
                continue;
            if (!voice.Slot.ActiveSelf.Value || !voice.Player.IsPlaying)
            {
                state.Next = (index + 1) % count;
                return voice;
            }
        }
        var oldest = state.Voices[state.Next % count];
        state.Next = (state.Next + 1) % count;
        return oldest.Slot.IsDestroyed ? null : oldest;
    }

    // Built on the world's own update, never inside the pose pass that heard the step.
    private static void EnsureVoices(WorldSteps state)
    {
        if (state.Building || (state.Holder != null && !state.Holder.IsDestroyed))
            return;
        state.Building = true;
        state.World.RunSynchronously(() => BuildVoices(state));
    }

    private static void BuildVoices(WorldSteps state)
    {
        state.Building = false;
        var world = state.World;
        if (world.IsDisposed || world.RootSlot == null || (state.Holder != null && !state.Holder.IsDestroyed))
            return;
        state.Voices.Clear();
        state.Next = 0;
        var holder = world.RootSlot.AddLocalSlot("Footsteps");
        holder.Persistent.Value = false;
        for (int i = 0; i < MaxVoices; i++)
        {
            var slot = holder.AddLocalSlot("Voice");
            slot.Persistent.Value = false;
            slot.ActiveSelf.Value = false;
            var player = slot.AttachComponent<AudioClipPlayer>();
            var output = slot.AttachComponent<AudioOutput>();
            output.Source.Target = player;
            output.AudioTypeGroup.Value = AudioTypeGroup.SoundEffect;
            output.Spatialize.Value = true;
            output.SpatialBlend.Value = 1f;
            output.DopplerLevel.Value = 0f;
            output.Priority.Value = VoicePriority;
            var voice = new Voice { Slot = slot, Player = player, Output = output };
            voice.Park = () => Park(voice);
            state.Voices.Add(voice);
        }
        state.Holder = holder;
    }

    private static void Park(Voice voice)
    {
        if (voice.Slot.IsDestroyed || voice.Player.IsDestroyed)
            return;
        if (!voice.Player.IsPlaying)
            voice.Slot.ActiveSelf.Value = false;
    }
}
