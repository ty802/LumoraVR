// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// How the IK walks an avatar's legs. Auto reads the rig: a leg with a long raised metatarsal between the shin
// and the toes stands on its toes, anything else on its heels. Saved by name, so members may be reordered but
// never renamed.
public enum LegMode
{
    Auto,
    Plantigrade,
    Digitigrade,
}
