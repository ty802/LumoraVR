// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;

namespace Lumora.Core.Components.UI;

// Holds the pending dialog's state and owns every action its widgets are bound to.
//
// A Helio button carries ONE press action and it is a SyncDelegate: a world element plus a method
// name. A closure cannot be named that way, so the house rule is that a UI action is always a method
// on a component and the component carries whatever the method needs to know. That is this. The
// buttons all bind the SAME method and the relay works out which one fired by looking it up in the
// list it built them from - no per-button component, no captured index. -xlinka
//
// It lives on the panel slot, which is rebuilt per dialog and never persisted.
[ComponentCategory("Hidden")]
public sealed class ModalDialogRelay : Component
{
    internal ModalHost? Host;
    internal readonly List<Button> Options = new();
    internal TextInput? PromptInput;

    public string PromptText => PromptInput != null && !PromptInput.IsDestroyed
        ? PromptInput.Text.Value ?? string.Empty
        : string.Empty;

    [SyncMethod]
    public void OnOptionPressed(Button button, UIInteractionContext context)
    {
        if (Host == null || Host.IsDestroyed || button == null)
            return;
        int index = Options.IndexOf(button);
        if (index < 0)
            return;
        Host.Complete(index);
    }

    // The scrim swallows everything behind the dialog. It only dismisses when the press landed OUTSIDE
    // the panel: the panel absorbs its own background presses, but a press on a gap between rows can
    // still reach here, and closing the dialog because somebody missed a button by four pixels is the
    // kind of thing that loses work.
    [SyncMethod]
    public void OnScrimPressed(Button button, UIInteractionContext context)
    {
        if (Host == null || Host.IsDestroyed)
            return;
        var panel = Host.PanelSlot;
        if (panel != null && !panel.IsDestroyed)
        {
            var rect = panel.GetComponent<RectTransform>();
            if (rect != null && rect.LocalComputeRect.Contains(context.PointIn(panel)))
                return;
        }
        Host.Dismiss();
    }

    [SyncMethod]
    public void OnPromptSubmitted(TextInput input, string text)
    {
        if (Host == null || Host.IsDestroyed)
            return;
        Host.CompleteDefault();
    }
}
