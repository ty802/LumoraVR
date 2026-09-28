// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Whether the person here is in a headset rather than at a desktop.
[Node("Input/In VR", Capability.Input)]
public sealed class InVR : ReadNode
{
    [return: Port("vr")]
    public static bool Run(ScriptContext ctx) => ScriptInput.Of(ctx)?.IsVRActive ?? false;
}
