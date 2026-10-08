// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Audio;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components;

// Puts the view, the tracked space or the ears of whoever carries this object onto one of its slots; a pool
// cue's aiming camera is what it was made for. View moves where a screen draws from and where the user's view
// reads from; Root moves where a headset's space is drawn from; Ears hears from the slot instead of the head.
//
// It acts on one machine, the one whose person carries the object, and only while the object is really
// carried: held in one of the carrier's own hands (by that hand's own list of what it picked up here, which no
// other machine writes), or worn through one of the carrier's own avatar sockets. Sitting under somebody's root
// is not enough, since anything can be parented there, and nobody else's view is ever touched. View and Root
// go on the carrier's UserRoot, which only the carrier's machine writes; Ears is a listener on a slot that
// exists on that machine alone. Switched off, dropped, taken, destroyed, or with the world out of focus, it
// hands everything back.
//
// Nothing runs per frame: it looks again when its settings change, when the object passes to another user,
// when its slot goes, and when the carrier's focus changes, each of which lands in OnChanges. -xlinka
[ComponentCategory("Users")]
public class HolderViewOverride : Component
{
    public enum OverrideMode
    {
        View,
        Root,
        Ears,
    }

    public readonly SyncRef<Slot> Target = new();
    public readonly Sync<OverrideMode> Mode = new();

    // What is applied on this machine, if anything.
    private UserRoot? _root;
    private Slot? _target;
    private OverrideMode _mode;
    private AudioListener? _ear;
    private AudioListener? _quieted;

    // The carrier whose focus is being listened to, while this machine's user carries the object.
    private User? _presence;

    private readonly Action<Slot> _moved;
    private readonly Action<Slot> _targetGone;
    private readonly Action<IChangeable> _focusChanged;

    public HolderViewOverride()
    {
        _moved = _ => MarkChangeDirty();
        _focusChanged = _ => MarkChangeDirty();
        _targetGone = OnTargetGone;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveUserRootChanged += _moved;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Refresh();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveUserRootChanged -= _moved;
        WatchFocus(null);
        Release();
        base.OnDestroy();
    }

    private void Refresh()
    {
        var carrier = Carrier();
        WatchFocus(carrier?.ActiveUser);
        var root = carrier != null && carrier.ActiveUser.IsPresent.Value ? carrier : null;
        var target = root != null && Target.Target is { IsDestroyed: false } slot ? slot : null;
        var mode = Mode.Value;
        if (ReferenceEquals(root, _root) && ReferenceEquals(target, _target) && mode == _mode)
            return;

        Release();
        if (root != null && target != null)
            Apply(root, target, mode);
    }

    // The local user's root, when that root carries this and the component is on.
    private UserRoot? Carrier()
    {
        var world = World;
        var slot = Slot;
        if (IsDestroyed || !Enabled.Value || world == null || world.IsDisposed || world.IsDestroyed || slot == null)
            return null;
        var root = slot.ActiveUserRoot;
        var local = world.LocalUser;
        if (root == null || root.IsDestroyed || root.Slot == null || local == null || !ReferenceEquals(root.ActiveUser, local))
            return null;
        return CarriedBy(root) ? root : null;
    }

    private bool CarriedBy(UserRoot root)
    {
        var top = root.Slot;
        for (var at = Slot; at != null && !ReferenceEquals(at, top); at = at.Parent)
        {
            foreach (var component in at.Components)
            {
                if (component is IGrabbable grabbable && HeldBy(grabbable, root))
                    return true;
                if (component is AvatarSocket socket && Wears(socket, root))
                    return true;
            }
        }
        return false;
    }

    private static bool HeldBy(IGrabbable grabbable, UserRoot root)
    {
        var hand = grabbable.Grabber;
        if (hand == null || hand.IsDestroyed || !ReferenceEquals(hand.Slot?.ActiveUserRoot, root))
            return false;
        var held = hand.GrabbedObjects;
        for (int i = 0; i < held.Count; i++)
        {
            if (ReferenceEquals(held[i], grabbable))
                return true;
        }
        return false;
    }

    // The socket is the carrier's own and names something this slot is part of as what it wears.
    private bool Wears(AvatarSocket socket, UserRoot root)
    {
        if (!ReferenceEquals(socket.Slot?.ActiveUserRoot, root) || socket.Equipped.Target is not Component { IsDestroyed: false } worn)
            return false;
        var wornSlot = worn.Slot;
        return wornSlot != null && (ReferenceEquals(wornSlot, Slot) || Slot.IsDescendantOf(wornSlot));
    }

    private void Apply(UserRoot root, Slot target, OverrideMode mode)
    {
        switch (mode)
        {
            case OverrideMode.View:
            case OverrideMode.Root:
                // A synced ref cannot name a slot that exists on one machine only.
                if (target.IsLocalElement)
                    return;
                (mode == OverrideMode.View ? root.ViewOverride : root.RootOverride).Target = target;
                break;
            case OverrideMode.Ears:
                HearFrom(root, target);
                break;
        }
        _root = root;
        _target = target;
        _mode = mode;
        target.OnPrepareDestroy += _targetGone;
    }

    // An ear of the user's own on a slot only this machine has, the head's ear kept quiet here meanwhile, and
    // the user's own voice left out of it the way it is left out of the head's.
    private void HearFrom(UserRoot root, Slot target)
    {
        var ear = target.AddLocalSlot("Ears").AttachComponent<AudioListener>();
        ear.ActiveUser.Target = World.LocalUser;
        var head = root.PrimaryListener;
        if (head != null)
        {
            foreach (var effect in head.Effects)
                ear.Effects.Add(effect);
            head.SuspendedHere = true;
            _quieted = head;
        }
        root.VoiceOutput?.SetLocallyExcluded(ear, true);
        _ear = ear;
    }

    private void Release()
    {
        var root = _root;
        var target = _target;
        var mode = _mode;
        _root = null;
        _target = null;
        if (target != null)
            target.OnPrepareDestroy -= _targetGone;

        bool live = root != null && !root.IsDestroyed && World is { IsDisposed: false, IsDestroyed: false };
        var quieted = _quieted;
        _quieted = null;
        if (quieted is { IsDestroyed: false })
            quieted.SuspendedHere = false;
        var ear = _ear;
        _ear = null;
        if (ear != null)
        {
            if (live)
                root!.VoiceOutput?.SetLocallyExcluded(ear, false);
            if (ear.Slot is { IsDestroyed: false } earSlot)
                earSlot.Destroy();
        }

        if (!live || mode == OverrideMode.Ears)
            return;
        // Cleared only while it is still this one's: something else may have taken the view since.
        var slotRef = mode == OverrideMode.View ? root!.ViewOverride : root!.RootOverride;
        if (ReferenceEquals(slotRef.RawTarget, target))
            slotRef.Target = null!;
    }

    private void OnTargetGone(Slot _)
    {
        Release();
        MarkChangeDirty();
    }

    private void WatchFocus(User? user)
    {
        if (ReferenceEquals(user, _presence))
            return;
        if (_presence != null)
            _presence.IsPresent.Changed -= _focusChanged;
        _presence = user;
        if (user != null)
            user.IsPresent.Changed += _focusChanged;
    }
}
