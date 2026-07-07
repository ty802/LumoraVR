// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class DashboardScreen : UIComponent
{
    public readonly Sync<string> Label;
    public readonly Sync<color> ActiveColor;

    /// <summary>Color of this screen's nav-row label text. Override to make a tab stand out.</summary>
    public virtual color NavLabelColor => new color(0.92f, 0.92f, 0.96f, 1f);

    private bool _built;
    private Slot? _contentSlot;
    // Overlays this screen parents OUTSIDE its own content slot - e.g. a modal + dim backdrop hosted on the
    // canvas root so they can cover the nav chrome. Those do NOT hide when this screen's content slot goes
    // inactive, so HideScreen deactivates them here. Structural safety net: a screen can't leave an on-top
    // overlay drawing over the next screen even if it forgets a custom OnHide. Register at build time. -xlinka
    private System.Collections.Generic.List<Slot>? _offRootOverlays;

    public Slot? ContentSlot
    {
        get
        {
            EnsureContent();
            return _contentSlot;
        }
    }

    public DashboardScreen()
    {
        Label = new Sync<string>(this, string.Empty);
        ActiveColor = new Sync<color>(this, new color(0.28f, 0.72f, 1f, 1f));
    }

    public override void OnStart()
    {
        base.OnStart();
        // Content is built lazily on first ShowScreen (or first ContentSlot access), NOT here. Opening the dash
        // used to tessellate every screen's UI up front (all screens OnStart at once), a big one-frame comp
        // spike. Now only the screen you actually land on builds; the rest defer until navigated to. - xlinka
    }

    public void ShowScreen()
    {
        EnsureContent();
        Slot.ActiveSelf.Value = true;
        OnShow();
        // Activating the slot doesn't dirty the canvas on its own, so a freshly
        // shown screen's chunks stay unbuilt until something else forces a full
        // dirty (hover, or switching screens) - that's the "flick screens to make
        // it render". Force a full layout+render here so it shows immediately.
        Slot.GetComponentInParents<Canvas>()?.MarkLayoutDirty();
    }

    public void HideScreen()
    {
        Slot.ActiveSelf.Value = false;
        OnHide();
        // Safety net for off-root overlays (canvas-root modals/backdrops). A screen with a stateful menu still
        // overrides OnHide for a clean reset; this just guarantees nothing it parented above its own content
        // survives the switch and draws over the next screen. -xlinka
        if (_offRootOverlays != null)
        {
            for (int i = 0; i < _offRootOverlays.Count; i++)
            {
                var overlay = _offRootOverlays[i];
                if (overlay != null && !overlay.IsDestroyed)
                    overlay.ActiveSelf.Value = false;
            }
        }
    }

    /// <summary>
    /// Register a slot this screen parents OUTSIDE its own content slot (a modal/backdrop on the canvas root).
    /// HideScreen deactivates every registered overlay so it can't draw over the next screen. -xlinka
    /// </summary>
    protected void RegisterOverlay(Slot overlay)
    {
        if (overlay == null)
            return;
        _offRootOverlays ??= new System.Collections.Generic.List<Slot>();
        if (!_offRootOverlays.Contains(overlay))
            _offRootOverlays.Add(overlay);
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }

    protected virtual void OnContentReady(Slot contentSlot)
    {
    }

    protected virtual void BuildContent(UIBuilder builder)
    {
    }

    private void EnsureContent()
    {
        if (_built) return;
        _built = true;

        var rect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        Fill(rect);

        _contentSlot = Slot.AddSlot("Content");
        Fill(_contentSlot.AttachComponent<RectTransform>());
        OnContentReady(_contentSlot);
        BuildContent(new UIBuilder(_contentSlot));
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
