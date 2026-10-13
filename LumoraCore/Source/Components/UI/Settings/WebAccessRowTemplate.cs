// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Localization;
using Lumora.Core.Math;
using Lumora.Core.Networking.Web;

namespace Lumora.Core.Components.UI;

// One site scripts have asked to reach: the host, an Allowed / Denied strip that flips the saved answer
// in place, and Remove, which forgets it so the next request asks again. The item's Tag is the host.
internal sealed class WebAccessRowTemplate : SettingsRowTemplate
{
    private static readonly LocaleText AllowedLabel = "Settings.General.WebAccessAllowed".AsLocale("Allowed");
    private static readonly LocaleText DeniedLabel = "Settings.General.WebAccessDenied".AsLocale("Denied");
    private static readonly LocaleText RemoveLabel = "Settings.General.WebAccessRemove".AsLocale("Remove");

    private readonly SettingsScreen _screen;

    public WebAccessRowTemplate(SettingsFonts fonts, SettingsScreen screen) : base(fonts) => _screen = screen;

    // Allowed and Denied beside Remove while two pills and the button fit across the control; narrower,
    // the pills take the line and Remove drops under them.
    private static bool TwoLines(SettingsRowFit? fit)
        => fit is { Measured: true } && fit.ControlWidth < SettingsSegments.PillMin * 2f + SettingsMetrics.SegmentGap
            + SettingsMetrics.ActionWidth + SettingsMetrics.ValueGap;

    protected override float ControlHeight(ListingItem item) => SettingsSegments.BlockHeight(TwoLines(Fit) ? 2 : 1);

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);

        var strip = SettingsUI.Child(shell.Control, "Segments", float2.Zero, float2.Zero, float2.Zero, float2.Zero);
        var buttonHost = SettingsUI.Child(shell.Control, "Remove", float2.Zero, float2.Zero, float2.Zero, float2.Zero);
        var remove = SettingsChip.Build(buttonHost, Fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);

        var listingRow = new HostRow
        {
            Shell = shell,
            Segments = new SettingsSegments(strip, Fonts),
            Remove = remove,
            Screen = _screen,
            Fit = Fit,
            StripRect = SettingsUI.Rect(strip),
            RemoveRect = SettingsUI.Rect(buttonHost),
        };
        listingRow.Segments.Grow(2, index => listingRow.Pick(index));
        remove.Button.Clicked += (_, _) => listingRow.Forget();
        return listingRow;
    }

    private sealed class HostRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;
        public required SettingsChip Remove;
        public required SettingsScreen Screen;
        public required RectTransform StripRect;
        public required RectTransform RemoveRect;
        public SettingsRowFit? Fit;

        // One line: the strip ends where the button begins instead of taking the usual share of the
        // column, so the two cannot overlap. Two lines: the strip on top, the button under its right end.
        private void Arrange()
        {
            float band = SettingsMetrics.ControlBand;
            if (!TwoLines(Fit))
            {
                float half = band * 0.5f;
                SettingsUI.SetAnchors(StripRect, new float2(0f, 0.5f), new float2(1f, 0.5f));
                SettingsUI.SetOffsets(StripRect, new float2(0f, -half),
                    new float2(-(SettingsMetrics.ActionWidth + SettingsMetrics.ValueGap), half));
                SettingsUI.SetAnchors(RemoveRect, new float2(1f, 0.5f), new float2(1f, 0.5f));
                SettingsUI.SetOffsets(RemoveRect, new float2(-SettingsMetrics.ActionWidth, -half), new float2(0f, half));
                return;
            }
            SettingsUI.SetAnchors(StripRect, new float2(0f, 1f), float2.One);
            SettingsUI.SetOffsets(StripRect, new float2(0f, -band), float2.Zero);
            SettingsUI.SetAnchors(RemoveRect, new float2(1f, 0f), new float2(1f, 0f));
            SettingsUI.SetOffsets(RemoveRect, new float2(-SettingsMetrics.ActionWidth, 0f), new float2(0f, band));
        }

        // Reads the row's current item, never the one it was built for: rows are recycled.
        public void Pick(int index)
        {
            if (Item is not { Interactable: true, Tag: string host })
                return;
            Screen.SetWebAccess(host, allowed: index == 0);
            Bind(Item);
        }

        public void Forget()
        {
            if (Item is { Interactable: true, Tag: string host })
                Screen.ForgetWebAccess(host);
        }

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            Arrange();

            bool allowed = false;
            bool decided = item.Tag is string host && WebAccess.Shared.TryGetDecision(host, out allowed);
            Segments.Layout(2);
            Segments.Paint(0, AllowedLabel.Resolve(), decided && allowed, item.Interactable);
            Segments.Paint(1, DeniedLabel.Resolve(), decided && !allowed, item.Interactable);

            ListingStyle.SetText(Remove.Text, RemoveLabel.Resolve());
            // Same treatment as a destructive action row: neutral chrome, the word carries the warning.
            Remove.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline,
                item.Interactable ? DashTheme.Negative : DashTheme.TextMuted);
            Remove.SetInteractable(item.Interactable);
        }
    }
}
