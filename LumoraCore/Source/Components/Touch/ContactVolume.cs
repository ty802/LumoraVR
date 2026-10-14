// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// What a contact sender and a contact receiver share: a sphere or capsule hung off an anchor slot, the tags it
// works with, and whose it is.
//
// Nothing about a contact is replicated beyond its settings. Every peer places every shape from the poses it
// already has and works the touches out itself, the way bone chains swing, so a touch costs no traffic at all.
// LocalOnly keeps one to the machine of the user wearing it. -xlinka
public abstract class ContactVolume : Component
{
    // Where the shape hangs. Empty is this slot. The component stays where it is, so switching this object off
    // still switches the contact off.
    public readonly SyncRef<Slot> Anchor;

    public readonly Sync<float> Radius;

    // End to end along the turned local Y. At most twice the radius is a sphere.
    public readonly Sync<float> Height;

    public readonly Sync<float3> Offset;
    public readonly Sync<floatQ> Rotation;

    public readonly SyncFieldList<string> Tags;

    // Only works on the machine of the user wearing it.
    public readonly Sync<bool> LocalOnly;

    private int[] _tagIds = Array.Empty<int>();
    private ulong _tagMask;
    private bool _tagsDirty = true;

    private object? _owner;
    private bool _onAvatar;
    private ulong _ownerFrame;
    private bool _ownerKnown;

    private float3 _lastCentre;
    private ulong _lastFrame;
    private bool _hasLast;

    protected ContactVolume()
    {
        Anchor = new SyncRef<Slot>(this);
        Radius = new Sync<float>(this, 0.05f);
        Height = new Sync<float>(this, 0f);
        Offset = new Sync<float3>(this, float3.Zero);
        Rotation = new Sync<floatQ>(this, floatQ.Identity);
        Tags = new SyncFieldList<string>(this);
        LocalOnly = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Tags.OnChanged += _ => _tagsDirty = true;
        Slot.ActiveUserRootChanged += OnUserChanged;
    }

    public override void OnDestroy()
    {
        Slot.ActiveUserRootChanged -= OnUserChanged;
        base.OnDestroy();
    }

    private void OnUserChanged(Slot slot) => _ownerKnown = false;

    internal int[] TagIds
    {
        get
        {
            RefreshTags();
            return _tagIds;
        }
    }

    internal ulong TagMask
    {
        get
        {
            RefreshTags();
            return _tagMask;
        }
    }

    private void RefreshTags()
    {
        if (!_tagsDirty)
            return;
        _tagsDirty = false;
        var names = new string[Tags.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = Tags[i] ?? "";
        _tagIds = ContactTags.Ids(names);
        _tagMask = ContactTags.Mask(_tagIds);
    }

    // The user wearing it, or else the object it is part of: the nearest marked object root, or the slot right under
    // the world's root. Looked up again every couple of seconds and whenever the slot changes user.
    internal object? Owner(ulong frame, out bool onAvatar)
    {
        if (!_ownerKnown || frame - _ownerFrame > 120)
        {
            _ownerKnown = true;
            _ownerFrame = frame;
            var user = Slot.ActiveUser;
            if (user != null)
            {
                _owner = user;
                _onAvatar = true;
            }
            else
            {
                Slot? top = null;
                for (var s = Slot; s != null; s = s.Parent)
                {
                    if (s.GetComponent<ObjectRoot>() != null)
                    {
                        top = s;
                        break;
                    }
                    if (s.Parent != null && s.Parent.Parent == null)
                    {
                        top = s;
                        break;
                    }
                }
                _owner = top;
                _onAvatar = top?.GetComponentInChildren<HumanoidRig>() != null;
            }
        }
        onAvatar = _onAvatar;
        return _owner;
    }

    // Whether this machine runs it at all this frame.
    internal bool Live(World world, object? owner)
        => Enabled.Value && Slot.IsActive && (!LocalOnly.Value || owner is not User user || ReferenceEquals(user, world.LocalUser));

    internal bool Shape(out float3 a, out float3 b, out float radius)
    {
        var anchor = Anchor.Target;
        if (anchor == null || anchor.IsDestroyed)
            anchor = Slot;
        return ContactShape.Place(anchor, Radius.Value, Height.Value, Offset.Value, Rotation.Value, out a, out b, out radius);
    }

    // How fast the shape's middle moved since the frame before; nothing when it was not placed then.
    internal float3 Velocity(in float3 centre, ulong frame, float delta)
    {
        float3 velocity = _hasLast && frame == _lastFrame + 1 && delta > 1e-5f ? (centre - _lastCentre) / delta : float3.Zero;
        _lastCentre = centre;
        _lastFrame = frame;
        _hasLast = true;
        return velocity;
    }
}
