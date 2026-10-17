// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Photos;

namespace Lumora.Core.Components.UI;

// The camera's own panel. It lives in userspace, so it stays with this machine from world to world and
// nobody else sees it, and a grab anywhere on it moves it. The switches, the picture and the controls share
// one canvas; the pages hang off its right side on a canvas of their own and only show when asked for.
//
// Closing it while the camera is on the desktop window only puts the panel away, so a headset user can
// stream with nothing in front of them. Any other close turns the camera off with it. -xlinka
[ComponentCategory("Hidden")]
[UserspaceOnly]
public sealed class CameraPanel : Component
{
    // Pixels, the unit the widgets are laid out in; the root's scale makes them a size in the room.
    private const float Pad = 12f;
    private const float Gap = 10f;
    private const float HeaderHeight = 36f;
    private const float OptionsWidth = 290f;
    private const float ViewWidth = 654f;
    private const float ViewHeight = 374f;
    private const float ControlsHeight = 146f;
    private const float PagesWidth = 380f;
    private const float ContentTop = Pad + HeaderHeight + Gap;
    private const float MainWidth = Pad + OptionsWidth + Gap + ViewWidth + Pad;
    private const float MainHeight = ContentTop + ViewHeight + Gap + ControlsHeight + Pad;

    // A gap between the two canvases that reads as two panels.
    private const float SideGap = 40f;

    private const float TitleWidth = 70f;
    private const float DotSize = 10f;
    private const float PageButtonWidth = 66f;
    private const float CloseWidth = 30f;
    private const float ButtonHeight = 24f;
    private const float ButtonGap = 4f;

    private const float HeightMeters = 0.3f;
    private const float Distance = 0.75f;
    private const float BelowEyes = 0.1f;
    private const float RefreshInterval = 0.25f;

    private static CameraPanel? _current;

    private readonly SettingsChip?[] _pageButtons = new SettingsChip?[CameraPagesWidgetPreset.PageTitles.Count];
    private Slot? _mainCanvas;
    private Slot? _side;
    private CameraPagesWidgetPreset? _pages;
    private Slot? _liveDot;
    private Text? _status;
    private SettingsChip? _close;
    private float _clock;
    private bool _built;

    // The panel, shown or put away.
    public static CameraPanel? Current => _current is { IsDestroyed: false } panel ? panel : null;

    // The panel when it is in front of someone.
    public static CameraPanel? Shown => Current is { } panel && panel.Slot.ActiveSelf.Value ? panel : null;

    public static bool IsOpen => Shown != null;

    public Slot? MainCanvas => _mainCanvas;

    public bool SideShown => _side != null && !_side.IsDestroyed && _side.ActiveSelf.Value;

    public CameraPagesWidgetPreset? Pages => _pages;

    // Brings the panel back where it was put away, or builds one, in front of the local user either way.
    public static CameraPanel? Open()
    {
        var panel = Current;
        if (panel != null)
        {
            if (!panel.Slot.ActiveSelf.Value)
            {
                panel.Slot.ActiveSelf.Value = true;
                panel.PlaceInFront(rescale: false);
            }
            return panel;
        }

        // Where the userspace pointer reaches; anything else in userspace is out of its exclusive root.
        var root = Templates.Userspace.LocalRoot;
        if (root == null || root.IsDestroyed)
            root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;
        panel = root.AddSlot("Camera Panel").AttachComponent<CameraPanel>();
        _current = panel;
        panel.EnsureBuilt();
        panel.PlaceInFront(rescale: true);
        return panel;
    }

    public static void Close() => Current?.CloseHere();

    // Gone, whatever the mirror is doing.
    public static void Remove()
    {
        var panel = Current;
        if (panel != null)
            panel.Slot.Destroy();
    }

    // The canvas a dialog from either side should open on. The pages' own canvas is narrower than a
    // dialog, which would hang over the gap and into the main canvas. Both canvases sit on rigs of their
    // own, so the panel is found through the surface a canvas belongs to.
    public static Slot ModalContext(Slot from) => Holding(from)?.MainCanvas ?? from;

    // The camera panel a slot is drawn on, from anywhere on either of its canvases.
    public static CameraPanel? Holding(Slot? slot)
        => slot?.GetComponentInParents<CameraPanel>() ?? PanelSurface.Of(slot)?.Slot.GetComponentInParents<CameraPanel>();

    // A header button: shows the pages on that tab, or puts them away when that tab is already up.
    public void TogglePage(int index)
    {
        if (_side == null || _pages == null || index < 0 || index >= _pageButtons.Length)
            return;
        if (SideShown && _pages.CurrentPage == index)
        {
            _side.ActiveSelf.Value = false;
        }
        else
        {
            _pages.ShowPage(index);
            _side.ActiveSelf.Value = true;
        }
        Refresh();
    }

