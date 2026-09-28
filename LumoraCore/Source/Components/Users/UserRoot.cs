// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using AudioListener = Lumora.Core.Components.Audio.AudioListener;
using AudioOutput = Lumora.Core.Components.Audio.AudioOutput;
using AvatarAudioOutputManager = Lumora.Core.Components.Audio.AvatarAudioOutputManager;
using AvatarVoiceInfo = Lumora.Core.Components.Audio.AvatarVoiceInfo;
using AudioTypeGroup = Lumora.Core.Audio.AudioTypeGroup;
using OpusStream = Lumora.Core.Networking.Streams.OpusStream;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

[ComponentCategory("Users")]
public class UserRoot : Component
{
    public enum UserNode
    {
        None,
        Root,
        Head,
        Body,
        LeftHand,
        RightHand,
        LeftFoot,
        RightFoot
    }

    // USER REFERENCE
    // so clients can identify their own
    public readonly SyncRef<User> TargetUser = null!;
    public readonly SyncRef<Slot> TrackingSpaceRoot = new();

    public Slot TrackingSpaceSlot => TrackingSpaceRoot.Target is { IsDestroyed: false } space
        ? space : Slot.FindChild("Body Nodes", recursive: false) ?? Slot;

    // Where this user's eye cameras take their clip planes from. The worn avatar's AvatarRenderSettings
    // registers here on equip and clears itself on dequip; empty means the engine defaults.
    public readonly SyncRef<Avatar.IRenderSettingsSource> RenderSettings = new();

    public Avatar.IRenderSettingsSource? ActiveRenderSettings
        => RenderSettings.Target is { IsDestroyed: false } source ? source : null;

    public void SetAvatarTrackingOffset(float3 offset)
    {
        var space = TrackingSpaceSlot;
        if (space != Slot)
            space.LocalPosition.Value = offset;
    }

    private bool _isRegistered = false;

    public User ActiveUser => (TargetUser?.Target) ?? null!;

    public bool IsLocalUserRoot => TargetUser?.Target != null && TargetUser.Target == World?.LocalUser;

    // CACHED BODY NODES
    private Slot _cachedHeadSlot = null!;
    private Slot _cachedBodySlot = null!;
    private Slot _cachedLeftHandSlot = null!;
    private Slot _cachedRightHandSlot = null!;
    private Slot _cachedLeftFootSlot = null!;
    private Slot _cachedRightFootSlot = null!;

    // BODY NODE ACCESSORS
    //
    // Resolved from the typed UserRootComponent registry rather than string-
    // named child lookups. Any TrackedDevicePositioner under the user that
    // declares AutoBodyNode == HeadNode shows up here automatically - slots
    // can be renamed or moved without breaking the lookup. - xlinka

    public Slot HeadSlot => GetBodyNodeSlot(ref _cachedHeadSlot, Input.BodyNode.Head)
                            ?? FallbackChildLookup(ref _cachedHeadSlot, "Head");

    public Slot LeftHandSlot => GetBodyNodeSlot(ref _cachedLeftHandSlot, Input.BodyNode.LeftHand)
                                ?? FallbackChildLookup(ref _cachedLeftHandSlot, "LeftHand");

    public Slot RightHandSlot => GetBodyNodeSlot(ref _cachedRightHandSlot, Input.BodyNode.RightHand)
                                 ?? FallbackChildLookup(ref _cachedRightHandSlot, "RightHand");

    public Slot LeftFootSlot => GetBodyNodeSlot(ref _cachedLeftFootSlot, Input.BodyNode.LeftFoot)
                                ?? FallbackChildLookup(ref _cachedLeftFootSlot, "LeftFoot");

    public Slot RightFootSlot => GetBodyNodeSlot(ref _cachedRightFootSlot, Input.BodyNode.RightFoot)
                                 ?? FallbackChildLookup(ref _cachedRightFootSlot, "RightFoot");

    // Body component (avatar torso) - not a tracked device, still needs string lookup.
    public Slot BodySlot
    {
        get
        {
            if ((_cachedBodySlot == null || _cachedBodySlot.IsDestroyed) && !IsDestroyed)
            {
                _cachedBodySlot = Slot.FindChild("Avatar", recursive: false)?.FindChild("Body", recursive: false)!;
            }
            return _cachedBodySlot!;
        }
    }

