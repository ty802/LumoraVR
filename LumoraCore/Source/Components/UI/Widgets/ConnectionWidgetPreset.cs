// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

[ComponentCategory("Hidden")]
public sealed class ConnectionWidgetPreset : WidgetPreset
{
    public readonly AssetRef<FontSet> Font;
    public readonly Sync<string> StatusText;
    public readonly Sync<color> DotColor;
    public readonly Sync<color> LabelColor;

    // Status colors. Green = other people are here, violet = a session with only you in it, muted
    // grey = no session at all.
    private static readonly color ConnectedDot = DashTheme.Positive;
    private static readonly color SoloDot = DashTheme.Accent;
    private static readonly color OfflineDot = DashTheme.TextMuted;

    // Live elements rebuilt by Build(); driven each update from the real session state.
    private Image? _dotImage;
    private Text? _text;

    public ConnectionWidgetPreset()
    {
        Font = new AssetRef<FontSet>(this);
        StatusText = new Sync<string>(this, "Offline");
        DotColor = new Sync<color>(this, OfflineDot);
        LabelColor = new Sync<color>(this, DashTheme.TextMuted);
    }

    protected override void Build(Widget widget, Slot root)
    {
        var dot = root.AddSlot("Dot");
        var dotRect = dot.AttachComponent<RectTransform>();
        dotRect.AnchorMin.Value = new float2(0f, 0.5f);
        dotRect.AnchorMax.Value = new float2(0f, 0.5f);
        dotRect.OffsetMin.Value = new float2(14f, -5f);
        dotRect.OffsetMax.Value = new float2(24f, 5f);
        _dotImage = dot.AttachComponent<Image>();
        _dotImage.Tint.Value = DotColor.Value;
        if (BackgroundSprite.Target != null)
        {
            _dotImage.Texture.Target = BackgroundSprite.Target;
            _dotImage.NineSlice.Value = true;
            _dotImage.Borders.Value = new float4(5f, 5f, 5f, 5f);
        }

        var builder = new UIBuilder(root);
        builder.Font(Font.Target).FontSize(DashTheme.FontSmall);
        _text = builder.Text(StatusText.Value, DashTheme.FontSmall, LabelColor.Value);
        _text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        var rect = _text.RectTransform;
        if (rect != null)
        {
            rect.AnchorMin.Value = float2.Zero;
            rect.AnchorMax.Value = float2.One;
            rect.OffsetMin.Value = new float2(32f, 0f);
            rect.OffsetMax.Value = float2.Zero;
        }

        RefreshStatus();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        RefreshStatus();
    }

    // Reflect the real network state of the active session (mirrors DebugScreen.BuildNetworkInfo:
    // reads Engine.Current.ActiveSessionTransferer.Session). No session = offline; a session with
    // only the local user = solo; otherwise connected with a peer count.
    private void RefreshStatus()
    {
        if (_dotImage == null || _text == null || _dotImage.IsDestroyed || _text.IsDestroyed)
            return;

        var session = Engine.Current?.ActiveSessionTransferer?.Session;
        string label;
        color dot;

        if (session == null || session.IsDisposed)
        {
            label = "Offline";
            dot = OfflineDot;
        }
        else
        {
            var world = session.World;
            int users = world?.UserCount ?? 1;
            bool host = world?.IsAuthority ?? true;
            if (users <= 1)
            {
                // A session exists but nobody else is in it: solo (your own hosted/home world).
                label = "Solo";
                dot = SoloDot;
            }
            else
            {
                int others = users - 1;
                label = host
                    ? $"Hosting ({others})"
                    : "Connected";
                dot = ConnectedDot;
            }
        }

        if (StatusText.Value != label)
            StatusText.Value = label;
        if (_text.Content.Value != label)
        {
            _text.Content.Value = label;
            Slot.GetComponentInParents<Canvas>()?.MarkDirty();
        }
        if (_dotImage.Tint.Value != dot)
        {
            _dotImage.Tint.Value = dot;
            DotColor.Value = dot;
            Slot.GetComponentInParents<Canvas>()?.MarkDirty();
        }
    }
}
