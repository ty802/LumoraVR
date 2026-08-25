// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Touch;

// A component that answers to touch. A probe resolves this by walking up from the collider slot it
// hit, so the collider, the mesh and the control can live on different slots of the same object.
//
// Every method here is asked on the TOUCHING user's client only. The target reacts by writing its
// own synced state, which replicates the normal way and is gated by the normal permission path -
// there is no separate touch authority. -xlinka
public interface ITouchTarget
{
    bool AcceptsFingertip { get; }

    bool AcceptsRemote { get; }

    // Whether contact still counts when the surface sits outside the toucher's view cone. Off by
    // default: reaching blindly behind you should not fire a control you cannot see.
    bool AcceptsOutOfSight { get; }

    // Final gate before a probe adopts this target: edit-mode restrictions, user filters, anything
    // the concrete control wants to refuse for. Asked every frame, so a control can go dead mid-touch.
    bool CanTouch(TouchProbe probe);

    // Called at most once per probe per frame, and always closed out with an End phase when the
    // probe leaves, is destroyed, or is disabled.
    void OnTouch(in TouchContact contact);
}