    private Slot GetBodyNodeSlot(ref Slot cache, Input.BodyNode node)
    {
        if (IsDestroyed)
            return null!;

        var positioner = GetRegisteredComponent<TrackedDevicePositioner>(p => p.AutoBodyNode.Value == node);
        var resolved = positioner?.Slot;

        if (cache != null && !cache.IsDestroyed)
        {
            // A tracked device positioner has two relevant slots:
            // - Slot: the tracked body transform (head/controller/etc.)
            // - BodyNodeRoot: an equipment/object child that only carries offsets
            // User-space consumers need the tracked transform. Older code cached the
            // child, which double-applied desktop head height and moved nametags/IK.
            if (resolved == null || ReferenceEquals(cache, resolved))
                return cache;
        }

        // Not every body node is device-tracked: hands are AvatarSockets
        // riding under the controller positioners. Resolve through the
        // registered object slots before giving up.
        if (resolved == null)
        {
            resolved = GetRegisteredComponent<Avatar.AvatarSocket>(s => s.Node.Value == node)?.Slot;
        }

        if (resolved != null)
            cache = resolved;
        return resolved!;
    }

    // Old string-named child lookup. Kept as a fallback for slots that exist
    // before the matching TrackedDevicePositioner registers, and for avatars
    // built by templates that don't use tracked-device positioners at all.
    // - xlinka
    private Slot FallbackChildLookup(ref Slot cache, string name)
    {
        if (cache != null && !cache.IsDestroyed)
            return cache;
        if (IsDestroyed)
            return null!;

        cache = (Slot.FindChild("Body Nodes", recursive: false)?.FindChild(name, recursive: false) ??
                 Slot.FindChild(name, recursive: false) ??
                 Slot.FindChild("Avatar", recursive: false)?.FindChild(name, recursive: false))!;
        return cache!;
    }

    // AUDIO
    //
    // The ear rides the head slot, one per user and replicated with the rest of the scaffold, because
    // everyone has to agree on where a user's ears are before excluding a listener from an output means
    // anything. Only the local user's listener is ever bound to the device - see AudioListener - so the
    // remote ones cost nothing but a registration.
    //
    // Built here rather than in the avatar assembler because the head slot only resolves once the body
    // nodes have been built, which is after the assembler has run. -xlinka
    private AudioListener _primaryListener = null!;

    public AudioListener PrimaryListener
        => _primaryListener != null && !_primaryListener.IsDestroyed ? _primaryListener : null!;

    private void EnsurePrimaryListener()
    {
        if (_primaryListener != null && !_primaryListener.IsDestroyed)
            return;
        if (IsDestroyed || World == null)
            return;

        var head = HeadSlot;
        var user = ActiveUser;
        if (head == null || head.IsDestroyed || user == null)
            return;

        // A saved world (or a scaffold that already replicated in) brings its own; adding a second ear
        // to the same head would mix the world twice.
        var existing = head.GetComponent<AudioListener>();
        if (existing != null)
        {
            _primaryListener = existing;
            if (World.IsAuthority && existing.ActiveUser.Target != user)
                existing.ActiveUser.Target = user;
            return;
        }

        // The scaffold is the authority's to build, and this is part of it. A client attaching its own
        // would mint one listener per peer on the same head.
        if (!World.IsAuthority)
            return;

        var listener = head.AttachComponent<AudioListener>();
        listener.ActiveUser.Target = user;
        _primaryListener = listener;
    }

    // VOICE
    //
    // Same shape as the ear, for the same reason: everyone has to agree which stream is whose voice
    // and where in the room it comes out before excluding anybody from it means anything.
    //
    // The stream itself belongs to the speaker, because only a stream's owner may create one, so a
    // remote user's shows up here a frame or two after they join rather than the instant their root
    // does. The scaffold is still the authority's to build; it just fills the ref when the stream
    // arrives. -xlinka
    public const string VoiceStreamName = "Voice";

    // Above an ordinary emitter, below nothing. When the voice limit bites, the person talking to you
    // is not the thing to drop. The local user's own output goes to the top in the manager, the one
    // emitter that must never be culled.
    private const int VoicePriority = 200;

    private UserAudioStream _voiceStream = null!;
    private AudioOutput _voiceOutput = null!;
    private AvatarAudioOutputManager _voiceOutputManager = null!;
    private bool _voiceWired;

    public UserAudioStream VoiceStream
        => _voiceStream != null && !_voiceStream.IsDestroyed ? _voiceStream : null!;

