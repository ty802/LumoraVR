// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Frames a second on the machine running the script, smoothed. User FPS reads anyone's.
[Node("Time/FPS")]
public sealed class FrameRate : ReadNode
{
    [return: Port("fps")]
    public static float Run(ScriptContext ctx) => ctx.World.Time.FramesPerSecond;
}
