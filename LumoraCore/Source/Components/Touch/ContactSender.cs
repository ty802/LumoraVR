// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A shape that contact receivers feel: on an avatar a fingertip, a foot, a nose, a marker some other part of the same
// avatar listens for; in a world anything that should set off a receiver. It carries tags and nothing else.
[ComponentCategory("Interaction/Touch")]
public class ContactSender : ContactVolume, IPlayerColliderSource
{
    // Stands for part of the wearer's body (head, torso, a hand, a foot, a finger). An avatar carrying any of these
    // brings its own body; the generic one every user has otherwise stays out.
    public readonly Sync<bool> Body;

    // Also pushes other people's bone chains, the way a hand does.
    public readonly Sync<bool> PushesBones;

    private ContactManager? _manager;
    private DynamicBoneManager? _bones;

    public ContactSender()
    {
        Body = new Sync<bool>(this, false);
        PushesBones = new Sync<bool>(this, false);
    }

    public override void OnStart()
    {
        base.OnStart();
        _manager = ContactManager.For(World);
        _manager?.Register(this);
        _bones = DynamicBoneManager.For(World);
        _bones?.Register(this);
    }

    public override void OnDestroy()
    {
        _manager?.Unregister(this);
        _manager = null;
        _bones?.Unregister(this);
        _bones = null;
        base.OnDestroy();
    }

    internal void Gather(ContactManager manager, ulong frame, float delta)
    {
        var owner = Owner(frame, out bool onAvatar);
        if (!Live(manager.World, owner) || !Shape(out var a, out var b, out float radius))
            return;
        var velocity = Velocity((a + b) * 0.5f, frame, delta);
        manager.AddShape(ContactShape.Make(a, b, radius, TagIds, TagMask, owner, onAvatar, velocity));
    }

    public void Contribute(DynamicBoneManager manager)
    {
        if (!PushesBones.Value || IsDestroyed)
            return;
        var owner = Owner(World?.Time.UpdateIndex ?? 0, out _);
        if (World == null || !Live(World, owner) || !Shape(out var a, out var b, out float radius))
            return;
        manager.AddPlayerCollider(a, b, radius, owner as User);
    }
}