    private void EnsureVoiceOutput()
    {
        if (IsDestroyed || World == null)
            return;

        if (_voiceWired)
        {
            if (_voiceStream != null && !_voiceStream.IsDestroyed
                && _voiceOutput != null && !_voiceOutput.IsDestroyed)
            {
                return;
            }
            _voiceWired = false;
        }

        var user = ActiveUser;
        var head = HeadSlot;
        if (user == null || head == null || head.IsDestroyed)
            return;

        // The speaker mints their own; nobody can do it on their behalf.
        var stream = user.IsLocal
            ? user.GetStreamOrAdd<OpusStream>(VoiceStreamName)
            : user.GetStream<OpusStream>(s => s.Name == VoiceStreamName);

        if (_voiceStream == null || _voiceStream.IsDestroyed)
            _voiceStream = Slot.GetComponent<UserAudioStream>()!;
        if (_voiceOutput == null || _voiceOutput.IsDestroyed)
            _voiceOutput = head.GetComponent<AudioOutput>()!;

        bool authority = World.IsAuthority;

        if (_voiceStream == null || _voiceStream.IsDestroyed)
        {
            if (!authority)
                return;
            _voiceStream = Slot.AttachComponent<UserAudioStream>();
        }

        if (_voiceOutput == null || _voiceOutput.IsDestroyed)
        {
            if (!authority)
                return;
            _voiceOutput = head.AttachComponent<AudioOutput>();
            ConfigureVoiceOutput(_voiceOutput);
        }

        if (_voiceOutputManager == null || _voiceOutputManager.IsDestroyed)
            _voiceOutputManager = head.GetComponent<AvatarAudioOutputManager>()!;

        if (!authority)
            return;

        if (stream != null && _voiceStream.Stream.Target != stream)
            _voiceStream.Stream.Target = stream;

        if (_voiceOutput.Source.Target != _voiceStream)
            _voiceOutput.Source.Target = _voiceStream;

        // Where the voice comes from, published on the root registry so a mouth visualiser or a level
        // meter finds it without guessing at a slot name.
        var info = GetRegisteredComponent<AvatarVoiceInfo>() ?? Slot.AttachComponent<AvatarVoiceInfo>();
        if (info.AudioSource.Target != _voiceStream)
            info.AudioSource.Target = _voiceStream;

        // Distances, rolloff, spatialization and the exclusion are the manager's from here. They used to
        // be a hard-coded 1..100 m written once at attach, which meant a voice had exactly one range and
        // whisper/shout/broadcast had nowhere to land.
        if (_voiceOutputManager == null || _voiceOutputManager.IsDestroyed)
            _voiceOutputManager = head.AttachComponent<AvatarAudioOutputManager>();
        if (_voiceOutputManager.AudioOutput.Target != _voiceOutput)
            _voiceOutputManager.BindTo(_voiceOutput);

        // Wired once. A remote user's stream arrives a moment after their root, so until it does the
        // walk repeats; after that this is a pair of null checks a frame.
        _voiceWired = stream != null && _voiceStream.Stream.Target == stream;
    }

    private static void ConfigureVoiceOutput(AudioOutput output)
    {
        output.AudioTypeGroup.Value = AudioTypeGroup.Voice;
        // A head moves fast enough in VR to pitch-shift a voice audibly, and nobody wants their friend
        // to whistle past them. The manager drives this too, but the attach-time value is what a peer
        // hears in the frames before the drive links are granted.
        output.DopplerLevel.Value = 0f;
        output.Priority.Value = VoicePriority;
    }

    // POSITION ACCESSORS

    public float3 HeadPosition
    {
        get => HeadSlot?.GlobalPosition ?? Slot.GlobalPosition;
        set
        {
            if (HeadSlot != null)
            {
                var offset = value - HeadPosition;
                Slot.GlobalPosition += offset;
            }
        }
    }

    public floatQ HeadRotation
    {
        get => HeadSlot?.GlobalRotation ?? Slot.GlobalRotation;
        set
        {
            if (HeadSlot != null)
            {
                HeadSlot.GlobalRotation = value;
            }
        }
    }

    public float3 FeetPosition
    {
        get
        {
            if (LeftFootSlot != null && RightFootSlot != null)
            {
                return (LeftFootSlot.GlobalPosition + RightFootSlot.GlobalPosition) / 2f;
            }
            // Fallback: project head position to ground
            var headPos = HeadPosition;
            return new float3(headPos.x, Slot.GlobalPosition.y, headPos.z);
        }
        set
        {
            var offset = value - FeetPosition;
            Slot.GlobalPosition += offset;
        }
    }

