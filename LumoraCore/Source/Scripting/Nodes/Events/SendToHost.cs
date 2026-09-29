// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Tells this author's Shared scripts on target, on the host: each hears On Message, with who sent it
// when it comes from a Local script on someone's machine. Messages sent from one machine in the same
// frame arrive as the last of them.
[Node("Events/Send To Host", Capability.Events)]
public sealed class SendToHost : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? target, string? message, float value = 0f)
    {
        if (!ScriptReads.Visible(ctx, target) || target!.IsLocalElement)
            return;
        ctx.CountMessage();
        string text = ScriptMessages.Text(message);
        var world = ctx.World;
        if (world.IsAuthority)
            ScriptMessages.ToHost(ctx.Principal, target, text, value, ctx.IsLocal ? world.LocalUser : null);
        else if (ctx.Host is { } sender && world.LocalUser is { } viewer)
        {
            // This machine's relay belongs to whoever is here, so it is written as them; the host checks the
            // sender's author against the listener's.
            using (world.DataModelPermissions?.EnterActor(viewer))
                ScriptPressRelay.For(world)?.PublishMessage(sender, target, text, value);
        }
    }
}
