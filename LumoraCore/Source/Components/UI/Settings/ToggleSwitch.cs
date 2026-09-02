// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A real on/off switch: pill track, round knob that sits at one end or the other, state word beside it.
// The old settings toggle was the generic Checkbox, which paints a small diamond and reads as neither
// on nor off at a glance.
//
// Deliberately not a Component. It carries no state of its own, it paints what the row hands it, and
// the row it belongs to is recycled across many items as the list scrolls, so anything that latched a
// value here would show the previous setting's answer on the next item. The click target is the whole
// row, not the switch. -xlinka
internal sealed class ToggleSwitch
{
    public const float TrackWidth = 44f;
    public const float TrackHeight = 24f;
    private const float KnobSize = 18f;
    private const float KnobMargin = 3f;
    private const float StateGap = 12f;
    private const float StateWidth = 46f;

    private readonly RoundedPanel _track;
    private readonly RoundedPanel _knob;
    private readonly RectTransform _knobRect;
    private readonly Text _state;

    private ToggleSwitch(RoundedPanel track, RoundedPanel knob, RectTransform knobRect, Text state)
    {
        _track = track;
        _knob = knob;
        _knobRect = knobRect;
        _state = state;
    }

    // Anchors itself to the left edge of host, vertically centred.
    public static ToggleSwitch Build(Slot host, IAssetProvider<FontSet>? font)
    {
        var trackSlot = SettingsUI.Child(host, "Track",
            new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(0f, -TrackHeight * 0.5f), new float2(TrackWidth, TrackHeight * 0.5f));
        // RoundedPanel clamps the radius to half the short side, so half-height is a true pill.
        var track = SettingsUI.Panel(trackSlot, DashTheme.Field, DashTheme.Outline, TrackHeight * 0.5f);

        var knobSlot = SettingsUI.Child(trackSlot, "Knob",
            new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(KnobMargin, -KnobSize * 0.5f), new float2(KnobMargin + KnobSize, KnobSize * 0.5f));
        var knob = SettingsUI.Panel(knobSlot, DashTheme.OnAccent, color.Transparent, KnobSize * 0.5f);

        var state = SettingsUI.Label(host, "State", font, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left,
            new float2(0f, 0f), new float2(0f, 1f),
            new float2(TrackWidth + StateGap, 0f), new float2(TrackWidth + StateGap + StateWidth, 0f));

        return new ToggleSwitch(track, knob, SettingsUI.Rect(knobSlot), state);
    }

    public void SetValue(bool on, bool interactable)
    {
        color fill = on && interactable ? DashTheme.Accent : DashTheme.Field;
        color outline = on && interactable ? DashTheme.Accent : DashTheme.Outline;
        SettingsUI.SetPaint(_track, fill, outline);
        SettingsUI.SetFill(_knob, interactable ? DashTheme.OnAccent : DashTheme.TextMuted);

        float left = on ? TrackWidth - KnobMargin - KnobSize : KnobMargin;
        SettingsUI.SetOffsets(_knobRect,
            new float2(left, -KnobSize * 0.5f), new float2(left + KnobSize, KnobSize * 0.5f));

        ListingStyle.SetText(_state, on ? "On" : "Off");
        ListingStyle.SetTextColor(_state, interactable ? DashTheme.TextDim : DashTheme.TextMuted);
    }
}
