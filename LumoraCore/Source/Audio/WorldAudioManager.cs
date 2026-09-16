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
        // After the listeners, because the distance every user is measured at is the ear position this
        // pass just wrote, and before the outputs, because the manager on each avatar reads the mode and
        // the distance while it drives its emitter.
        UpdateUsers();
        ProcessOutputQueues(space);
        RefreshExclusions();
        UpdateOutputs(step);
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
            if ((authority || isLocal) && user.VoiceMode > cap)
                user.VoiceMode = cap;

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
                continue;

            // A slot deactivating (or a source dropping) does not raise a change pass on this component,
            // so the enabled test is re-read here rather than only when something is queued.
            if (!output.ShouldBeEnabled)
            {
                DropOutputVoice(output);
                continue;
            }

            output.Voice.Update(output.BuildParams(delta));
        }
    }

    private void DropListenerVoice(AudioListener listener)
    {
        var voice = listener.Voice;
        listener.Voice = null;
        listener.BoundToDevice = false;
        listener.HasLastPosition = false;
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
        if (voice == null || _space == null)
            return;
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

        var space = _space;
        _space = null;
        space?.Simulator.RemoveSpace(space);
    }
}
