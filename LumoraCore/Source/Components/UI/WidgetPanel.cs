// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A standalone, world-space, grabbable, persistent widget. It hosts a WidgetPreset's content on its own
// canvas, rendered directly in the world (no dashboard render-texture) and clickable through the laser's
// canvas hit test. Click vs. grab is resolved by edit mode, not by where you point. The laser always selects
// the highest-priority target it hits, and a Canvas (priority 1000) outranks a Grabbable; so out of edit mode
// the canvas wins and you interact with the widget's content, and in edit mode the grab is boosted above the
// canvas so you can pick the panel up. Carry it over any widget grid and let go to put it down there. -xlinka
[ComponentCategory("Hidden")]
public sealed class WidgetPanel : Component
{
    public const float CanvasScale = 0.001f;
    // Boosted above Canvas (1000) so the grab wins the whole panel in edit mode;
    // dropped below zero out of edit mode so the canvas always wins for clicks.
    private const int GrabActivePriority = 2000;
    private const int GrabIdlePriority = -1;

    // Local per-user UI state: when on, every panel becomes grabbable and every grid shows its cells so
    // widgets can be rearranged. Not synced, this is a userspace editing toggle.
    public static bool EditMode { get; set; }

    public readonly Sync<float2> CanvasSize;
    // The footprint this widget last had on a grid, tried before its preferred size when it is put back
    // down. Zero until it has been on a grid. -xlinka
    public readonly Sync<float2> LastPlacedSize;
    public readonly AssetRef<FontSet> Font;

    private Slot? _bodySlot;
    private FontProvider? _fontProvider;
    private Grabbable? _grab;
    private bool _built;

    public WidgetPanel()
    {
        CanvasSize = new Sync<float2>(this, new float2(220f, 110f));
        LastPlacedSize = new Sync<float2>(this, float2.Zero);
        Font = new AssetRef<FontSet>(this);
    }

    // The canvas slot widget content is built into.
    public Slot? Body => _bodySlot != null && !_bodySlot.IsDestroyed ? _bodySlot : null;

    public Grabbable? GrabHandle => _grab != null && !_grab.IsDestroyed ? _grab : null;

    public Type? PresetType => Body?.GetComponent<WidgetPreset>()?.GetType();

    // The hosted preset's Widget, which carries the size limits a grid negotiates against.
    public Widget? ContentWidget => Body?.GetComponent<WidgetPreset>()?.EnsureBuilt();

    public static WidgetPanel? From(IGrabbable? item) => (item as Component)?.Slot?.GetComponent<WidgetPanel>();

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (_grab != null && !_grab.IsDestroyed)
            _grab.InteractionPriority.Value = EditMode ? GrabActivePriority : GrabIdlePriority;
    }

    // Build (or re-bind, on load) the font provider, grab, body slot and canvas. Idempotent: re-finds existing
    // children so a persisted panel reloads without duplicating its subtree.
    public WidgetPanel EnsureBuilt()
    {
        if (_built) return this;
        _built = true;

        EnsureFont();

        _grab = Slot.GetComponent<Grabbable>() ?? Slot.AttachComponent<Grabbable>();
        _grab.AllowGrab.Value = true;
        _grab.Scalable.Value = true;
        _grab.FollowRotation.Value = true;
        _grab.Receivable.Value = false;
        _grab.InteractionPriority.Value = GrabIdlePriority;

        _bodySlot = Slot.FindChild("Body", recursive: false) ?? Slot.AddSlot("Body");
        _bodySlot.LocalScale.Value = float3.One * CanvasScale;

        ApplyCanvasSize(CanvasSize.Value);

        _ = _bodySlot.GetComponent<Canvas>() ?? _bodySlot.AttachComponent<Canvas>();

        return this;
    }

    // Resize the panel around its centre. Spawn calls this once the preset is on, because the default pill
    // is sized for a clock and a card like the account form has to bring its own size or it comes out
    // squeezed into a strip. -xlinka
    public void ApplyCanvasSize(in float2 size)
    {
        if (_bodySlot == null || _bodySlot.IsDestroyed || size.x <= 0f || size.y <= 0f)
            return;
        CanvasSize.Value = size;
        var rect = _bodySlot.GetComponent<RectTransform>() ?? _bodySlot.AttachComponent<RectTransform>();
        rect.OffsetMin.Value = new float2(-size.x * 0.5f, -size.y * 0.5f);
        rect.OffsetMax.Value = new float2(size.x * 0.5f, size.y * 0.5f);
    }

    // World size of one canvas pixel. A widget lifted off the dash keeps the size it had on the surface.
    public void SetPixelScale(float metersPerPixel)
    {
        if (metersPerPixel <= 0f)
            return;
        Slot.GlobalScale = float3.One * (metersPerPixel / CanvasScale);
    }

    private void EnsureFont()
    {
        if (Font.Target != null)
            return;

        // The provider must live in this panel's world: cross-world AssetRef
        // targets are rejected, so a shared provider can't be reused here.
        _fontProvider ??= Slot.FindChild("WidgetFont", recursive: false)?.GetComponent<FontProvider>();
        if (_fontProvider == null)
        {
            var fontSlot = Slot.AddSlot("WidgetFont");
            _fontProvider = fontSlot.AttachComponent<FontProvider>();
            if (ImportDialog.DefaultFontUrl != null)
            {
                _fontProvider.URL.Value = ImportDialog.DefaultFontUrl;
                _fontProvider.FallbackURLs.Add(ImportDialog.DefaultFontUrl);
            }
        }

        Font.Target = _fontProvider;
    }

    public static WidgetPanel? Spawn<T>(float3 globalPosition, floatQ globalRotation)
        where T : WidgetPreset, new()
        => Spawn(typeof(T), globalPosition, globalRotation);

    // Spawn a grabbable panel hosting a preset under the local userspace root at the given pose. That root
    // is where the userspace pointer can reach: anything parented elsewhere in the userspace world is
    // outside its exclusive root and can never be picked up again. The grid only knows the preset's
    // runtime type, so the panel re-hosts that preset rather than moving the canvas subtree. -xlinka
    public static WidgetPanel? Spawn(Type presetType, float3 globalPosition, floatQ globalRotation, float2? canvasSize = null)
    {
        var root = Templates.Userspace.LocalRoot;
        if (root == null || root.IsDestroyed)
            root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null || presetType == null)
            return null;

        var slot = root.AddSlot(presetType.Name);
        slot.GlobalPosition = globalPosition;
        slot.GlobalRotation = globalRotation;

        var panel = slot.AttachComponent<WidgetPanel>();
        panel.EnsureBuilt();

        var preset = panel.Body!.AttachComponent(presetType);
        if (preset is TextWidgetPreset text)
            text.Font.Target = panel.Font.Target;
        if (preset is WidgetPreset sized)
            panel.ApplyCanvasSize(canvasSize ?? sized.PreferredSize.Value);

        return panel;
    }
}
