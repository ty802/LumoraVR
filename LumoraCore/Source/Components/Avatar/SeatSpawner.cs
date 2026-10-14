// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Touch;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Sit anywhere along a bench. A touch on this surface puts a copy of the template seat where it landed,
// pressed flat onto a plane, and seats the toucher in it; the copy goes away once they stand up (SeatClaim).
//
// A seat holds one user, so a bench that takes a crowd is a seat per person, made on demand. The copy is
// made by the authority, which is the only peer that may add to the world's own content, and sitting in it
// is left to the toucher's peer, the only one that may move their body. -xlinka
[ComponentCategory("Users/Avatar")]
public class SeatSpawner : TouchControl
{
    // Copied per sitter. Usually kept switched off; the copy is switched on.
    public readonly SyncRef<Seat> Template;

    // Where the copies go.
    public readonly SyncRef<Slot> SeatsRoot;

    // Below zero means no limit.
    public readonly Sync<int> MaxUsers;

    // No new place closer than this to one already taken.
    public readonly Sync<float> MinDistance;

    // The plane a touch is pressed onto runs through this slot; empty means this one.
    public readonly SyncRef<Slot> PlaneSpace;

    // In the plane slot's space: the plane's normal, and the copy's up.
    public readonly Sync<float3> PlaneNormal;

    public SeatSpawner()
    {
        Template = new SyncRef<Seat>(this);
        SeatsRoot = new SyncRef<Slot>(this);
        MaxUsers = new Sync<int>(this, -1);
        MinDistance = new Sync<float>(this, 0.25f);
        PlaneSpace = new SyncRef<Slot>(this);
        PlaneNormal = new Sync<float3>(this, float3.Up);
        AcceptFingertipTouch.Value = false;
        ContactHaptics.Value = TouchHaptics.Strong;
    }

    protected override string? PointerLabel => "Sit";

    protected override void OnTouchContact(in TouchContact contact)
    {
        if (contact.Contact != TouchPhase.Begin)
            return;

        var world = World;
        var user = contact.User;
        var template = Template.Target;
        var root = SeatsRoot.Target;
        var slot = Slot;
        if (world == null || !world.IsAuthority || user == null || slot == null
            || template == null || template.IsDestroyed || root == null || root.IsDestroyed)
            return;

        // Already sitting on this bench.
        if (user.Root?.Slot is { } rig && rig.IsDescendantOf(root))
            return;
        if (root.IsDescendantOf(template.Slot))
            return;

        var plane = PlaneSpace.Target is { IsDestroyed: false } space ? space : slot;
        var normal = PlaneNormal.Value;
        normal = normal.LengthSquared > 1e-8f ? normal.Normalized : float3.Up;
        var local = plane.GlobalPointToLocal(contact.Point);
        local -= normal * float3.Dot(local, normal);
        var point = plane.LocalPointToGlobal(local);
        var rotation = plane.GlobalRotation * UpTo(normal);

        // Only a seat somebody is in counts against the limit and the spacing. A copy still waiting for its
        // sitter keeps nobody else off the bench; one waiting for this same toucher means they are on the way.
        int taken = 0;
        float spacing = MinDistance.Value;
        foreach (var child in root.Children)
        {
            if (child == null || child.IsDestroyed || child.GetComponent<Seat>() is not { } placed)
                continue;
            if (!placed.IsOccupied)
            {
                if (child.GetComponent<SeatClaim>()?.Claimant.Target == user)
                    return;
                continue;
            }
            taken++;
            if (spacing > 0f && (child.GlobalPosition - point).Length < spacing)
                return;
        }
        if (MaxUsers.Value >= 0 && taken >= MaxUsers.Value)
            return;

        Slot copy;
        try
        {
            copy = template.Slot.Duplicate(root);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"SeatSpawner on {ParentHierarchyToString()} could not copy its seat: {ex.Message}");
            return;
        }
        if (copy == null || copy.IsDestroyed)
            return;

        copy.ActiveSelf.Value = true;
        copy.Persistent.Value = false;
        copy.GlobalPosition = point;
        copy.GlobalRotation = rotation;

        var seat = copy.GetComponent<Seat>();
        if (seat == null)
        {
            copy.Destroy();
            return;
        }
        seat.AllowedUsers.Clear();
        seat.AllowedUsers.Add(user);
        copy.AttachComponent<SeatClaim>().Claimant.Target = user;
        PulseContact(in contact);
    }

    // The turn that takes up onto the given direction.
    private static floatQ UpTo(in float3 normal)
    {
        float dot = float3.Dot(float3.Up, normal);
        if (dot > 0.9999f)
            return floatQ.Identity;
        if (dot < -0.9999f)
            return floatQ.AxisAngle(float3.Forward, MathF.PI);
        return floatQ.AxisAngle(float3.Cross(float3.Up, normal), MathF.Acos(dot));
    }
}
