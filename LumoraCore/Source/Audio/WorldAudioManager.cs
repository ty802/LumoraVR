// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Audio;
using Lumora.Core.Components.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Audio;

// One world's ears and sounds. The world side of the mixer lives here: components register, this
// hands them voices in the world's own AudioSpace, and once a frame it pushes the transforms and
// volumes the mixer reads on its own thread.
//
// A world nobody is in has its space deactivated rather than its voices torn down, so walking back
// into a room does not restart every sound in it. -xlinka
public sealed class WorldAudioManager : IDisposable
{
    // Metres per second past which a position delta is a teleport rather than motion. Doppler on a
    // teleport is a siren going off in somebody's ear.
    private const float MaxVelocity = 50f;

    private readonly World _world;
    private readonly List<AudioListener> _listeners = new();
    private readonly List<AudioOutput> _outputs = new();
    private readonly List<AudioOutput> _outputsToAddOrRemove = new();
    private readonly List<AudioOutput> _outputsToUpdate = new();
    private readonly List<AudioListenerVoice> _excludedScratch = new();
    private readonly List<VoicePermission> _voicePermissions = new();
    private readonly List<User> _userScratch = new();
    private readonly List<ReverbZone> _zones = new();
    private readonly List<ZoneWeight> _zoneWeights = new();
    private readonly List<IAudioDspFilter> _chain = new();
    private readonly List<IAudioDspFilter> _dropped = new();
    private readonly HashSet<IAudioDspFilter> _claimed = new(ReferenceEqualityComparer.Instance);

    // Deeper than this a blend of a blend of a blend is refused rather than walked.
    private const int MaxEffectNesting = 8;

    // The local microphone setting as of the last frame this world saw it. A CHANGE is what pushes into
    // User.IsMuted; the raw value is not, because IsMuted also carries the host's mute and pushing the
    // setting every frame would lift a host mute the instant it landed. -xlinka
    private bool _lastMicrophoneSetting;
    private bool _hasMicrophoneSetting;

    private AudioSpace? _space;
    private bool _disposed;

    // A listener appearing or going away invalidates every output's exclusion list, because those hold
    // listener voices rather than components.
    private bool _listenersChanged;

