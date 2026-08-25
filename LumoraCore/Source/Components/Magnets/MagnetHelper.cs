// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Shared maths and rigging for the magnet system: candidate search, orientation building, and the
// one-call setup that turns a plain slot into something that snaps.
public static class MagnetHelper
{
    // Rotation whose local +Z points along forward and whose +Y leans toward up.
    //
    // floatQ.LookRotation fills its matrix from ROWS, so what it returns is the inverse of the
    // rotation that actually aims +Z down 'forward'. For a unit quaternion the conjugate is exactly
    // the transposed basis, so inverting it is the correct construction and not a fudge factor.
    // Every facing in this system goes through here so nobody rediscovers that the hard way. -xlinka
    public static floatQ Facing(in float3 forward, in float3 up)
    {
        var f = forward;
        if (f.LengthSquared < 1e-10f)
            f = float3.Forward;
        f = f.Normalized;

        var u = up;
        if (u.LengthSquared < 1e-10f)
            u = float3.Up;
        u = u.Normalized;

        // Parallel forward and up leave no basis to build from; swing up onto another axis.
        if (MathF.Abs(float3.Dot(f, u)) > 0.9999f)
            u = MathF.Abs(f.y) > 0.9f ? float3.Forward : float3.Up;

        return floatQ.LookRotation(f, u).Inverse;
    }

    // In degrees.
    public static float AngleDegrees(in floatQ a, in floatQ b)
    {
        float dot = MathF.Abs(floatQ.Dot(a, b));
        if (dot > 1f)
            dot = 1f;
        return 2f * MathF.Acos(dot) * (180f / MathF.PI);
    }

    // Every socket that would take this item right now, unsorted. A non-empty
    // Magnet.SocketWhitelist replaces the world scan outright, so a linked pair finds its partner
    // without caring what else is nearby.
    public static int CollectSockets(
        Magnet magnet,
        in float3 worldPoint,
        in floatQ worldRotation,
        List<MagnetSocket> results,
        bool autoAttachOnly = false)
    {
        results.Clear();
        if (magnet == null || magnet.IsDestroyed || magnet.Slot == null)
            return 0;

        if (magnet.SocketWhitelist.Count > 0)
        {
            foreach (var socket in magnet.SocketWhitelist)
            {
                if (socket == null || (autoAttachOnly && !socket.AutoAttach.Value))
                    continue;
                if (socket.CanAttach(magnet, in worldPoint, in worldRotation))
                    results.Add(socket);
            }
            return results.Count;
        }

        var registry = MagnetRegistry.For(magnet.World);
        if (registry == null)
            return 0;

        var sockets = registry.Sockets;
        for (int i = 0; i < sockets.Count; i++)
        {
            var socket = sockets[i];
            if (autoAttachOnly && !socket.AutoAttach.Value)
                continue;
            if (socket.CanAttach(magnet, in worldPoint, in worldRotation))
                results.Add(socket);
        }
        return results.Count;
    }

    // Null when nothing would take it.
    public static MagnetSocket? FindBestSocket(
        Magnet magnet,
        in float3 worldPoint,
        in floatQ worldRotation,
        bool autoAttachOnly = false)
    {
        var candidates = new List<MagnetSocket>();
        if (CollectSockets(magnet, in worldPoint, in worldRotation, candidates, autoAttachOnly) == 0)
            return null;

        MagnetSocket? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            float distance = candidates[i].DistanceTo(in worldPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidates[i];
            }
        }
        return best;
    }

    // False when nothing is in reach. A guide is only in play when the point sits inside its own
    // range plus the item's reach.
    public static bool TryConstrain(
        World? world,
        in float3 worldPoint,
        in floatQ worldRotation,
        float extraRange,
        out float3 position,
        out floatQ rotation,
        out MagnetGuide? guide)
    {
        position = worldPoint;
        rotation = worldRotation;
        guide = null;

        var registry = MagnetRegistry.For(world);
        if (registry == null)
            return false;

        float bestDistance = float.MaxValue;
        var guides = registry.Guides;
        for (int i = 0; i < guides.Count; i++)
        {
            var candidate = guides[i];
            if (!candidate.IsUsable)
                continue;
            if (!candidate.Constrain(in worldPoint, in worldRotation, out var snapped, out var aligned))
                continue;

            float reach = candidate.WorldRange + extraRange;
            float distance = float3.Distance(worldPoint, snapped);
            if (distance > reach || distance >= bestDistance)
                continue;

            bestDistance = distance;
            position = snapped;
            rotation = candidate.AlignRotation.Value ? aligned : worldRotation;
            guide = candidate;
        }
        return guide != null;
    }

    // Anything that reparents world objects on its own should ask first and leave a socketed item
    // where it is.
    public static bool IsSocketed(Slot? slot)
    {
        if (slot == null || slot.IsDestroyed)
            return false;
        var magnet = slot.GetComponent<Magnet>();
        if (magnet == null)
            return false;
        return magnet.AttachedSocket != null;
    }

    // Turn a plain slot into a snapping pair: a socket slot takes the object's place in the
    // hierarchy and the object moves inside it at identity, whitelisted to each other. Grab it,
    // carry it off, let go anywhere near home and it drops back into exactly the pose it started in.
    //
    // The socket has to be a NEW parent rather than a component on the object itself: a socket that
    // lived on the object would move with the object, and "put it back where it was" needs something
    // that stays behind. -xlinka
    public static MagnetSocket? MakeSocketable(Slot slot)
    {
        if (slot == null || slot.IsDestroyed || slot.IsRootSlot)
            return null;
        var parent = slot.Parent;
        if (parent == null)
            return null;

        var socketSlot = parent.AddSlot($"{slot.SlotName.Value} Socket");
        socketSlot.CopyTransformFrom(slot);

        var socket = socketSlot.AttachComponent<MagnetSocket>();
        socket.DirectOnly.Value = true;
        // MaxDistance is read in the socket's own scale, and the socket just inherited the object's
        // scale, so the default has to be converted or a shrunken object gets a huge reach.
        socket.MaxDistance.Value = socketSlot.GlobalScaleToLocal(socket.MaxDistance.Value);

        slot.SetParent(socketSlot, preserveGlobalTransform: false);
        slot.LocalPosition.Value = float3.Zero;
        slot.LocalRotation.Value = floatQ.Identity;
        slot.LocalScale.Value = float3.One;

        var magnet = slot.GetComponent<Magnet>() ?? slot.AttachComponent<Magnet>();
        var grabbable = slot.GetComponent<Grabbable>();
        if (grabbable == null)
        {
            grabbable = slot.AttachComponent<Grabbable>();
            grabbable.Scalable.Value = true;
        }

        magnet.SocketWhitelist.Add(socket);
        socket.MagnetWhitelist.Add(magnet);
        socket.Claim(magnet);
        return socket;
    }
}
