// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.UI.Worlds;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// One pooled card in the inventory grid. It shows a folder, a saved item or a saved world, and gets
// rebound to a different entry as the grid scrolls, so the press handler reads the card's live entry
// rather than anything captured at build time. Three paints on top of the normal one: selected (accent
// outline), drop target (positive outline, for the folder under a carried thing) and carried (faded,
// the thing being moved). -xlinka
internal sealed class InventoryCard
{
    // A card is a thumbnail with a name under it, not a poster. At 168 tall over a 4 wide grid they
    // came out 250 across and four of them filled the screen. -xlinka
    public const float Height = 132f;
    private const float FrameThickness = 1.5f;
    private const float TextInset = 10f;
    private const float ArtHeight = 80f;
    private const float ChipWidth = 50f;
    private const float ChipInset = 6f;

    public readonly Slot Root;
    public readonly RectTransform Rect;
    public int EntryIndex = -1;
    public bool Active;
    public string Id = string.Empty;
    public InventoryEntryKind Kind;
    public Action<InventoryCard, UIInteractionContext>? Pressed;

    private readonly RoundedPanel _frame;
    private readonly ArtBlock _art;
    private readonly Slot _folderArt;
    private readonly Text _name;
    private readonly Text _meta;
    private readonly Chip _chip;
    private readonly Image _wash;
    private readonly ColorDriver _washDriver;
    private readonly Button _button;
    private readonly int _nameBudget;
    private readonly int _metaBudget;

    public InventoryCard(BrowserParts parts, Slot parent, float width)
    {
        _nameBudget = (int)(width / 8.8f);
        _metaBudget = (int)(width / 6.5f);

        Root = parent.AddSlot("Card");
        Root.ActiveSelf.Value = false;
        Rect = Root.AttachComponent<RectTransform>();
        Rect.AnchorMin.Value = new float2(0f, 1f);
        Rect.AnchorMax.Value = new float2(0f, 1f);
        Root.AttachComponent<GraphicChunkRoot>();
        _frame = BrowserParts.Panel(Root, DashTheme.Surface, DashTheme.RadiusCard, DashTheme.Outline, FrameThickness);

        _button = Root.AttachComponent<Button>();
        _button.Clicked += (_, context) => Pressed?.Invoke(this, context);

        // Picture area across the top. Items have no picture yet, so the art block's initial stands in;
        // worlds bring the thumbnail that was uploaded with them.
        var artFrame = Root.AddSlot("ArtFrame");
        var artRect = artFrame.AttachComponent<RectTransform>();
        artRect.AnchorMin.Value = new float2(0f, 1f);
        artRect.AnchorMax.Value = new float2(1f, 1f);
        artRect.OffsetMin.Value = new float2(FrameThickness, -ArtHeight);
        artRect.OffsetMax.Value = new float2(-FrameThickness, -FrameThickness);
        _art = new ArtBlock(parts, artFrame, DashTheme.RadiusCard - FrameThickness, 28f, width / ArtHeight, 0f);

        // A folder drawn as a folder: a darker back with its tab, and a lighter front flap sitting a
        // little lower, so it reads as a thing with an inside. Only one of the two arts is up at a time.
        _folderArt = artFrame.AddSlot("FolderArt");
        BrowserParts.Fill(_folderArt.AttachComponent<RectTransform>());
        BrowserParts.Panel(_folderArt, DashTheme.Field, DashTheme.RadiusCard - FrameThickness);
        var back = _folderArt.AddSlot("Back");
        var backRect = back.AttachComponent<RectTransform>();
        backRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        backRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        backRect.OffsetMin.Value = new float2(-28f, -18f);
        backRect.OffsetMax.Value = new float2(28f, 13f);
        BrowserParts.Panel(back, Blend(DashTheme.Accent, 0.62f), 7f);
        var tab = _folderArt.AddSlot("Tab");
        var tabRect = tab.AttachComponent<RectTransform>();
        tabRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        tabRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        tabRect.OffsetMin.Value = new float2(-28f, 11f);
        tabRect.OffsetMax.Value = new float2(-5f, 20f);
        BrowserParts.Panel(tab, Blend(DashTheme.Accent, 0.62f), 5f);
        var flap = _folderArt.AddSlot("Flap");
        var flapRect = flap.AttachComponent<RectTransform>();
        flapRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        flapRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        flapRect.OffsetMin.Value = new float2(-28f, -18f);
        flapRect.OffsetMax.Value = new float2(28f, 5f);
        BrowserParts.Panel(flap, Blend(DashTheme.AccentHover, 0.92f), 7f, Blend(DashTheme.OnAccent, 0.18f), 1f);
        _folderArt.ActiveSelf.Value = false;

        var nameSlot = Root.AddSlot("Name");
        BrowserParts.PinBottom(nameSlot.AttachComponent<RectTransform>(), 26f, 24f, TextInset);
        _name = parts.Label(nameSlot, string.Empty, DashTheme.FontBody, DashTheme.Text, BrowserParts.Weight.Semibold);

        var metaSlot = Root.AddSlot("Meta");
        BrowserParts.PinBottom(metaSlot.AttachComponent<RectTransform>(), 6f, 18f, TextInset);
        _meta = parts.Label(metaSlot, string.Empty, DashTheme.FontSmall, DashTheme.TextDim, BrowserParts.Weight.Regular);

        _chip = parts.AddChip(artFrame, string.Empty, DashTheme.Accent, DashTheme.OnAccent, ChipWidth);
        BrowserParts.PinTopCorner(_chip.Root.GetComponent<RectTransform>()!, ChipInset, ChipWidth, DashTheme.ChipHeight, fromRight: true);

        // Hover wash above the art, so a picture does not hide it.
        var wash = BrowserParts.Child(artFrame, "Hover");
        _wash = wash.AttachComponent<Image>();
        _wash.Tint.Value = color.Transparent;
        _washDriver = _button.AddColorDriver(_wash.Tint, color.Transparent, InteractionColorMode.Direct);
        _washDriver.HighlightColor.Value = new color(1f, 1f, 1f, 0.08f);
        _washDriver.PressedColor.Value = new color(1f, 1f, 1f, 0.14f);
        _washDriver.DisabledColor.Value = color.Transparent;
    }

