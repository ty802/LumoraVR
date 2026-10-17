// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Layout;
using Helio.UI.Listing;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// What the camera sees, live, with the hands-on controls over it: click the picture and the subject moves
// to that spot, hold an arrow and the camera moves until let go, and the width of view runs along the
// bottom. The picture belongs to another world; a texture link carries it across. -xlinka
[ComponentCategory("Hidden")]
public sealed class CameraViewWidgetPreset : DirectorWidgetPreset
{
    private const float FrameInset = 6f;
    private const float OverlayInset = 8f;
    private const float HoldSize = 30f;
    private const float HoldGap = 4f;
    private const float BarHeight = 26f;
    private const float FovMin = 5f;
    private const float FovMax = 100f;

    // Drawn the size it has on the feed, so it frames the same share of the picture at any widget size.
    private const float ReticleSide = 128f;
    private static readonly color ReticleTint = new(1f, 0.75f, 0f, 0.4f);

    // The dash is captured only when it changes, and a live picture changes without touching the mesh, so
    // it asks for captures itself; at most this often, since each one is the whole dash.
    private const float PreviewInterval = 1f / 30f;

    private static readonly CameraDirector.Nudge[] Nudges =
    {
        CameraDirector.Nudge.Left, CameraDirector.Nudge.Up, CameraDirector.Nudge.Right,
        CameraDirector.Nudge.Closer, CameraDirector.Nudge.Down, CameraDirector.Nudge.Farther,
    };

    private static readonly string[] NudgeGlyphs = { "←", "↑", "→", "+", "↓", "−" };

    private readonly SettingsChip?[] _holds = new SettingsChip?[6];
    private readonly bool[] _held = new bool[6];

    private Slot? _picture;
    private Slot? _feed;
    private Image? _image;
    private TextureLink? _link;
    private Button? _aim;
    private Slot? _controls;
    private Slot? _reticle;
    private RectTransform? _reticleRect;
    private Slot? _reset;
    private Slot? _fov;
    private Slot? _warning;
    private Slot? _message;
    private Text? _messageText;
    private Slot? _start;
    private Canvas? _canvas;
    private float _previewClock;
    private bool _showing;

