// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Components.UI.Worlds;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// While something is being moved, this sits over the whole Inventory screen and takes every pointer
// event: hover moves drive the ghost, a press is the drop (or the cancel). It captures its subtree so
// the cards and pills under it cannot fire on the same press, which is what lets a click on a folder
// card mean "put it here" rather than "open this folder". Inactive the rest of the time, so the hit
// scan never sees it. -xlinka
[ComponentCategory("Hidden")]
public sealed class InventoryMoveCatcher : InteractionElement, IUIInteractionCapture
{
    public Action<UIInteractionContext>? Moved;
    public Action<UIInteractionContext>? Dropped;

    public bool CapturesSubtree(in UIInteractionContext context) => Slot != null && Slot.IsActive;

    protected override void OnHoverMove(in UIInteractionContext context) => Moved?.Invoke(context);

    protected override void OnHoverEnter(in UIInteractionContext context) => Moved?.Invoke(context);

    protected override void OnPress(in UIInteractionContext context) => Dropped?.Invoke(context);
}

// The transparent copy that follows the pointer while a thing is being moved: a small card with the
// thing's initial and name, drawn above everything else on the screen. It is positioned in canvas
// units against its parent's rect each hover move, so it works from the mouse and from a laser alike.
internal sealed class InventoryGhost
{
    private const float Width = 220f;
    private const float Height = 52f;

    public readonly Slot Root;
    private readonly RectTransform _rect;
    private readonly RectTransform _parentRect;
    private readonly Text _name;
    private readonly Text _initial;
    private readonly Chip _kind;

    public InventoryGhost(BrowserParts parts, Slot parent, RectTransform parentRect)
    {
        _parentRect = parentRect;
        Root = parent.AddSlot("MoveGhost");
        Root.ActiveSelf.Value = false;
        _rect = Root.AttachComponent<RectTransform>();
        _rect.AnchorMin.Value = new float2(0f, 1f);
        _rect.AnchorMax.Value = new float2(0f, 1f);
        Root.AttachComponent<GraphicChunkRoot>();
        var fill = new color(DashTheme.Surface.r, DashTheme.Surface.g, DashTheme.Surface.b, 0.62f);
        BrowserParts.Panel(Root, fill, DashTheme.RadiusCard, DashTheme.Accent, 2f);

        var badge = Root.AddSlot("Initial");
        var badgeRect = badge.AttachComponent<RectTransform>();
        badgeRect.AnchorMin.Value = new float2(0f, 0.5f);
        badgeRect.AnchorMax.Value = new float2(0f, 0.5f);
        badgeRect.OffsetMin.Value = new float2(8f, -18f);
        badgeRect.OffsetMax.Value = new float2(44f, 18f);
        BrowserParts.Panel(badge, new color(DashTheme.Accent.r, DashTheme.Accent.g, DashTheme.Accent.b, 0.7f), DashTheme.RadiusChip);
        _initial = parts.Label(badge, string.Empty, DashTheme.FontHeading, DashTheme.OnAccent, BrowserParts.Weight.Bold);

        var nameSlot = Root.AddSlot("Name");
        var nameRect = nameSlot.AttachComponent<RectTransform>();
        nameRect.AnchorMin.Value = new float2(0f, 0f);
        nameRect.AnchorMax.Value = new float2(1f, 1f);
        nameRect.OffsetMin.Value = new float2(52f, 0f);
        nameRect.OffsetMax.Value = new float2(-70f, 0f);
        _name = parts.Label(nameSlot, string.Empty, DashTheme.FontBody, DashTheme.Text, BrowserParts.Weight.Semibold);
        _name.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        _kind = parts.AddChip(Root, string.Empty, DashTheme.Field, DashTheme.TextDim, 58f);
        var kindRect = _kind.Root.GetComponent<RectTransform>()!;
        kindRect.AnchorMin.Value = new float2(1f, 0.5f);
        kindRect.AnchorMax.Value = new float2(1f, 0.5f);
        kindRect.OffsetMin.Value = new float2(-66f, -DashTheme.ChipHeight * 0.5f);
        kindRect.OffsetMax.Value = new float2(-8f, DashTheme.ChipHeight * 0.5f);
    }

    public void Show(string name, string kind)
    {
        BrowserParts.SetText(_name, BrowserParts.Truncate(name, 20));
        BrowserParts.SetText(_initial, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?");
        _kind.Set(kind, DashTheme.Field, DashTheme.TextDim);
        Root.ActiveSelf.Value = true;
    }

    public void Hide()
    {
        if (Root.ActiveSelf.Value)
            Root.ActiveSelf.Value = false;
    }

    // Centred a little up and right of the pointer so the pointer itself stays visible over the card
    // it is about to drop on.
    public void MoveTo(in float2 canvasPoint)
    {
        var parent = _parentRect.LocalComputeRect;
        float dx = canvasPoint.x - parent.x + 14f;
        float dy = canvasPoint.y - (parent.y + parent.height) + 10f;
        _rect.OffsetMin.Value = new float2(dx, dy - Height * 0.5f);
        _rect.OffsetMax.Value = new float2(dx + Width, dy + Height * 0.5f);
    }
}
