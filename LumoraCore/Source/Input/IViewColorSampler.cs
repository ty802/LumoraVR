// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Input;

// Reads the colour the LOCAL VIEW is actually showing at a world point, injected by the platform layer
// at startup the way the clipboard service is. The engine has no renderer of its own, so this is the
// only way to answer "what colour is that" for something the datamodel cannot name - a skybox, a
// particle, a texel of an imported texture.
//
// One call per user gesture, never per frame: the implementation is expected to project the point
// through the active camera and pull the viewport image back off the GPU, which is a stall. A platform
// with no view (headless) leaves InputInterface.ViewColorSampler null, and every caller has to cope
// with that rather than assume. -xlinka
public interface IViewColorSampler
{
    bool TrySample(float3 worldPoint, out colorHDR color);
}
