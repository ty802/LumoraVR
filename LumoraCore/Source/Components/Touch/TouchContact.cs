// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// Transition of one touch channel (hover or contact) for a single frame.
public enum TouchPhase
{
    // Not engaged and was not engaged last frame.
    None,

    // First frame the channel is engaged.
    Begin,

    // Already engaged and still is.
    Stay,

    // Engaged last frame; released after this event.
    End,
}

// What kind of probe produced a contact. Targets opt in per kind, so a control can be
// fingertip-only (a physical panel switch), remote-only (a sign you click from across the room),
// or both.
public enum TouchProbeKind
{
    // A physical tip driven by a hand: it has to reach the surface.
    Fingertip,

    // A pointer beam standing in for a fingertip, so desktop users reach every control.
    Remote,
}

// One frame of touch state delivered to an ITouchTarget. Both channels are reported in the same
// event: Hover tracks whether the probe is aimed at the target at all, Contact whether it is
// actually pressing. A target can act on either.
public readonly struct TouchContact
{
    public readonly TouchPhase Hover;

    public readonly TouchPhase Contact;

    // World-space point on the target surface.
    public readonly float3 Point;

    // World-space surface normal at Point.
    public readonly float3 Normal;

    // World-space position of the probe tip itself, which may be past the surface.
    public readonly float3 Tip;

    // World-space direction the probe is pointing.
    public readonly float3 Direction;

    // Metres the tip has travelled past the surface along Direction. Zero while the tip is still
    // short of contact. A depressible control turns this into travel; a plain switch ignores it.
    public readonly float Penetration;

    // Null on a contact the authority rebuilt from a relayed touch: the probe is a component on the
    // toucher's own machine and does not exist here. Kind and Hand are carried as values for exactly
    // that reason.
    public readonly TouchProbe? Probe;

    // For targets that accept one kind and refuse the other.
    public readonly TouchProbeKind Kind;

    // For haptics and per-hand filtering.
    public readonly Input.Chirality Hand;

    // Null only if the probe outlived its owner.
    public readonly User? User;

    public TouchContact(
        TouchPhase hover,
        TouchPhase contact,
        in float3 point,
        in float3 normal,
        in float3 tip,
        in float3 direction,
        float penetration,
        TouchProbe probe,
        User? user)
    {
        Hover = hover;
        Contact = contact;
        Point = point;
        Normal = normal;
        Tip = tip;
        Direction = direction;
        Penetration = penetration;
        Probe = probe;
        Kind = probe?.Kind ?? TouchProbeKind.Fingertip;
        Hand = probe?.Hand.Value ?? Input.Chirality.None;
        User = user;
    }

    // The authority's rebuild of a touch relayed from another peer. The surface normal and the push
    // direction deliberately do not cross the seam: nothing in the control family reads them, and only
    // the toucher's own geometry can produce them, so they would double the record for nothing. -xlinka
    public TouchContact(
        TouchPhase hover,
        TouchPhase contact,
        in float3 point,
        in float3 tip,
        float penetration,
        TouchProbeKind kind,
        Input.Chirality hand,
        User? user)
    {
        Hover = hover;
        Contact = contact;
        Point = point;
        Normal = float3.Zero;
        Tip = tip;
        Direction = float3.Zero;
        Penetration = penetration;
        Probe = null;
        Kind = kind;
        Hand = hand;
        User = user;
    }

    public bool IsContacting => Contact == TouchPhase.Begin || Contact == TouchPhase.Stay;

    public bool IsHovering => Hover == TouchPhase.Begin || Hover == TouchPhase.Stay;

    public override string ToString()
        => $"{Kind} hover:{Hover} contact:{Contact} pen:{Penetration:0.###}m at {Point}";
}
