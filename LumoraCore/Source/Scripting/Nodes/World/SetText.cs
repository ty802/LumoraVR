// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Script;
using HelioText = Helio.UI.Text;

namespace Lumora.Core.Scripting;

// The words a slot shows: its text renderer's, else its UI text's.
[Node("World/Components/Set Text", Capability.WorldWrite)]
public sealed class SetText : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, string? text)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return;
        text ??= string.Empty;
        if (slot!.GetComponent<TextRenderer>() is { IsDestroyed: false } renderer)
            ScriptWrites.Write(ctx, renderer.Text, text);
        else if (slot.GetComponent<HelioText>() is { IsDestroyed: false } label)
            ScriptWrites.Write(ctx, label.Content, text);
    }
}
