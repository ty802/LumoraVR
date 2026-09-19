// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Serves one of the named shapes from HandPosePresets. Only the shape replicates; the pose itself is
// the same static data on every peer. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPosePreset : Component, IHandPoseSourceComponent
{
    public readonly Sync<HandPoseShape> Shape = new();

    private StaticHandPose Current => HandPosePresets.Get(Shape.Value);

    public bool TracksMetacarpals => Current.TracksMetacarpals;

    public bool IsHandTracked(Chirality side) => Current.IsHandTracked(side);

    public bool TryGetFingerPosition(BodyNode node, out float3 handLocalPosition)
        => Current.TryGetFingerPosition(node, out handLocalPosition);

    public bool TryGetFingerRotation(BodyNode node, out floatQ handLocalRotation)
        => Current.TryGetFingerRotation(node, out handLocalRotation);
}