    public override void OnStart()
    {
        base.OnStart();
        _current ??= this;
        EnsureBuilt();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        _clock += delta;
        if (_clock < RefreshInterval)
            return;
        _clock = 0f;
        Refresh();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
        base.OnDestroy();
    }

    private void CloseHere()
    {
        var director = CameraDirector.Local;
        if (director is { IsDestroyed: false, Mirror: true })
        {
            Slot.ActiveSelf.Value = false;
            return;
        }
        if (director is { IsDestroyed: false })
            director.Active = false;
        Slot.Destroy();
    }

    // BUILDING

    private void EnsureBuilt()
    {
        if (_built)
            return;
        _built = true;

        var grab = Slot.GetComponent<Grabbable>() ?? Slot.AttachComponent<Grabbable>();
        grab.AllowGrab.Value = true;
        grab.Scalable.Value = true;
        grab.FollowRotation.Value = true;
        grab.Receivable.Value = false;
        // Below the canvas, so a press on a control is a press; a grab walks up to this from the canvas hit.
        grab.InteractionPriority.Value = -1;

        _mainCanvas = Board("Main", float3.Zero, MainWidth, MainHeight, out _);
        BuildHeader(_mainCanvas);
        float right = Pad + OptionsWidth + Gap;
        Host<CameraOptionsWidgetPreset>(_mainCanvas, "Options", Pad, ContentTop, OptionsWidth, ViewHeight + Gap + ControlsHeight);
        Host<CameraViewWidgetPreset>(_mainCanvas, "View", right, ContentTop, ViewWidth, ViewHeight);
        Host<CameraControlsWidgetPreset>(_mainCanvas, "Controls", right, ContentTop + ViewHeight + Gap, ViewWidth, ControlsHeight);

        // Beside the main canvas, the same height, riding the same root: one grab moves both.
        float sideX = MainWidth * 0.5f + SideGap + PagesWidth * 0.5f;
        var sideCanvas = Board("Side", new float3(sideX, 0f, 0f), PagesWidth, MainHeight, out var sideHolder);
        _side = sideHolder;
        _pages = Host<CameraPagesWidgetPreset>(sideCanvas, "Pages", 0f, 0f, PagesWidth, MainHeight);
        _side.ActiveSelf.Value = false;

        Refresh();
    }

    // A PanelSurface, as a popped app's panel is: the canvas renders on a rig of its own into a texture the
    // room sees on a quad in the band above the dash. Inside its own pass a dialog, a list over the pages or
    // a scroll pane stacks the way it does on the dash; as an overlay canvas the whole panel shared sixteen
    // render priorities and its dialogs drew out of order. Position is in canvas units. -xlinka
    private Slot Board(string name, float3 position, float width, float height, out Slot holder)
    {
        holder = Slot.AddSlot(name);
        holder.LocalPosition.Value = position * PanelSurface.UnitMeters;
        var surface = PanelSurface.Create(holder, new float2(width, height));
        var dash = UserspaceDashboard.LocalInstance?.Dashboard;
        if (dash != null && !dash.IsDestroyed && ReferenceEquals(dash.World, World))
        {
            surface.WatchFont(dash.Font.Target);
            surface.WatchFont(dash.FontSemibold.Target);
            surface.WatchFont(dash.FontBold.Target);
        }
        var canvas = surface.CanvasSlot!;
        SettingsUI.Panel(canvas, DashTheme.GlassBackdrop, DashTheme.OutlineStrong, DashTheme.RadiusPanel);
        return canvas;
    }

    // A widget on a slot of its own, placed top-down from the canvas corner, in a chunk of its own over
    // the canvas fill the way the grids give each widget one.
    private static T Host<T>(Slot canvas, string name, float left, float top, float width, float height)
        where T : WidgetPreset, new()
    {
        var host = SettingsUI.Child(canvas, name, new float2(0f, 1f), new float2(0f, 1f),
            new float2(left, -(top + height)), new float2(left + width, -top));
        host.AttachComponent<GraphicChunkRoot>();
        return host.AttachComponent<T>();
    }