    public float GlobalScale
    {
        get => Slot.Scale.x;
        set
        {
            Slot.Scale = float3.One * value;
        }
    }

    private TransformStreamDriver _rootStreamDriver = null!;

    // A TransformStreamDriver sharing the root slot means the Root stream is the transport for the root
    // transform. Turning has to write SILENTLY then, or the same rotation goes out over the stream AND the
    // delta channel and the two arrive a frame apart. Re-checks until the driver is found (it is attached
    // during the avatar build), then caches. Same rule the character controller hook follows when it writes
    // walking motion onto this slot. -xlinka
    private bool RootIsStreamed()
    {
        if (_rootStreamDriver == null || _rootStreamDriver.IsDestroyed)
            _rootStreamDriver = Slot?.GetComponent<TransformStreamDriver>()!;
        return _rootStreamDriver != null && !_rootStreamDriver.IsDestroyed;
    }

    private void WriteRootRotation(floatQ rotation)
    {
        if (RootIsStreamed())
            Slot.SetGlobalRotationSilently(rotation);
        else
            Slot.GlobalRotation = rotation;
    }

    private void WriteRootPosition(float3 position)
    {
        if (RootIsStreamed())
            Slot.SetGlobalPositionSilently(position);
        else
            Slot.GlobalPosition = position;
    }

    // Below a hundredth of a millimetre the pivot correction is float noise, and paying it every frame
    // would put a transform write (and a hook update) on the root for every frame of mouse look.
    private const float PivotCorrectionSq = 1e-10f;

    // Rotate the WHOLE rig and keep the head where it is. Everything tracked hangs off this slot in
    // user-root space - head, controllers, hands, the avatar - so turning the root is what carries them
    // all round together, and pinning the head is what makes it read as turning in place rather than
    // being swung around a pole. This is the one rotate a turn goes through, snap, smooth or mouse.
    public void RotateAroundHead(floatQ deltaRotation)
    {
        if (Slot == null)
            return;

        // Resolved once: desktop look comes through here every frame the mouse moves, and the body-node
        // lookup behind HeadSlot is a registry walk. No head (rig still building) pivots on the root, which
        // is what a plain rotation write would have done anyway.
        var head = HeadSlot;
        var pivot = head != null ? head.GlobalPosition : Slot.GlobalPosition;

        WriteRootRotation((deltaRotation * Slot.GlobalRotation).Normalized);

        var offset = pivot - (head != null ? head.GlobalPosition : Slot.GlobalPosition);
        if (offset.LengthSquared > PivotCorrectionSq)
            WriteRootPosition(Slot.GlobalPosition + offset);
    }

    public void RotateYawAroundHead(float yawRadians)
    {
        if (System.Math.Abs(yawRadians) < 0.000001f)
            return;

        RotateAroundHead(floatQ.AxisAngle(float3.Up, yawRadians));
    }

    public bool ReceivedFirstPositionalData
    {
        get
        {
            if (HeadSlot != null)
            {
                var headPos = HeadSlot.LocalPosition.Value;
                var headRot = HeadSlot.LocalRotation.Value;
                if (headPos != float3.Zero || headRot != floatQ.Identity)
                    return true;
            }

            var headPositioner = HeadSlot?.GetComponent<TrackedDevicePositioner>();
            if (headPositioner != null)
                return headPositioner.IsTracking.Value;

            // For desktop mode, we consider it always tracked
            if (ActiveUser != null && Engine.Current?.InputInterface != null)
            {
                var headDevice = Engine.Current.InputInterface.HeadDevice;
                return headDevice == null || !headDevice.IsTracked; // Desktop mode
            }

            return false;
        }
    }

    // INITIALIZATION

    // called by SimpleUserSpawn after attaching the component
    public void Initialize(User user)
    {
        if (user == null)
        {
            LumoraLogger.Error("UserRoot: Cannot initialize with null user");
            return;
        }

        TargetUser.Target = user;

        if (World?.IsAuthority == true)
        {
            // The authority wiring a joiner's record to the root it just built them, an engine step and
            // not an edit: the social freeze refuses every write to what the writer does not own. -xlinka
            using (World.DataModelPermissions?.EnterSystemBypass())
                user.Root = this;
            LumoraLogger.Log($"User: Registered UserRoot for authority user '{user.UserName.Value}'");
        }

        LumoraLogger.Log($"UserRoot: Initialized for user '{user.UserName.Value}' (RefID: {user.ReferenceID})");
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Slot?.RegisterUserRoot(this);
    }

