// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components;

// The texture a hand means when it lets go over a texture field or presses one: the one rule a shader card's
// texture slot and a material panel's texture row both go by. A reference card means the texture it points
// at; anything else held means the texture it carries itself, the way an imported picture carries its own.
// A card is spent once its texture is taken; a held picture stays in the hand. -xlinka
public static class HeldTexture
{
    // Offers what the hand holds to a texture field, first fit first. Take puts the texture in and records
    // the undo step; false passes on to the next thing held.
    public static bool Offer(IReadOnlyList<IGrabbable> held, Grabber? grabber, Func<IAssetProvider<TextureAsset>, bool> take)
    {
        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not Component { IsDestroyed: false } component || component.Slot is not { IsDestroyed: false } slot)
                continue;
            // A held object counts only what is on it, not under it: a panel keeps its theme's sprite just
            // under it, and letting go of a panel over a texture row is not handing over that sprite.
            var card = slot.GetComponent<ReferenceProxy>();
            var texture = card != null ? In(card.Target.Target) : OnSlot(slot);
            if (texture == null || !take(texture))
                continue;
            card?.Consume(held[i], grabber);
            return true;
        }
        return false;
    }

    // A press on a texture field offers it what the presser holds, hand by hand, as letting go over it does.
    // A hand carrying on its beam never presses a field; this is the press of a hand that took hold another
    // way.
    public static bool OfferHeldBy(User? actor, IProxyReceiver receiver)
    {
        var root = actor?.Root?.Slot;
        if (root == null)
            return false;
        foreach (var grabber in root.GetComponentsInChildren<Grabber>())
        {
            // A taken card leaves the hand, so the hand's list is copied first.
            if (grabber.IsHoldingObjects && receiver.TryReceiveProxy(new List<IGrabbable>(grabber.GrabbedObjects), grabber))
                return true;
        }
        return false;
    }

    // The texture a card's target stands for: itself, what a texture field holds, or one on a slot or just
    // under it, where a picture built in parts keeps its own.
    public static IAssetProvider<TextureAsset>? In(IWorldElement? element)
    {
        if (element == null || element.IsDestroyed)
            return null;
        if (element is IAssetProvider<TextureAsset> texture)
            return texture;
        if (element is ISyncRef reference)
            return reference.Target is IAssetProvider<TextureAsset> { IsDestroyed: false } target ? target : null;
        if (element is not Slot slot)
            return null;
        if (OnSlot(slot) is { } own)
            return own;
        foreach (var child in slot.Children)
        {
            if (OnSlot(child) is { } part)
                return part;
        }
        return null;
    }

    private static IAssetProvider<TextureAsset>? OnSlot(Slot slot)
    {
        foreach (var component in slot.Components)
        {
            if (component is IAssetProvider<TextureAsset> texture && !component.IsDestroyed)
                return texture;
        }
        return null;
    }
}