    public WorldAudioManager(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public AudioSpace? Space => _space;

    // Where this peer's ears were on the last update. Anything that wants "how far is this from the
    // user" without walking the user root can read it.
    public float3 LastPrimaryEarPosition { get; private set; }

    public floatQ LastPrimaryEarRotation { get; private set; } = floatQ.Identity;

    // Whether that position is a real ear this frame or just the last one we saw. A world with nobody
    // listening would otherwise measure every distance from the origin.
    public bool HasPrimaryEar { get; private set; }

    // REGISTRATION

    internal void RegisterListener(AudioListener listener)
    {
        if (_disposed || listener == null || listener.InManager)
            return;
        listener.InManager = true;
        _listeners.Add(listener);
        _listenersChanged = true;
    }

    internal void UnregisterListener(AudioListener listener)
    {
        if (listener == null || !listener.InManager)
            return;
        listener.InManager = false;
        _listeners.Remove(listener);
        DropListenerVoice(listener);
        _listenersChanged = true;
    }

    internal void RegisterOutput(AudioOutput output)
    {
        if (_disposed || output == null || output.InManager)
            return;
        output.InManager = true;
        _outputs.Add(output);
        QueueAddOrRemove(output);
        QueueUpdate(output);
    }

    internal void UnregisterOutput(AudioOutput output)
    {
        if (output == null || !output.InManager)
            return;
        output.InManager = false;
        _outputs.Remove(output);
        DropOutputVoice(output);
    }

    internal void RegisterZone(ReverbZone zone)
    {
        if (_disposed || zone == null || zone.InManager)
            return;
        zone.InManager = true;
        _zones.Add(zone);
    }

    // The zone's reverbs are not dropped here: each listener carrying one keeps it until its tail is out.
    internal void UnregisterZone(ReverbZone zone)
    {
        if (zone == null || !zone.InManager)
            return;
        zone.InManager = false;
        _zones.Remove(zone);
    }

    internal void RegisterVoicePermission(VoicePermission permission)
    {
        if (_disposed || permission == null || _voicePermissions.Contains(permission))
            return;
        _voicePermissions.Add(permission);
    }

    internal void UnregisterVoicePermission(VoicePermission permission)
    {
        if (permission == null)
            return;
        _voicePermissions.Remove(permission);
    }

    // Recomputed in the same pass as the caps, so a reader never has to walk the permissions itself.
    public bool VoicePermissionsAllowChangingOtherUsers { get; private set; } = true;

    // Every ear in this world that belongs to the person at this machine. Whatever wants to exclude
    // itself from being heard by them (their own voice, their own monitor) needs all of them, not just
    // the head's.
    public void CollectLocalUserListeners(List<AudioListener> target)
    {
        if (target == null)
            return;
        for (int i = 0; i < _listeners.Count; i++)
        {
            var listener = _listeners[i];
            if (listener != null && !listener.IsDestroyed && listener.IsLocalUserOutput)
                target.Add(listener);
        }
    }

    internal void MarkOutputChanged(AudioOutput output)
    {
        if (_disposed || output == null || !output.InManager)
            return;
        QueueUpdate(output);
        if (output.ShouldBeEnabled != output.IsRegistered)
            QueueAddOrRemove(output);
    }

    private void QueueUpdate(AudioOutput output)
    {
        if (output.QueuedForUpdate)
            return;
        output.QueuedForUpdate = true;
        _outputsToUpdate.Add(output);
    }

    private void QueueAddOrRemove(AudioOutput output)
    {
        if (output.QueuedForAddRemove)
            return;
        output.QueuedForAddRemove = true;
        _outputsToAddOrRemove.Add(output);
    }

    // UPDATE

    public void Update(double delta)
    {
        if (_disposed)
            return;

        var space = EnsureSpace();
        if (space == null)
            return;

        EnsureActive(space);

        float step = (float)delta;
        UpdateListeners(space, step);
        // After the listeners, because a zone's weight is measured from where each ear is this frame.
        UpdateEffectChains(space);
        // After the listeners, because the distance every user is measured at is the ear position this
        // pass just wrote, and before the outputs, because the manager on each avatar reads the mode and
        // the distance while it drives its emitter.
        UpdateUsers();
        ProcessOutputQueues(space);
        RefreshExclusions();
        // Builds each output's own chain after the ears have claimed theirs.
        UpdateOutputs(step);
        ReleaseDroppedFilters();
    }

    // PER-USER VOICE STATE
    //
    // One pass, once a frame, for every user in the world: what their voice is allowed to be, how far
    // away they are from my ear, and - for me only - whether my microphone setting has changed since the
    // last frame.
    private void UpdateUsers()
    {
        PruneVoicePermissions();

        _world.GetAllUsers(_userScratch);
        if (_userScratch.Count == 0)
            return;

        var localUser = _world.LocalUser;
        bool authority = _world.IsAuthority;
        var ear = LastPrimaryEarPosition;
        bool hasEar = HasPrimaryEar;

        for (int i = 0; i < _userScratch.Count; i++)
        {
            var user = _userScratch[i];
            if (user == null || user.IsDisposed)
                continue;

            var cap = MaxAllowedFor(user);
            user.SetMaxAllowedVoiceMode(cap);

            bool isLocal = ReferenceEquals(user, localUser);
            if (isLocal)
                ApplyLocalMicrophoneSetting(user);

            // Only the person speaking or the host may write the mode, so only they may clamp it. A guest
            // watching someone over the cap just reads the capped value through ActiveVoiceMode's caller.
            // The host enforcing a voice cap on someone else's record is policy, not an edit, so it takes
            // the system scope the social freeze would otherwise refuse it.
            if ((authority || isLocal) && user.VoiceMode > cap)
            {
                using (authority && !isLocal ? _world.DataModelPermissions?.EnterSystemBypass() : null)
                    user.VoiceMode = cap;
            }

            user.SetDistanceToLocalUserHead(DistanceTo(user, localUser, ear, hasEar));
        }

        _userScratch.Clear();
    }

    private static float DistanceTo(User user, User? localUser, in float3 ear, bool hasEar)
    {
        if (localUser == null || !hasEar)
            return float.PositiveInfinity;
        if (ReferenceEquals(user, localUser))
            return 0f;

        var head = user.Root?.HeadSlot;
        if (head == null || head.IsDestroyed)
            return float.PositiveInfinity;

        float distance = (head.GlobalPosition - ear).Length;
        return float.IsNaN(distance) ? float.PositiveInfinity : distance;
    }

    // The setting is the local half of "muted by anyone": a change to it writes through, and a setting
    // left ON keeps writing through so nothing can quietly unmute this machine's microphone. A host mute
    // arriving while the setting is off is left exactly where the host put it.
    private void ApplyLocalMicrophoneSetting(User user)
    {
        bool setting = EngineSettings.MicrophoneMuted;

        if (!_hasMicrophoneSetting || setting != _lastMicrophoneSetting)
        {
            _hasMicrophoneSetting = true;
            _lastMicrophoneSetting = setting;
            if (user.IsMuted.Value != setting)
                user.IsMuted.Value = setting;
            return;
        }

        if (setting && !user.IsMuted.Value)
            user.IsMuted.Value = true;
    }

    private VoiceMode MaxAllowedFor(User user)
    {
        var cap = VoiceMode.Broadcast;
        for (int i = 0; i < _voicePermissions.Count; i++)
        {
            var permission = _voicePermissions[i];
            if (permission == null || !permission.AppliesTo(user))
                continue;
            var allowed = permission.MaxAllowedVoiceMode.Value;
            if (allowed < cap)
                cap = allowed;
        }
        return cap;
    }

    private void PruneVoicePermissions()
    {
        bool allowChange = true;
        for (int i = _voicePermissions.Count - 1; i >= 0; i--)
        {
            var permission = _voicePermissions[i];
            if (permission == null || permission.IsDestroyed)
            {
                _voicePermissions.RemoveAt(i);
                continue;
            }
            if (permission.Enabled.Value && !permission.AllowChangeOtherUsers.Value)
                allowChange = false;
        }
        VoicePermissionsAllowChangingOtherUsers = allowChange;
    }

    private AudioSpace? EnsureSpace()
    {
        if (_space != null)
            return _space;

        var system = Engine.Current?.AudioSystem;
        if (system == null)
            return null;

        _space = system.Simulator.AddSpace(startActive: false);
        return _space;
    }

    // PrivateOverlay counts as heard: userspace (the dashboard and everything it plays) runs in a world
    // with that focus, and a silent dashboard is not a design decision anybody made.
    private void EnsureActive(AudioSpace space)
    {
        var focus = _world.Focus;
        bool active = focus == World.WorldFocus.Focused
            || focus == World.WorldFocus.Overlay
            || focus == World.WorldFocus.PrivateOverlay;

        if (active == space.IsActive)
            return;

        if (active)
            space.Simulator.ActivateSpace(space);
        else
            space.Simulator.DeactivateSpace(space);
    }

    private void UpdateListeners(AudioSpace space, float delta)
    {
        var device = Engine.Current?.AudioSystem?.Simulator.PrimaryOutput;
        bool sawEar = false;

        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            var listener = _listeners[i];
            if (listener == null || listener.IsDestroyed)
            {
                _listeners.RemoveAt(i);
                if (listener != null)
                {
                    listener.InManager = false;
                    DropListenerVoice(listener);
                }
                _listenersChanged = true;
                continue;
            }

            bool shouldBeActive = listener.ShouldBeActive;
            if (shouldBeActive && listener.Voice == null)
            {
                listener.Voice = space.AddListener();
                listener.HasLastPosition = false;
                listener.BoundToDevice = false;
                _listenersChanged = true;
            }
            else if (!shouldBeActive && listener.Voice != null)
            {
                DropListenerVoice(listener);
                _listenersChanged = true;
            }

            var voice = listener.Voice;
            if (voice == null)
                continue;

            var slot = listener.Slot;
            float3 position = slot != null ? slot.GlobalPosition : float3.Zero;
            floatQ rotation = slot != null ? slot.GlobalRotation : floatQ.Identity;

            voice.Update(new AudioListenerParams
            {
                Position = position,
                Rotation = rotation,
                Velocity = VelocityOf(listener.LastPosition, position, delta, listener.HasLastPosition),
            });
            listener.LastPosition = position;
            listener.HasLastPosition = true;

            // Only the local user's ear is the one at the machine, so it is the only one the device
            // renders. Everything else mixes for its own sake and stays unbound.
            bool bind = listener.IsLocalUserOutput;
            if (bind != listener.BoundToDevice)
            {
                voice.BindToDevice(bind ? device : null);
                listener.BoundToDevice = bind;
            }

            if (bind)
            {
                LastPrimaryEarPosition = position;
                LastPrimaryEarRotation = rotation;
                sawEar = true;
            }
        }

        HasPrimaryEar = sawEar;
    }