    // Title, the live mark, why there is no camera when there is not, and the buttons for the pages.
    private void BuildHeader(Slot canvas)
    {
        var header = SettingsUI.Child(canvas, "Header", new float2(0f, 1f), new float2(1f, 1f),
            new float2(Pad, -(Pad + HeaderHeight)), new float2(-Pad, -Pad));
        header.AttachComponent<GraphicChunkRoot>();
        SettingsUI.Panel(header, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var title = SettingsUI.Label(header, "Title", Font(semibold: true), DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, new float2(0f, 1f), new float2(14f, 0f), new float2(14f + TitleWidth, 0f));
        title.Content.Value = "Camera";

        float dotLeft = 14f + TitleWidth;
        _liveDot = SettingsUI.Child(header, "Live", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(dotLeft, -DotSize * 0.5f), new float2(dotLeft + DotSize, DotSize * 0.5f));
        SettingsUI.Panel(_liveDot, DashTheme.Negative, color.Transparent, DotSize * 0.5f);
        _liveDot.ActiveSelf.Value = false;

        int count = _pageButtons.Length;
        float buttons = count * (PageButtonWidth + ButtonGap) + CloseWidth + 6f;
        _status = SettingsUI.Label(header, "Status", Font(semibold: false), DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, float2.One, new float2(dotLeft + DotSize + 10f, 0f),
            new float2(-(buttons + 10f), 0f), shrinkToFit: true);

        var close = SettingsUI.Child(header, "Close", new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-(6f + CloseWidth), -ButtonHeight * 0.5f), new float2(-6f, ButtonHeight * 0.5f));
        _close = Button(close, "×", Close);
        for (int i = 0; i < count; i++)
        {
            float rightEdge = 6f + CloseWidth + ButtonGap + (count - 1 - i) * (PageButtonWidth + ButtonGap);
            var host = SettingsUI.Child(header, CameraPagesWidgetPreset.PageTitles[i], new float2(1f, 0.5f), new float2(1f, 0.5f),
                new float2(-(rightEdge + PageButtonWidth), -ButtonHeight * 0.5f), new float2(-rightEdge, ButtonHeight * 0.5f));
            int index = i;
            _pageButtons[i] = Button(host, CameraPagesWidgetPreset.PageTitles[i], () => TogglePage(index));
        }
    }

    private SettingsChip Button(Slot host, string label, Action press)
    {
        var chip = SettingsChip.Build(host, Font(semibold: true), DashTheme.FontSmall, DashTheme.RadiusControl);
        ListingStyle.SetText(chip.Text, label);
        chip.Button.Clicked += (_, _) => press();
        chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.Text);
        return chip;
    }

    // The dash in this world hands out the faces; a Text with none draws nothing.
    private IAssetProvider<FontSet>? Font(bool semibold)
    {
        var dash = UserspaceDashboard.LocalInstance?.Dashboard;
        if (dash == null || dash.IsDestroyed || !ReferenceEquals(dash.World, World))
            return null;
        return semibold ? dash.FontSemibold.Target ?? dash.Font.Target : dash.Font.Target;
    }

    private void Refresh()
    {
        if (_liveDot != null && !_liveDot.IsDestroyed && _liveDot.ActiveSelf.Value != LiveStatus.IsLive)
            _liveDot.ActiveSelf.Value = LiveStatus.IsLive;
        if (_status != null && !_status.IsDestroyed)
            ListingStyle.SetText(_status, CameraDirector.Local is { IsDestroyed: false } director ? director.Status : string.Empty);
        bool side = SideShown;
        int page = _pages?.CurrentPage ?? -1;
        for (int i = 0; i < _pageButtons.Length; i++)
        {
            var chip = _pageButtons[i];
            if (chip == null)
                continue;
            if (side && page == i)
                chip.SetPaint(DashTheme.Accent, DashTheme.AccentHover, DashTheme.Accent, DashTheme.OnAccent);
            else
                chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.Text);
        }
    }

    // Arm's length out along the head's flattened forward, a little under the eyes, turned to face back.
    // Userspace shares the focused world's space, so that world's head is where the user is.
    private void PlaceInFront(bool rescale)
    {
        var user = Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root;
        if (rescale)
            Slot.GlobalScale = float3.One * (HeightMeters / (MainHeight * PanelSurface.UnitMeters));
        if (user?.HeadSlot == null)
        {
            Slot.GlobalPosition = new float3(0f, 1.4f, -Distance);
            Slot.GlobalRotation = floatQ.Identity;
            return;
        }
        var head = user.HeadPosition;
        var forward = user.HeadRotation * float3.Backward;
        forward.y = 0f;
        forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
        Slot.GlobalPosition = head + forward * Distance + float3.Down * BelowEyes;
        var toViewer = head - Slot.GlobalPosition;
        toViewer.y = 0f;
        Slot.GlobalRotation = toViewer.LengthSquared > 1e-6f
            ? floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z))
            : floatQ.Identity;
    }
}