    // Typed component cache. Every UserRootComponent in the user's slot
    // hierarchy registers here. Lookups like "find the AvatarIK attached
    // anywhere under this user" are O(1) by type instead of walking the
    // slot tree + GetComponents per slot. - xlinka
    private readonly HashSet<Component> _registeredComponents = new();
    private readonly Dictionary<Type, IList> _perTypeComponents = new();

    internal void RegisterComponent(Component component)
    {
        if (component == null || !_registeredComponents.Add(component))
            return;

        var type = component.GetType();
        foreach (var kvp in _perTypeComponents)
        {
            if (kvp.Key.IsAssignableFrom(type))
                kvp.Value.Add(component);
        }
    }

    internal void UnregisterComponent(Component component)
    {
        if (component == null || !_registeredComponents.Remove(component))
            return;

        var type = component.GetType();
        foreach (var kvp in _perTypeComponents)
        {
            if (kvp.Key.IsAssignableFrom(type))
                kvp.Value.Remove(component);
        }
    }

    private List<T> GetComponentsOfType<T>() where T : class
    {
        if (_perTypeComponents.TryGetValue(typeof(T), out var existing))
            return (List<T>)existing;

        var list = new List<T>();
        foreach (var c in _registeredComponents)
        {
            if (c is T match)
                list.Add(match);
        }
        _perTypeComponents[typeof(T)] = list;
        return list;
    }