    // EFFECT CHAINS
    //
    // Rebuilt for every rendering ear and every sounding output every frame and handed to the voice only
    // when it differs from what the voice already has. An ear's chain is the listener's own effects in list
    // order, then the reverb of every zone the ear stands in, highest priority leading; an output's is its
    // own effects in list order, run on its sound before any ear hears it. Rebuilding is a walk over a
    // handful of references, and it is what makes an entry that was switched off, destroyed or retargeted
    // drop out without any of them having to say so.
    //
    // A filter holds one room's state, so it can run in one place per frame, and the claims are shared by
    // every chain in the world: the ear at the machine is served ahead of the other ears, and every ear
    // ahead of the outputs. An effect already claimed is skipped everywhere after, and a blend whose nested
    // effect is taken (or leads back to itself) runs dry. A filter that drops out of every chain is cleared
    // once all of them are built, so switching an effect back on starts a fresh room instead of the tail it
    // was cut off in. -xlinka
    private void UpdateEffectChains(AudioSpace space)
    {
        PruneZones();
        _claimed.Clear();
        int sampleRate = space.Simulator.SampleRate;
        for (int pass = 0; pass < 2; pass++)
        {
            bool bound = pass == 0;
            for (int i = 0; i < _listeners.Count; i++)
            {
                var listener = _listeners[i];
                var voice = listener.Voice;
                if (voice == null || listener.BoundToDevice != bound)
                    continue;
                BuildChain(listener, voice, sampleRate);
            }
        }
    }

