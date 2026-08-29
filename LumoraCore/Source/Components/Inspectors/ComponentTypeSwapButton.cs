// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

// press relay for the "Swap Type" row: opens the component browser as a type picker for this
// component. the picker does the swap, so nothing here mutates the world.
[ComponentCategory("Utility/Inspectors")]
public class ComponentTypeSwapButton : Component
{
    public readonly SyncRef<Component> Target;

    public ComponentTypeSwapButton()
    {
        Target = new SyncRef<Component>(this);
    }

    [SyncMethod]
    public void OnPressed(Button button, UIInteractionContext context)
    {
        var target = Target.Target;
        if (target == null || target.IsDestroyed || target.Slot == null)
            return;

        // The picker is a full panel, so it wants the panel's own slot to place itself against, not
        // the button's - a button lives in canvas space where the scale means something else.
        var anchor = Slot.GetComponentInParents<UI.PanelShell>()?.Slot ?? target.Slot;
        var owner = Slot.GetComponentInParents<SceneInspectorPanel>();
        ComponentSelectorPanel.SpawnForSwap(anchor, target, owner);
    }
}