    public T GetRegisteredComponent<T>(Predicate<T> filter = null!) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            if (filter == null || filter(item))
                return item;
        }
        return null!;
    }

    public void GetRegisteredComponents<T>(List<T> output, Predicate<T> filter = null!) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            if (filter == null || filter(item))
                output.Add(item);
        }
    }

    public List<T> GetRegisteredComponents<T>(Predicate<T> filter = null!) where T : class
    {
        var list = new List<T>();
        GetRegisteredComponents(list, filter);
        return list;
    }

    public void ForeachRegisteredComponent<T>(Action<T> action) where T : class
    {
        foreach (var item in GetComponentsOfType<T>())
        {
            if (item is Component c && c.IsDestroyed) continue;
            action(item);
        }
    }

    public override void OnChanges()
    {
        base.OnChanges();

        if (TargetUser.Target == World?.LocalUser && !_isRegistered)
        {
            World.LocalUser.Root = this;
            _isRegistered = true;
            LumoraLogger.Log($"UserRoot: Registered as Root for local user '{TargetUser.Target?.UserName?.Value}'");
        }

        if (TargetUser.Target != World?.LocalUser && _isRegistered)
        {
            if (World?.LocalUser?.Root == this)
            {
                World.LocalUser.Root = null!;
            }
            _isRegistered = false;
        }
    }

    public float3 GetGlobalPosition(UserNode node)
    {
        return node switch
        {
            UserNode.None => float3.Zero,
            UserNode.Root => Slot.GlobalPosition,
            UserNode.Head => HeadSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.Body => BodySlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.LeftHand => LeftHandSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.RightHand => RightHandSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.LeftFoot => LeftFootSlot?.GlobalPosition ?? Slot.GlobalPosition,
            UserNode.RightFoot => RightFootSlot?.GlobalPosition ?? Slot.GlobalPosition,
            _ => throw new ArgumentException($"Invalid UserNode: {node}")
        };
    }

    public floatQ GetGlobalRotation(UserNode node)
    {
        return node switch
        {
            UserNode.None => floatQ.Identity,
            UserNode.Root => Slot.GlobalRotation,
            UserNode.Head => HeadSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.Body => BodySlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.LeftHand => LeftHandSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.RightHand => RightHandSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.LeftFoot => LeftFootSlot?.GlobalRotation ?? floatQ.Identity,
            UserNode.RightFoot => RightFootSlot?.GlobalRotation ?? floatQ.Identity,
            _ => throw new ArgumentException($"Invalid UserNode: {node}")
        };
    }

    // flattened for locomotion
    public float3 HeadFacingDirection
    {
        get
        {
            floatQ headRot = HeadSlot?.GlobalRotation ?? Slot.GlobalRotation;
            // Godot uses -Z as forward; align locomotion basis accordingly
            float3 forward = headRot * float3.Backward;
            forward.y = 0;
            return forward.LengthSquared < 1e-6f ? float3.Backward : forward.Normalized;
        }
    }

    public floatQ HeadFacingRotation
    {
        get
        {
            if (HeadSlot != null)
                return HeadSlot.GlobalRotation;
            return Slot.GlobalRotation;
        }
    }

    public void SetGlobalPosition(UserNode node, float3 position)
    {
        switch (node)
        {
            case UserNode.Root:
                Slot.GlobalPosition = position;
                break;
            case UserNode.Head:
                HeadPosition = position;
                break;
            case UserNode.Body:
                if (BodySlot != null)
                    BodySlot.GlobalPosition = position;
                break;
            case UserNode.LeftHand:
                if (LeftHandSlot != null)
                    LeftHandSlot.GlobalPosition = position;
                break;
            case UserNode.RightHand:
                if (RightHandSlot != null)
                    RightHandSlot.GlobalPosition = position;
                break;
            case UserNode.LeftFoot:
                if (LeftFootSlot != null)
                    LeftFootSlot.GlobalPosition = position;
                break;
            case UserNode.RightFoot:
                if (RightFootSlot != null)
                    RightFootSlot.GlobalPosition = position;
                break;
        }
    }

    // Rotates the ROOT, not the node's own slot: every body node except Root is written each frame by
    // whatever drives it (tracking positioner, IK), so a rotation stamped on the node itself survives
    // exactly until the next update. Turning the root is the only write that sticks, and pinning the
    // node's position across the turn is what stops the rig from swinging out from under the user -
    // the same pivot trick RotateAroundHead uses, generalized to any node. - xlinka
    public void SetGlobalRotation(UserNode node, floatQ rotation)
    {
        if (Slot == null || node == UserNode.None)
            return;

        var current = GetGlobalRotation(node);
        var delta = (rotation * current.Inverse).Normalized;

        var pivot = GetGlobalPosition(node);
        WriteRootRotation((delta * Slot.GlobalRotation).Normalized);
        var moved = pivot - GetGlobalPosition(node);
        if (moved.LengthSquared > PivotCorrectionSq)
            WriteRootPosition(Slot.GlobalPosition + moved);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        EnsurePrimaryListener();
        // After the ear, because the voice output has to exclude it and the list is filled once.
        EnsureVoiceOutput();

        // Validate scale to prevent invalid transforms. These corrective writes run on EVERY peer (incl. an
        // observer holding a REMOTE user's root), and use the THROWING .Value/Scale setters - so a foreign-write
        // denial would throw per frame. It's engine sanitization of a body transform, not a user edit, so bypass
        // the gate. Only entered when a correction is actually needed (rare). -xlinka
        var scale = Slot.Scale;
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
            float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsNaN(scale.z) ||
            float.IsInfinity(scale.x) || float.IsInfinity(scale.y) || float.IsInfinity(scale.z))
        {
            LumoraLogger.Warn($"UserRoot: Invalid scale detected ({scale}), resetting to (1,1,1)");
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.LocalScale.Value = float3.One;
        }

        if (System.Math.Abs(scale.x - scale.y) > 0.0001f || System.Math.Abs(scale.y - scale.z) > 0.0001f)
        {
            var avgScale = (scale.x + scale.y + scale.z) / 3f;
            using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
            Slot.Scale = float3.One * avgScale;
        }
    }

    public override void OnDestroy()
    {
        LumoraLogger.Log($"UserRoot: Destroying UserRoot for user '{ActiveUser?.UserName.Value ?? "Unknown"}'");

        Slot?.UnregisterUserRootHierarchy(this);

        // During world disposal users are disposed before slots/components.
        // Their Root setter is no longer valid, and the user object is going away anyway.
        if (World?.IsDisposed != true && _isRegistered && World?.LocalUser?.Root == this)
        {
            World.LocalUser.Root = null!;
        }
        _isRegistered = false;

        _primaryListener = null!;
        _voiceStream = null!;
        _voiceOutput = null!;
        _voiceWired = false;
        _cachedHeadSlot = null!;
        _cachedBodySlot = null!;
        _cachedLeftHandSlot = null!;
        _cachedRightHandSlot = null!;
        _cachedLeftFootSlot = null!;
        _cachedRightFootSlot = null!;

        base.OnDestroy();
    }
}
