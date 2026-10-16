// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Physics;

// The platform side of a world's simulated bodies, run once a frame before scripts and components: the bodies
// still moving hand their last physics step to the data model and are drawn between steps.
public interface IPhysicsStepHook
{
    void StepBodies();
}
