// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components.Touch;

// The plainest touchable surface: two synced flags and the events that go with them. No press
// semantics, no latch, no travel - just "is a hand on this right now".
//
// It exists so a world author can wire touch into anything without writing a component: drive a
// colour off Touching, gate an emitter off Hovering, count contacts. Everything with actual button
// behaviour builds on top of the same probe events, not on top of this. -xlinka
[ComponentCategory("Interaction/Touch")]
public class TouchPad : TouchControl
{
    public readonly Sync<bool> Hovering;

    public readonly Sync<bool> Touching;

    // Metres the touching tip is currently past the surface. Zero when nothing is touching.
    public readonly Sync<float> ContactDepth;

    public TouchPad()
    {
        Hovering = new Sync<bool>(this, false);
        Touching = new Sync<bool>(this, false);
        ContactDepth = new Sync<float>(this, 0f);
    }

    // Fired on the toucher's client when a probe arrives.
    public event Action<TouchPad, TouchContact>? HoverEntered;

    // Fired on the toucher's client when the probe leaves.
    public event Action<TouchPad, TouchContact>? HoverExited;

    // Fired on the toucher's client the frame contact starts.
    public event Action<TouchPad, TouchContact>? TouchBegan;

    // Fired on the toucher's client every frame contact continues.
    public event Action<TouchPad, TouchContact>? TouchStayed;

    // Fired on the toucher's client the frame contact ends.
    public event Action<TouchPad, TouchContact>? TouchEnded;

    protected override void OnTouchContact(in TouchContact contact)
    {
        switch (contact.Hover)
        {
            case TouchPhase.Begin:
                Hovering.Value = true;
                PulseHover(in contact);
                HoverEntered?.Invoke(this, contact);
                break;

            case TouchPhase.End:
                Hovering.Value = false;
                HoverExited?.Invoke(this, contact);
                break;
        }

        switch (contact.Contact)
        {
            case TouchPhase.Begin:
                Touching.Value = true;
                ContactDepth.Value = contact.Penetration;
                PulseContact(in contact);
                TouchBegan?.Invoke(this, contact);
                break;

            case TouchPhase.Stay:
                Touching.Value = true;
                ContactDepth.Value = contact.Penetration;
                TouchStayed?.Invoke(this, contact);
                break;

            case TouchPhase.End:
                Touching.Value = false;
                ContactDepth.Value = 0f;
                PulseContact(in contact);
                TouchEnded?.Invoke(this, contact);
                break;
        }
    }
}
