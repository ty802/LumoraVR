// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Always-eligible fallback. Used when permissions deny everything else or
// the user manually cycles through to "no locomotion". - xlinka
public class NullLocomotionModule : LocomotionModule
{
    public override string DisplayName => "None";
    public override void OnModuleUpdate(float delta) { }
}
