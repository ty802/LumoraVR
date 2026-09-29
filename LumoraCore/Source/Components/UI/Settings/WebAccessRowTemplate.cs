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

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);

        // The strip ends where the button begins instead of taking the usual share of the column, so the
        // two cannot overlap on a narrow dash.
        float band = SettingsMetrics.ControlBand * 0.5f;
        var strip = SettingsUI.Child(shell.Control, "Segments", new float2(0f, 0.5f), new float2(1f, 0.5f),
            new float2(0f, -band), new float2(-(SettingsMetrics.ActionWidth + SettingsMetrics.ValueGap), band));
        var buttonHost = SettingsUI.Child(shell.Control, "Remove", new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-SettingsMetrics.ActionWidth, -band), new float2(0f, band));
        var remove = SettingsChip.Build(buttonHost, Fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);

        var listingRow = new HostRow
        {
            Shell = shell,
            Segments = new SettingsSegments(strip, Fonts),
            Remove = remove,
            Screen = _screen,
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
