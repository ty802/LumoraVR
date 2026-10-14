// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A shape that feels contact senders carrying any of its tags, and reports it in Value: whether one touches it, the
// frame one arrives, or how deep the nearest one reaches. Senders on the same avatar count only with AllowSelf,
// everyone else's only with AllowOthers.
//
// Value and Touching are worked out on every peer each frame and written locally, never sent; drive things from
// them like any field. -xlinka
[ComponentCategory("Interaction/Touch")]
[DefaultUpdateOrder(2100)]
public class ContactReceiver : ContactVolume
{
    public readonly Sync<ContactReceiverMode> Mode;
    public readonly Sync<bool> AllowSelf;
    public readonly Sync<bool> AllowOthers;

    // Which senders count by where they are: on avatars, and on anything else in the world.
    public readonly Sync<bool> FromAvatars;
    public readonly Sync<bool> FromWorld;

    // Enter only: metres a second the sender must arrive at.
    public readonly Sync<float> MinVelocity;

    // Another receiver this one also reports into: that one's Value becomes the larger of the two.
    public readonly SyncRef<ContactReceiver> ReportTo;

    [NonPersistent]
    public readonly Sync<float> Value;

    [NonPersistent]
    public readonly Sync<bool> Touching;

    private ContactManager? _manager;

    internal float Raw;
    internal bool RawTouching;
    internal bool WasTouching;

    public ContactReceiver()
    {
        Mode = new Sync<ContactReceiverMode>(this, ContactReceiverMode.Constant);
        AllowSelf = new Sync<bool>(this, true);
        AllowOthers = new Sync<bool>(this, true);
        FromAvatars = new Sync<bool>(this, true);
        FromWorld = new Sync<bool>(this, true);
        MinVelocity = new Sync<float>(this, 0.05f);
        ReportTo = new SyncRef<ContactReceiver>(this);
        Value = new Sync<float>(this, 0f);
        Touching = new Sync<bool>(this, false);
    }

    public override void OnStart()
    {
        base.OnStart();
        _manager = ContactManager.For(World);
        _manager?.Register(this);
    }

    public override void OnDestroy()
    {
        _manager?.Unregister(this);
        _manager = null;
        base.OnDestroy();
    }

    // The first receiver to update in a frame runs the whole world's contacts.
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (IsDestroyed)
            return;
        _manager ??= ContactManager.For(World);
        _manager?.Tick();
    }

    // Switched off, it reads nothing at once, whether or not any other receiver is left to run the pass.
    public override void OnDisabled()
    {
        base.OnDisabled();
        WasTouching = false;
        if (IsDestroyed || World == null)
            return;
        using var bypass = World?.DataModelPermissions?.EnterSystemBypass();
        Publish(0f, false);
    }

    internal void Publish(float value, bool touching)
    {
        Value.SetValueSilently(value);
        Touching.SetValueSilently(touching);
    }
}
