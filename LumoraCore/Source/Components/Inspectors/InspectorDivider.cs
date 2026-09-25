// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using Helio.UI;

namespace Lumora.Core.Components;

// The bar between the hierarchy and the component list. LocalPoint is canvas space, so measuring the drag
// against the press point stays steady while the bar itself moves under the pointer.
[ComponentCategory("Hidden")]
public sealed class InspectorDivider : InteractionElement
{
    public readonly SyncRef<SceneInspectorPanel> Panel = new();
    private float _startX, _startWidth;

    protected override void OnPress(in UIInteractionContext context)
    {
        _startX = context.LocalPoint.x;
        _startWidth = Panel.Target?.HierarchyWidth.Value ?? SceneInspectorPanel.DefaultHierarchyWidth;
    }

    protected override void OnDrag(in UIInteractionContext context)
        => Panel.Target?.SetHierarchyWidth(_startWidth + context.LocalPoint.x - _startX);
}
