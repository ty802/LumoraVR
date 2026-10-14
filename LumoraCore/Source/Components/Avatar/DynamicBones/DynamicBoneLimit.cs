// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// How far a chain bone may turn from its rest direction under its parent. Cone: any way, up to the angle.
// Hinge: only about the bone's own X axis. Polar: about X up to the angle and about Z up to the side angle.
public enum DynamicBoneLimit
{
    None,
    Cone,
    Hinge,
    Polar,
}