    private void ReleaseDroppedFilters()
    {
        for (int i = 0; i < _dropped.Count; i++)
        {
            if (!_claimed.Contains(_dropped[i]))
                _dropped[i].Reset();
        }
        _dropped.Clear();
    }

    private void BuildChain(AudioListener listener, AudioListenerVoice voice, int sampleRate)
    {
        _chain.Clear();
        var effects = listener.Effects;
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (effect != null && Claim(effect, 0))
                _chain.Add(effect.Filter);
        }

        UpdateZoneVoices(listener, sampleRate);
        var zones = listener.ZoneVoices;
        for (int i = 0; i < zones.Count; i++)
            _chain.Add(zones[i].Blend);

        if (Swap(listener.PushedEffects))
            voice.SetEffects(_chain);
    }

    private void BuildOutputChain(AudioOutput output, AudioOutputVoice voice)
    {
        _chain.Clear();
        var effects = output.Effects;
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (effect != null && Claim(effect, 0))
                _chain.Add(effect.Filter);
        }

        if (Swap(output.PushedEffects))
            voice.SetEffects(_chain);
    }

    // False when _chain is what the voice already has. Otherwise records it as handed over and queues every
    // filter it no longer holds.
    private bool Swap(List<IAudioDspFilter> pushed)
    {
        bool same = pushed.Count == _chain.Count;
        for (int i = 0; same && i < pushed.Count; i++)
            same = ReferenceEquals(pushed[i], _chain[i]);
        if (same)
            return false;
        for (int i = 0; i < pushed.Count; i++)
        {
            if (!_chain.Contains(pushed[i]))
                _dropped.Add(pushed[i]);
        }
        pushed.Clear();
        pushed.AddRange(_chain);
        return true;
    }

    private bool Claim(AudioEffect effect, int depth)
    {
        if (!effect.IsEffectActive)
            return false;
        var filter = effect.Filter;
        if (!_claimed.Add(filter))
            return false;
        if (effect is AudioEffectBlend blend)
        {
            var nested = blend.Effect.Target;
            bool bound = nested != null && depth < MaxEffectNesting && Claim(nested, depth + 1);
            blend.BindNested(bound ? nested!.Filter : null);
        }
        return true;
    }

    // ZONES
    //
    // Each ear weighs every zone from where it stands (ReverbZone.WeightAt), then the weights are shared
    // out by priority so they never add up to more than 1: the highest priority takes what it asks for,
    // each lower one gets at most what is left, and zones of one priority that ask for more than is left
    // are scaled down together in proportion. So a booth inside a hall is the booth alone once you are in
    // it, the two crossfade across the booth's blend band, and two equal rooms overlapping share the
    // overlap instead of doubling it. A reverb whose weight has gone to 0 stays in the chain until its
    // blend reports the tail has died, so leaving a room never cuts one off. -xlinka
    private void UpdateZoneVoices(AudioListener listener, int sampleRate)
    {
        var voices = listener.ZoneVoices;
        var weights = _zoneWeights;
        weights.Clear();
        if (_zones.Count > 0)
        {
            float3 ear = listener.LastPosition;
            for (int z = 0; z < _zones.Count; z++)
            {
                var zone = _zones[z];
                if (!zone.IsZoneActive)
                    continue;
                float weight = zone.WeightAt(ear);
                if (weight > 0f)
                    weights.Add(new ZoneWeight(zone, weight, zone.Priority.Value, zone.ReferenceID.RawValue));
            }
            ShareWeights(weights);
        }

        for (int v = 0; v < voices.Count; v++)
            voices[v].Weight = 0f;
        for (int w = 0; w < weights.Count; w++)
        {
            var entry = weights[w];
            if (!(entry.Weight > 0f))
                continue;
            ZoneVoice? voice = null;
            for (int v = 0; v < voices.Count; v++)
            {
                if (ReferenceEquals(voices[v].Zone, entry.Zone))
                {
                    voice = voices[v];
                    break;
                }
            }
            if (voice == null)
            {
                voice = new ZoneVoice(entry.Zone, sampleRate);
                voices.Add(voice);
            }
            voice.Weight = entry.Weight;
        }

        for (int v = voices.Count - 1; v >= 0; v--)
        {
            var voice = voices[v];
            // Idle speaks for the weight last written, so a voice is only dropped once it has sat at 0
            // for a whole render with nothing left ringing.
            if (voice.Weight == 0f && voice.Blend.Weight == 0f && voice.Blend.IsIdle)
            {
                voices.RemoveAt(v);
                continue;
            }
            var zone = voice.Zone;
            if (!zone.IsDestroyed)
            {
                voice.Priority = zone.Priority.Value;
                if (voice.SettingsVersion != zone.SettingsVersion)
                {
                    voice.SettingsVersion = zone.SettingsVersion;
                    voice.Reverb.Settings = zone.Settings;
                }
            }
            voice.Blend.Weight = voice.Weight;
        }

        // Highest priority leading, then by id, so one zone always sits in the same place in the chain.
        for (int i = 1; i < voices.Count; i++)
        {
            var moving = voices[i];
            int j = i - 1;
            while (j >= 0 && Before(moving, voices[j]))
            {
                voices[j + 1] = voices[j];
                j--;
            }
            voices[j + 1] = moving;
        }
    }

    private static bool Before(ZoneVoice a, ZoneVoice b)
        => a.Priority != b.Priority ? a.Priority > b.Priority : a.Order < b.Order;

    private static void ShareWeights(List<ZoneWeight> weights)
    {
        for (int i = 1; i < weights.Count; i++)
        {
            var moving = weights[i];
            int j = i - 1;
            while (j >= 0 && (moving.Priority > weights[j].Priority
                              || (moving.Priority == weights[j].Priority && moving.Order < weights[j].Order)))
            {
                weights[j + 1] = weights[j];
                j--;
            }
            weights[j + 1] = moving;
        }

        float left = 1f;
        int start = 0;
        while (start < weights.Count)
        {
            int priority = weights[start].Priority;
            int end = start;
            float asked = 0f;
            while (end < weights.Count && weights[end].Priority == priority)
            {
                asked += weights[end].Weight;
                end++;
            }
            float scale = asked > left ? (asked > 0f ? left / asked : 0f) : 1f;
            for (int k = start; k < end; k++)
            {
                var entry = weights[k];
                entry.Weight *= scale;
                weights[k] = entry;
            }
            left = MathF.Max(0f, left - MathF.Min(asked, left));
            start = end;
        }
    }

    private void PruneZones()
    {
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            var zone = _zones[i];
            if (zone == null || zone.IsDestroyed)
            {
                if (zone != null)
                    zone.InManager = false;
                _zones.RemoveAt(i);
            }
        }
    }

    // How strongly one zone applies to one ear this frame.
    private struct ZoneWeight
    {
        public readonly ReverbZone Zone;
        public float Weight;
        public readonly int Priority;
        public readonly ulong Order;

        public ZoneWeight(ReverbZone zone, float weight, int priority, ulong order)
        {
            Zone = zone;
            Weight = weight;
            Priority = priority;
            Order = order;
        }
    }

    // One zone's reverb as one ear hears it: that ear's own copy of the room, behind a blend at the
    // weight the ear is at. A zone destroyed while its tail rings keeps the priority and place it had.
    internal sealed class ZoneVoice
    {
        public readonly ReverbZone Zone;
        public readonly ReverbFilter Reverb;
        public readonly BlendFilter Blend;
        public readonly ulong Order;
        public int SettingsVersion;
        public int Priority;
        public float Weight;

        public ZoneVoice(ReverbZone zone, int sampleRate)
        {
            Zone = zone;
            Reverb = new ReverbFilter(sampleRate, zone.Settings);
            Blend = new BlendFilter(Reverb);
            Order = zone.ReferenceID.RawValue;
            SettingsVersion = zone.SettingsVersion;
            Priority = zone.Priority.Value;
        }
    }

    private void ProcessOutputQueues(AudioSpace space)
    {
        for (int i = 0; i < _outputsToAddOrRemove.Count; i++)
        {
            var output = _outputsToAddOrRemove[i];
            if (output == null)
                continue;
            output.QueuedForAddRemove = false;

            if (output.IsDestroyed || !output.InManager)
            {
                DropOutputVoice(output);
                continue;
            }

            bool wanted = output.ShouldBeEnabled;
            if (wanted && output.Voice == null)
            {
                output.Voice = space.AddOutput();
                output.ClearPushedSource();
                output.HasLastPosition = false;
                output.ExclusionsDirty = true;
                output.PushSource();
            }
            else if (!wanted && output.Voice != null)
            {
                DropOutputVoice(output);
            }
        }
        _outputsToAddOrRemove.Clear();

        for (int i = 0; i < _outputsToUpdate.Count; i++)
        {
            var output = _outputsToUpdate[i];
            if (output == null)
                continue;
            output.QueuedForUpdate = false;
            if (output.IsDestroyed || output.Voice == null)
                continue;
            output.PushSource();
        }
        _outputsToUpdate.Clear();
    }

    private void RefreshExclusions()
    {
        bool all = _listenersChanged;
        _listenersChanged = false;

        for (int i = 0; i < _outputs.Count; i++)
        {
            var output = _outputs[i];
            if (output == null || output.Voice == null)
                continue;
            if (!all && !output.ExclusionsDirty)
                continue;

            output.ExclusionsDirty = false;
            _excludedScratch.Clear();
            output.CollectExcludedVoices(_excludedScratch);
            output.Voice.SetExcludedListeners(_excludedScratch.Count == 0 ? null : _excludedScratch.ToArray());
        }
    }

    // Every live voice gets its parameters every frame, queued or not: a grabbed sound emitter moves
    // without any of its own members changing, so waiting for a change pass would leave it playing from
    // where it used to be. -xlinka
    private void UpdateOutputs(float delta)
    {
        for (int i = _outputs.Count - 1; i >= 0; i--)
        {
            var output = _outputs[i];
            if (output == null || output.IsDestroyed)
            {
                _outputs.RemoveAt(i);
                if (output != null)
                {
                    output.InManager = false;
                    DropOutputVoice(output);
                }
                continue;
            }

            if (output.Voice == null)
            {
                // The way back is the same: a slot coming active again or a user unblocked raises no change
                // pass either, so an output without a voice is asked every frame whether it should have one.
                if (output.ShouldBeEnabled)
                    QueueAddOrRemove(output);
                continue;
            }

            // A slot deactivating (or a source dropping) does not raise a change pass on this component,
            // so the enabled test is re-read here rather than only when something is queued.
            if (!output.ShouldBeEnabled)
            {
                DropOutputVoice(output);
                continue;
            }

            output.Voice.Update(output.BuildParams(delta));
            BuildOutputChain(output, output.Voice);
        }
    }

    private void DropListenerVoice(AudioListener listener)
    {
        var voice = listener.Voice;
        listener.Voice = null;
        listener.BoundToDevice = false;
        listener.HasLastPosition = false;
        for (int i = 0; i < listener.PushedEffects.Count; i++)
            listener.PushedEffects[i].Reset();
        listener.PushedEffects.Clear();
        listener.ZoneVoices.Clear();
        if (voice == null || _space == null)
            return;
        voice.BindToDevice(null);
        _space.RemoveListener(voice);
    }

    private void DropOutputVoice(AudioOutput output)
    {
        var voice = output.Voice;
        output.Voice = null;
        output.ClearPushedSource();
        output.HasLastPosition = false;
        for (int i = 0; i < output.PushedEffects.Count; i++)
            output.PushedEffects[i].Reset();
        output.PushedEffects.Clear();
        if (voice == null || _space == null)
            return;
        voice.SetEffects(null);
        voice.SetSource(null);
        _space.RemoveOutput(voice);
    }

    internal static float3 VelocityOf(float3 last, float3 now, float delta, bool hasLast)
    {
        if (!hasLast || delta <= 0.0001f)
            return float3.Zero;

        var velocity = (now - last) / delta;
        float lengthSquared = velocity.LengthSquared;
        if (float.IsNaN(lengthSquared) || lengthSquared > MaxVelocity * MaxVelocity)
            return float3.Zero;
        return velocity;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        for (int i = 0; i < _listeners.Count; i++)
        {
            var listener = _listeners[i];
            if (listener == null)
                continue;
            listener.InManager = false;
            DropListenerVoice(listener);
        }
        _listeners.Clear();

        for (int i = 0; i < _outputs.Count; i++)
        {
            var output = _outputs[i];
            if (output == null)
                continue;
            output.InManager = false;
            output.QueuedForUpdate = false;
            output.QueuedForAddRemove = false;
            DropOutputVoice(output);
        }
        _outputs.Clear();
        _outputsToAddOrRemove.Clear();
        _outputsToUpdate.Clear();
        _excludedScratch.Clear();
        _voicePermissions.Clear();
        _userScratch.Clear();
        for (int i = 0; i < _zones.Count; i++)
        {
            if (_zones[i] != null)
                _zones[i].InManager = false;
        }
        _zones.Clear();
        _zoneWeights.Clear();
        _chain.Clear();
        _claimed.Clear();
        _dropped.Clear();

        var space = _space;
        _space = null;
        space?.Simulator.RemoveSpace(space);
    }
}
