// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// The curves Ease and Tween Field run along. Linear is no easing at all. Smooth and Smoother are the smooth
// steps, already eased at both ends. Back pulls past its start and Elastic springs past its end.
public enum EaseCurve
{
    Linear,
    Smooth,
    Smoother,
    Sine,
    Quadratic,
    Cubic,
    Quartic,
    Quintic,
    Exponential,
    Circular,
    Back,
    Elastic,
    Bounce,
}
