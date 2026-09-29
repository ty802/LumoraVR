// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Scripting;

// What a script may look at. A slot or user from another world, one already gone, or a slot local to
// the machine running the script reads as nothing: a script on the host is not the host. -xlinka
internal static class ScriptReads
{
    // A Local script also sees the local slots under its own object: the ones it made on this machine.
    public static bool Visible(ScriptContext ctx, Slot? slot)
        => slot != null && !slot.IsDestroyed && ReferenceEquals(slot.World, ctx.World)
            && (!slot.IsLocalElement || (ctx.IsLocal && ctx.Self != null && slot.IsDescendantOf(ctx.Self)));

    public static bool Visible(ScriptContext ctx, User? user)
        => user != null && !user.IsDestroyed && ReferenceEquals(user.World, ctx.World);

    public static bool Visible(ScriptContext ctx, Component? component)
        => component != null && !component.IsDestroyed && Visible(ctx, component.Slot);

    // Any world object a script is handed, by what holds it: a slot or user as themselves, a component by
    // its slot, a member by the object it belongs to.
    public static bool VisibleElement(ScriptContext ctx, IWorldElement? element)
    {
        for (int depth = 0; element != null && depth < 16; depth++)
        {
            switch (element)
            {
                case Slot slot:
                    return Visible(ctx, slot);
                case User user:
                    return Visible(ctx, user);
                case Component component:
                    return Visible(ctx, component);
                case SyncElement member:
                    if (member.IsDestroyed || !ReferenceEquals(member.World, ctx.World))
                        return false;
                    element = member.Parent;
                    break;
                default:
                    return false;
            }
        }
        return false;
    }

    // A reference field whose target a script may read: one it can see, and not host-only.
    public static bool Readable(ScriptContext ctx, ISyncRef? reference)
        => reference is SyncElement member && VisibleElement(ctx, member)
            && member is not ConflictingSyncElement { IsHostOnly: true };

    // Part of someone's body: under a user root, and not something they are holding. A person is found
    // once, by their root, instead of once per limb.
    public static bool IsWornPart(Slot slot)
    {
        var root = slot.ActiveUserRoot?.Slot;
        if (root == null)
            return false;
        for (var current = slot; current != null && !ReferenceEquals(current, root); current = current.Parent)
        {
            if (current.GetComponent<Grabbable>() is { IsGrabbed: true })
                return false;
        }
        return true;
    }

    // A user's root as any machine can find it. User.Root is filled in on the host and for the local user
    // only; the replicated reference is there everywhere.
    public static UserRoot? RootOf(User? user)
    {
        if (user == null || user.IsDestroyed)
            return null;
        var root = user.Root ?? user.UserRootRef.Target;
        return root is { IsDestroyed: false } ? root : null;
    }

    // The user whose user root this slot is.
    public static User? BodyOf(Slot? slot)
        => slot?.ActiveUserRoot is { } root && ReferenceEquals(root.Slot, slot) && root.ActiveUser is { IsDestroyed: false } user ? user : null;

    // The users in the session, the host included.
    public static List<User> Users(ScriptContext ctx)
    {
        var users = ctx.UserScratch;
        ctx.World.GetAllUsers(users);
        users.RemoveAll(user => user.IsDestroyed);
        return users;
    }
}
