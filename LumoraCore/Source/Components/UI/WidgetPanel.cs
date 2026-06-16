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

/// <summary>
/// A standalone, world-space, grabbable, persistent widget. It hosts a
/// <see cref="WidgetPreset"/>'s content on its own canvas, rendered directly in
/// the world (no dashboard render-texture) and clickable through the laser's
/// canvas hit test.
///
/// Click vs. grab is resolved by edit mode, not by where you point. The laser
/// always selects the highest-priority target it hits, and a Canvas
/// (priority 1000) outranks a Grabbable; so out of edit mode the canvas wins and
/// you interact with the widget's content, and in edit mode the grab is boosted
/// above the canvas so you can pick the panel up and place it. Toggle
/// <see cref="EditMode"/> to switch every panel between the two.
/// </summary>
public sealed class WidgetPanel : Component
{
    private const float CanvasScale = 0.001f;
    // Boosted above Canvas (1000) so the grab wins the whole panel in edit mode;
    // dropped below zero out of edit mode so the canvas always wins for clicks.
    private const int GrabActivePriority = 2000;
    private const int GrabIdlePriority = -1;
    // Release this close to the dash surface (in edit mode) to dock back onto the bar.
    private const float DockDistance = 0.35f;

    /// <summary>
    /// Local per-user UI state: when on, every panel becomes grabbable so widgets
    /// can be repositioned. Not synced - this is a userspace editing toggle, like
    /// the dashboard's own edit mode.
    /// </summary>
    public static bool EditMode { get; set; }

    public readonly Sync<float2> CanvasSize;
    public readonly AssetRef<FontSet> Font;

    private Slot? _bodySlot;
    private FontProvider? _fontProvider;
    private Grabbable? _grab;
    private bool _built;

    public WidgetPanel()
    {
        CanvasSize = new Sync<float2>(this, new float2(220f, 110f));
        Font = new AssetRef<FontSet>(this);
    }

    /// <summary>The canvas slot widget content is built into.</summary>
    public Slot? Body => _bodySlot != null && !_bodySlot.IsDestroyed ? _bodySlot : null;

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

    // Drop-in: release a panel near the dash surface (in edit mode) to dock its
    // preset back onto the top bar and remove the standalone panel. Both live in
    // userspace, so a plain proximity test against the surface is the "over the
    // dash" check - no portal UV mapping needed.
    private void OnReleased(IGrabbable grabbable)
    {
        if (!EditMode)
            return;

        var dash = UserspaceDashboard.LocalInstance;
        var surface = dash?.SurfaceSlot;
        if (dash == null || surface == null)
            return;

        if ((Slot.GlobalPosition - surface.GlobalPosition).Length > DockDistance)
            return;

        var presetType = Body?.GetComponent<WidgetPreset>()?.GetType();
        if (presetType != null && dash.Dashboard?.TryDockWidget(presetType) == true)
            Slot.Destroy();
    }

    /// <summary>
    /// Build (or re-bind, on load) the font provider, grab, body slot and canvas.
    /// Idempotent: re-finds existing children so a persisted panel reloads without
    /// duplicating its subtree.
    /// </summary>
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
        _grab.OnLocalReleased += OnReleased;

        _bodySlot = Slot.FindChild("Body", recursive: false) ?? Slot.AddSlot("Body");
        _bodySlot.LocalScale.Value = float3.One * CanvasScale;

        var size = CanvasSize.Value;
        var rect = _bodySlot.GetComponent<RectTransform>() ?? _bodySlot.AttachComponent<RectTransform>();
        rect.OffsetMin.Value = new float2(-size.x * 0.5f, -size.y * 0.5f);
        rect.OffsetMax.Value = new float2(size.x * 0.5f, size.y * 0.5f);

        _ = _bodySlot.GetComponent<Canvas>() ?? _bodySlot.AttachComponent<Canvas>();

        return this;
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

    /// <summary>
    /// Spawn a grabbable widget panel hosting preset <typeparamref name="T"/> in
    /// the USERSPACE world (never the focused world) at the given pose. Userspace
    /// renders as an overlay aligned to the local user's view, so a head-relative
    /// pose places the panel in front of the user and pops out there - matching how
    /// the dashboard itself is placed. Persists with userspace; enter widget edit
    /// mode to reposition it.
    /// </summary>
    public static WidgetPanel? Spawn<T>(float3 globalPosition, floatQ globalRotation)
        where T : WidgetPreset, new()
        => Spawn(typeof(T), globalPosition, globalRotation);

    /// <summary>
    /// Non-generic spawn used when popping a widget out of a grid: the grid only
    /// knows the preset's runtime <see cref="Type"/>, so it re-hosts that preset
    /// on a fresh userspace panel rather than moving the canvas subtree.
    /// </summary>
    public static WidgetPanel? Spawn(Type presetType, float3 globalPosition, floatQ globalRotation)
    {
        var world = Engine.Current?.WorldManager?.UserspaceWorld;
        if (world?.RootSlot == null || presetType == null)
            return null;

        var slot = world.RootSlot.AddSlot(presetType.Name);
        slot.GlobalPosition = globalPosition;
        slot.GlobalRotation = globalRotation;

        var panel = slot.AttachComponent<WidgetPanel>();
        panel.EnsureBuilt();

        var preset = panel.Body!.AttachComponent(presetType);
        if (preset is TextWidgetPreset text)
            text.Font.Target = panel.Font.Target;

        return panel;
    }
}