    private static color Blend(in color c, float alpha) => new color(c.r, c.g, c.b, alpha);

    public void Place(float x, float y, float width)
    {
        Rect.OffsetMin.Value = new float2(x, -(y + Height));
        Rect.OffsetMax.Value = new float2(x + width, -y);
    }

    public void Bind(in InventoryEntry entry, int index)
    {
        EntryIndex = index;
        Id = entry.Id;
        Kind = entry.Kind;
        BrowserParts.SetText(_name, BrowserParts.Truncate(entry.Name, _nameBudget));

        bool folder = entry.IsFolder;
        BrowserParts.SetActive(_folderArt, folder);
        BrowserParts.SetActive(_art.Root, !folder);
        if (!folder)
            _art.SetPlaceholder(null, entry.Name);

        string meta;
        if (folder)
            meta = entry.ChildCount == 1 ? "1 thing inside" : $"{entry.ChildCount} things inside";
        else if (!string.IsNullOrEmpty(entry.Path))
            meta = $"in {entry.Path}";
        else
            meta = $"{entry.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Inventory.FormatBytes(entry.SizeBytes)}";
        BrowserParts.SetText(_meta, BrowserParts.Truncate(meta, _metaBudget));

        switch (entry.Kind)
        {
            case InventoryEntryKind.World:
                if (Enum.TryParse(entry.ItemKind, ignoreCase: true, out WorldMode mode))
                {
                    var tint = DashTheme.ModeTint(mode);
                    _chip.Set(DashTheme.ModeLabel(mode), Blend(tint, 0.85f), DashTheme.OnAccent);
                }
                else
                {
                    _chip.Set("World", Blend(DashTheme.ModeSocial, 0.85f), DashTheme.OnAccent);
                }
                BrowserParts.SetActive(_chip.Root, true);
                break;
            case InventoryEntryKind.Item:
                if (entry.IsAvatar)
                    _chip.Set("Avatar", Blend(DashTheme.Warning, 0.85f), DashTheme.OnAccent);
                else
                    _chip.Set("Item", Blend(DashTheme.Accent, 0.85f), DashTheme.OnAccent);
                BrowserParts.SetActive(_chip.Root, true);
                break;
            default:
                BrowserParts.SetActive(_chip.Root, false);
                break;
        }

        if (!Active)
        {
            Root.ActiveSelf.Value = true;
            Active = true;
        }
    }

    public void ApplyArt(IAssetProvider<TextureAsset>? texture) => _art.SetTexture(texture);

    // Selected wins over drop target; carried dims the whole card so the ghost reads as the live copy.
    public void SetState(bool selected, bool dropTarget, bool carried)
    {
        color outline = selected ? DashTheme.Accent : dropTarget ? DashTheme.Positive : DashTheme.Outline;
        float thickness = selected || dropTarget ? 2.5f : FrameThickness;
        BrowserParts.SetOutline(_frame, outline, thickness);
        var fill = dropTarget ? DashTheme.SurfaceHover : DashTheme.Surface;
        if (carried)
            fill = Blend(fill, 0.45f);
        BrowserParts.SetColor(_frame.Color, fill);
        _button.Interactable.Value = !carried;
    }

    public void Hide()
    {
        if (Root.ActiveSelf.Value)
            Root.ActiveSelf.Value = false;
        Active = false;
        EntryIndex = -1;
    }

    public bool Contains(in float2 point, in float2 scrollOffset)
    {
        var r = Rect.LocalComputeRect;
        float px = point.x - scrollOffset.x;
        float py = point.y - scrollOffset.y;
        return Active && px >= r.x && px <= r.x + r.width && py >= r.y && py <= r.y + r.height;
    }
}
