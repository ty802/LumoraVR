// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The first player on the slot: a video, a sound or an animation. From the start.
[Node("Media/Play", Capability.WorldWrite)]
public sealed class PlayMedia : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot) => ScriptMedia.Control(ctx, slot, static playable => playable.Play());
}
