// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Components;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;

namespace Lumora.Core.Scripting;

// How the grab and tool nodes find a person's hands: a hand is the HandTool rig under their user root,
// which carries its side, its grabber and the tool it holds, all replicated, so every machine answers the
// same. A side of None asks about either hand. -xlinka
internal static class ScriptHands
{
    // Found once per body and checked on the way out, so a read in an update does not walk an avatar.
    private static readonly ConditionalWeakTable<UserRoot, HandTool?[]> _found = new();

    public static HandTool? Hand(UserRoot? root, Chirality side)
    {
        var body = root?.Slot;
        if (root == null || body == null || body.IsDestroyed || side is not (Chirality.Left or Chirality.Right))
            return null;
        var cached = _found.GetValue(root, static _ => new HandTool?[2]);
        int index = side == Chirality.Left ? 0 : 1;
        var hand = cached[index];
        if (hand is { IsDestroyed: false } && hand.Side.Value == side && hand.Slot.IsDescendantOf(body))
            return hand;
        hand = null;
        foreach (var candidate in body.GetComponentsInChildren<HandTool>())
        {
            if (!candidate.IsDestroyed && candidate.Side.Value == side)
            {
                hand = candidate;
                break;
            }
        }
        cached[index] = hand;
        return hand;
    }

    public static HandTool? Hand(ScriptContext ctx, User? user, Chirality side)
    {
        if (!ScriptReads.Visible(ctx, user))
            return null;
        var root = ScriptReads.RootOf(user);
        var hand = Hand(root, side);
        return hand != null && ScriptReads.Visible(ctx, hand) ? hand : null;
    }

    // The hand rig a grabber belongs to, if it is one.
    public static HandTool? HandOf(Grabber? grabber)
        => grabber?.Slot?.GetComponentInParents<HandTool>() is { IsDestroyed: false } hand ? hand : null;

    // The grabbers of a hand, its owner's first. A machine that met someone's hand before the rig their own
    // machine built for it arrived builds one of its own beside it, and the hand may be holding on to that
    // one; the owner's is the one their grabs name. -xlinka
    public static void GrabbersOf(HandTool? hand, List<Grabber> into)
    {
        into.Clear();
        if (hand == null || hand.IsDestroyed)
            return;
        byte owner = hand.ReferenceID.GetUserByte();
        foreach (var child in hand.Slot.Children)
        {
            foreach (var grabber in child.GetComponents<Grabber>())
            {
                if (grabber.IsDestroyed)
                    continue;
                if (grabber.ReferenceID.GetUserByte() == owner)
                    into.Insert(0, grabber);
                else
                    into.Add(grabber);
            }
        }
    }

    public static Grabber? GrabberOf(HandTool? hand)
    {
        var found = new List<Grabber>(2);
        GrabbersOf(hand, found);
        return found.Count > 0 ? found[0] : null;
    }

    // The tool a hand has equipped: the replicated link, so a remote person's reads the same everywhere.
    public static ToolItem? ToolOf(HandTool? hand)
        => hand?.ActiveToolItem.Target is { IsDestroyed: false } item ? item : null;

    // What a slot is grabbed as: its own grabbable, else the nearest one above it, the way a hand finds
    // one from the collider it touched. A search block above the slot is a boundary another rig owns.
    public static IGrabbable? GrabbableAt(Slot? slot)
    {
        for (var current = slot; current != null && !current.IsRemoved; current = current.Parent)
        {
            if (!ReferenceEquals(current, slot) && current.GetComponent<SearchBlock>() != null)
                return null;
            foreach (var grabbable in current.GetComponentsImplementing<IGrabbable>())
            {
                if (grabbable is Component { IsDestroyed: false })
                    return grabbable;
            }
        }
        return null;
    }
}
