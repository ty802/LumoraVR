// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Seconds this world has run on the machine running the script. It pauses with the world.
[Node("Time/World Time")]
public sealed class WorldTime : ReadNode
{
    [return: Port("seconds")]
    public static double Run(ScriptContext ctx) => ctx.World.Time.TotalTime;
}
