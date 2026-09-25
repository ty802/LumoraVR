// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The handle at the left of a component header. Press and drag moves the component within the list; the
// grab action on the same handle pulls a reference card instead, through the header's proxy source, which
// only answers inside this handle.
[ComponentCategory("Hidden")]
public sealed class InspectorComponentDrag : InteractionElement
{
    public readonly SyncRef<SceneInspectorPanel> Panel = new();
    public readonly SyncRef<Component> Target = new();
    // Canvas units squared. A press that never travels this far is a click and moves nothing.
    private const float DragStartSquared = 100f;
    private float2 _start;
    private bool _dragged;

    protected override void OnPress(in UIInteractionContext context)
    {
        _start = context.LocalPoint;
        _dragged = false;
    }

    protected override void OnDrag(in UIInteractionContext context)
    {
        _dragged |= (context.LocalPoint - _start).LengthSquared > DragStartSquared;
        if (_dragged)
            Panel.Target?.PreviewComponentDrop(Target.Target, context);
    }

    protected override void OnRelease(in UIInteractionContext context)
    {
        if (_dragged)
            Panel.Target?.DropComponent(Target.Target, context);
        Panel.Target?.ClearComponentDropPreview();
        _dragged = false;
    }
}
