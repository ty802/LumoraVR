// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// An object that defines its own in-hand pose for a TOUCH (physical/proximity) grab - reaching out and
// grabbing it directly, as opposed to pointing the laser at it. When a single alignable object is
// touch-grabbed, the hand tool snaps it to this pose so props/panels sit correctly in the hand instead of
// dangling at whatever offset they happened to be grabbed at. Implementors are Components. - xlinka
public interface IGrabAlignable
{
    // The desired LOCAL pose to snap to, expressed relative to the grabbed slot's parent (which, once
    // grabbed, is the grabber's holder slot). Return false to decline alignment and keep the grab offset.
    // - xlinka
    bool GetGrabAlignmentPose(Grabber grabber, out float3 alignedPosition, out floatQ alignedRotation, out float3 alignedScale);
}