    public CameraViewWidgetPreset()
    {
        MinSize.Value = new float2(340f, 200f);
        PreferredSize.Value = new float2(640f, 380f);
        MaxSize.Value = new float2(1280f, 760f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        var frame = SettingsUI.Fill(root, "Frame", FrameInset, FrameInset, FrameInset, FrameInset);
        SettingsUI.Panel(frame, DashTheme.Field, color.Transparent, DashTheme.RadiusControl);

        // In the card's own chunk, after the well it sits in. A chunk of its own would pack into the same
        // top slot of the band as the well and the two would fight over which draws last.
        _picture = SettingsUI.Fill(frame, "Picture");
        var aspect = _picture.AttachComponent<AspectRatioConstraint>();
        aspect.Mode.Value = AspectRatioConstraint.AspectMode.FitInParent;
        aspect.AspectRatio.Value = 16f / 9f;
        _aim = _picture.AttachComponent<Button>();
        _aim.Clicked += (_, context) => Aim(context);

        // On the picture, not on the feed beside it: the feed is hidden until there is a picture, and a
        // component on a hidden slot is never updated, so a link there would never see one arrive.
        _link = _picture.AttachComponent<TextureLink>();
        _link.Source = () => Director is { Active: true } director ? director.Feed : null;
        _feed = SettingsUI.Fill(_picture, "Feed");
        _image = _feed.AttachComponent<Image>();
        _image.Texture.Target = _link;
        _image.Tint.Value = color.White;

        // A band above the card's, so the bars and the buttons always draw over the picture.
        var overlay = SettingsUI.Fill(_picture, "Overlay");
        overlay.AttachComponent<GraphicChunkRoot>().OverlayLevel = 1;
        _controls = SettingsUI.Fill(overlay, "Controls");

        _reticle = SettingsUI.Child(_controls, "Reticle", new float2(0.5f, 0.5f), new float2(0.5f, 0.5f), float2.Zero, float2.Zero);
        _reticleRect = SettingsUI.Rect(_reticle);
        var ring = SettingsUI.Panel(_reticle, color.Transparent, ReticleTint, 2f);
        ring.OutlineThickness.Value = 2f;

        BuildHolds(_controls);
        BuildFov(_controls);

        _warning = SettingsUI.Child(_controls, "Warning", float2.Zero, new float2(1f, 0f),
            new float2(OverlayInset, OverlayInset + BarHeight + 4f), new float2(-OverlayInset, OverlayInset + BarHeight + 24f));
        Label(_warning, "Text", "Not on your desktop window", DashTheme.FontSmall, DashTheme.Negative.WithAlpha(0.5f),
            TextHorizontalAlignment.Center, float2.Zero, float2.One, float2.Zero, float2.Zero, SemiboldFont);

        _message = SettingsUI.Fill(overlay, "Message");
        var line = SettingsUI.Child(_message, "Line", new float2(0f, 0.5f), new float2(1f, 0.5f),
            new float2(12f, 0f), new float2(-12f, 30f));
        _messageText = Label(line, "Text", string.Empty, DashTheme.FontBody, DashTheme.TextDim,
            TextHorizontalAlignment.Center, float2.Zero, float2.One, float2.Zero, float2.Zero, SemiboldFont);
        _messageText.WordWrap.Value = true;
        _start = SettingsUI.Child(_message, "Start", new float2(0.5f, 0.5f), new float2(0.5f, 0.5f),
            new float2(-48f, -38f), new float2(48f, -8f));
        Chip(_start, "Start", director => director.Active = true);

        Settle();
    }

    // A d-pad in the top-left corner: turn and raise in the top row, nearer, lower and further under it,
    // and Reset across the bottom.
    private void BuildHolds(Slot parent)
    {
        for (int i = 0; i < Nudges.Length; i++)
        {
            int column = i % 3;
            int row = i / 3;
            float left = OverlayInset + column * (HoldSize + HoldGap);
            float top = OverlayInset + row * (HoldSize + HoldGap);
            var host = SettingsUI.Child(parent, "Hold" + Nudges[i], new float2(0f, 1f), new float2(0f, 1f),
                new float2(left, -(top + HoldSize)), new float2(left + HoldSize, -top));
            int index = i;
            var chip = FreeChip(host, NudgeGlyphs[i], () => { }, () => _showing);
            chip.Button.Pressed += _ => Hold(index, true);
            chip.Button.Released += _ => Hold(index, false);
            _holds[i] = chip;
        }

        float resetTop = OverlayInset + 2f * (HoldSize + HoldGap);
        float resetWidth = 3f * HoldSize + 2f * HoldGap;
        _reset = SettingsUI.Child(parent, "Reset", new float2(0f, 1f), new float2(0f, 1f),
            new float2(OverlayInset, -(resetTop + BarHeight)), new float2(OverlayInset + resetWidth, -resetTop));
        Chip(_reset, "Reset", director => director.ResetFraming());
    }

    private void BuildFov(Slot parent)
    {
        _fov = SettingsUI.Child(parent, "Fov", float2.Zero, new float2(1f, 0f),
            new float2(OverlayInset, OverlayInset), new float2(-OverlayInset, OverlayInset + BarHeight));
        SettingsUI.Panel(_fov, DashTheme.Backdrop.WithAlpha(0.7f), color.Transparent, DashTheme.RadiusControl);
        Label(_fov, "Label", "FOV", DashTheme.FontLabel, DashTheme.TextDim, TextHorizontalAlignment.Left,
            float2.Zero, new float2(0f, 1f), new float2(10f, 0f), new float2(44f, 0f), SemiboldFont);
        var value = Label(_fov, "Value", string.Empty, DashTheme.FontSmall, DashTheme.Text, TextHorizontalAlignment.Right,
            new float2(1f, 0f), float2.One, new float2(-48f, 0f), new float2(-10f, 0f));
        var host = SettingsUI.Child(_fov, "Bar", float2.Zero, float2.One, new float2(48f, 0f), new float2(-52f, 0f));
        Bar(host, FovMin, FovMax, director => director.FieldOfView, (director, v) => director.FieldOfView = v,
            null, value, v => $"{v:0}°");
    }

    protected override void Refresh()
    {
        var director = Director;
        bool active = director is { Active: true };
        bool showing = active && _link != null && _link.IsAssetAvailable;
        if (showing != _showing)
        {
            _showing = showing;
            if (!showing)
                ReleaseHolds();
        }
        base.Refresh();

        Show(_feed, showing);
        Show(_controls, showing);
        Show(_message, !showing);
        Show(_start, !active);
        if (_messageText != null)
            ListingStyle.SetText(_messageText, active ? director!.Status : "Camera off");
        if (_aim != null)
            ListingStyle.SetInteractable(_aim, showing && !Editing);
        if (!showing || director == null)
            return;

        var mode = director.Mode;
        bool orbits = mode is PhotoCameraMode.ThirdPerson or PhotoCameraMode.Group;
        bool steers = orbits || mode == PhotoCameraMode.FirstPerson;
        for (int i = 0; i < Nudges.Length; i++)
        {
            var nudge = Nudges[i];
            bool shown = nudge is CameraDirector.Nudge.Closer or CameraDirector.Nudge.Farther ? orbits : steers;
            Show(_holds[i]?.Slot, shown);
            if (!shown && _held[i])
                Hold(i, false);
        }
        Show(_reset, steers);
        Show(_fov, mode != PhotoCameraMode.Manual);
        Show(_warning, InVR && !director.Mirror);

        if (_image != null && _image.FlipHorizontal.Value != director.FlipPreview)
            _image.FlipHorizontal.Value = director.FlipPreview;

        bool framed = mode is PhotoCameraMode.ThirdPerson or PhotoCameraMode.Group or PhotoCameraMode.Stationary;
        Show(_reticle, framed);
        if (framed && _reticleRect != null)
        {
            var framing = director.Framing;
            float x = director.FlipPreview ? 1f - framing.x : framing.x;
            float y = 1f - framing.y;
            float halfWidth = ReticleSide / PhotoCamera.FeedWidth * 0.5f;
            float halfHeight = ReticleSide / PhotoCamera.FeedHeight * 0.5f;
            SettingsUI.SetAnchors(_reticleRect, new float2(x - halfWidth, y - halfHeight), new float2(x + halfWidth, y + halfHeight));
            SettingsUI.SetOffsets(_reticleRect, float2.Zero, float2.Zero);
        }
    }

    protected override void Tick(float delta)
    {
        // A release that never arrived (the pointer left the canvas, the button went away) must not keep
        // the camera moving.
        for (int i = 0; i < _held.Length; i++)
        {
            if (_held[i] && _holds[i] is { } chip && !chip.Button.IsPressedNow)
                Hold(i, false);
        }

        if (!_showing)
            return;
        _previewClock += delta;
        if (_previewClock < PreviewInterval)
            return;
        _previewClock = 0f;
        if (_canvas == null || _canvas.IsDestroyed)
            _canvas = Slot.GetComponentInParents<Canvas>();
        _canvas?.RequestRender();
    }

    public override void OnDestroy()
    {
        ReleaseHolds();
        base.OnDestroy();
    }

    // Where the click landed on the picture, left to right and top to bottom; a mirrored preview is turned
    // back first so the subject goes where it was clicked.
    private void Aim(UIInteractionContext context)
    {
        var director = Director;
        var rect = _picture?.GetComponent<RectTransform>()?.LocalComputeRect;
        if (director == null || _picture == null || !rect.HasValue || rect.Value.width <= 0f || rect.Value.height <= 0f)
            return;
        var point = context.PointIn(_picture);
        float x = (point.x - rect.Value.xMin) / rect.Value.width;
        float y = (rect.Value.yMax - point.y) / rect.Value.height;
        if (director.FlipPreview)
            x = 1f - x;
        director.SetFraming(x, y);
        MarkChanged();
    }

    private void Hold(int index, bool down)
    {
        if (_held[index] == down)
            return;
        _held[index] = down;
        Director?.Hold(Nudges[index], down);
    }

    private void ReleaseHolds()
    {
        for (int i = 0; i < _held.Length; i++)
            Hold(i, false);
    }
}
