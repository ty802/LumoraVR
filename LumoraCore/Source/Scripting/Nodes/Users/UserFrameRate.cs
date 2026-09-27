// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The frame rate the user's own machine reports, whole frames, refreshed about twice a second.
[Node("Users/User FPS", Capability.UsersRead)]
public sealed class UserFrameRate : ReadNode
{
    [return: Port("fps")]
    public static float Run(ScriptContext ctx, User? user) => ScriptReads.Visible(ctx, user) ? user!.FPS.Value : 0f;
}
