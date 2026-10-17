// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The camera's switches in one column, and a press to leave an anchor where the lens is. -xlinka
[ComponentCategory("Hidden")]
public sealed class CameraOptionsWidgetPreset : DirectorWidgetPreset
{
    private const float TitleTop = 10f;
    private const float TitleHeight = 22f;
    private const float FirstRow = 38f;
    private const float RowPitch = 32f;

    public CameraOptionsWidgetPreset()
    {
        MinSize.Value = new float2(200f, 470f);
        PreferredSize.Value = new float2(230f, 500f);
        MaxSize.Value = new float2(420f, 700f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        var title = Row(root, "Title", TitleTop, TitleHeight, Pad, Pad);
        Label(title, "Text", "Options", DashTheme.FontBody, DashTheme.Text, TextHorizontalAlignment.Left,
            float2.Zero, float2.One, float2.Zero, float2.Zero, SemiboldFont);

        var rows = SettingsUI.Fill(root, "Rows", Pad, 0f, Pad, 0f);
        int row = 0;
        Option(rows, ref row, "Avoid walls", d => d.AvoidOcclusion, (d, v) => d.AvoidOcclusion = v);
        Option(rows, ref row, "Stay in world", d => d.KeepInWorld, (d, v) => d.KeepInWorld = v);
        Option(rows, ref row, "Hand wobble", d => d.Wobble, (d, v) => d.Wobble = v);
        Option(rows, ref row, "Aim ahead", d => d.AimAhead, (d, v) => d.AimAhead = v);
        Option(rows, ref row, "Hide camera", d => d.HideBody, (d, v) => d.HideBody = v);
        Option(rows, ref row, "Eyes on camera", d => d.EyesOnCamera, (d, v) => d.EyesOnCamera = v);
        Option(rows, ref row, "Hide name plates", d => d.HideNameplates, (d, v) => d.HideNameplates = v);
        Option(rows, ref row, "Hide lasers", d => d.HideLasers, (d, v) => d.HideLasers = v);
        Option(rows, ref row, "Show view", d => d.ShowFrustum, (d, v) => d.ShowFrustum = v);
        Option(rows, ref row, "Camera audio", d => d.AudioFromCamera, (d, v) => d.AudioFromCamera = v);
        Option(rows, ref row, "Photo timer", d => d.Timed, (d, v) => d.Timed = v);
        Option(rows, ref row, "Show live", d => d.ShowLive, (d, v) => d.ShowLive = v);

        var anchor = Row(rows, "NewAnchor", FirstRow + row * RowPitch + 8f, LineHeight, 0f, 0f);
        Chip(anchor, "New anchor", director => director.CreateAnchor(), live: director => director.Camera != null);

        Settle();
    }

    private void Option(Slot parent, ref int row, string label, Func<CameraDirector, bool> read,
        Action<CameraDirector, bool> write)
    {
        SwitchRow(parent, label, label, FirstRow + row * RowPitch, read, write, showState: false);
        row++;
    }
}
