// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

internal sealed class SettingsDropdownTemplate : SettingsRowTemplate
{
    public SettingsDropdownTemplate(SettingsFonts fonts) : base(fonts) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var host = SettingsUI.Child(shell.Control, "Dropdown", new float2(0f, 0.5f), new float2(1f, 0.5f),
            new float2(0f, -SettingsMetrics.ControlBand * 0.5f), new float2(0f, SettingsMetrics.ControlBand * 0.5f));
        var chip = SettingsChip.Build(host, Fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        chip.Text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        chip.Text.Ellipsis.Value = true;
        var labelRect = chip.Text.Slot.GetComponent<RectTransform>()!;
        labelRect.OffsetMin.Value = new float2(12f, 0f);
        labelRect.OffsetMax.Value = new float2(-32f, 0f);
        SettingsUI.Label(host, "Arrow", Fonts.Medium, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Center,
            new float2(1f, 0f), float2.One, new float2(-28f, 0f), new float2(-8f, 0f)).Content.Value = "▾";
        var listingRow = new DropdownRow { Shell = shell, Chip = chip };
        chip.Button.ActOnRelease.Value = true;
        chip.Button.Clicked += (_, _) => listingRow.Open();
        return listingRow;
    }

    private sealed class DropdownRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsChip Chip;

        public void Open()
        {
            if (Item is not ListingDropdown choice || !choice.Interactable || choice.Options.Count == 0)
                return;
            // Capture the write delegate with this option snapshot. A device refresh must not turn an
            // index in the open list into a different device, nor can a recycled row change its target.
            var write = choice.Write;
            var request = new ModalRequest
            {
                Title = choice.LabelText,
                Message = choice.ListHint,
                SelectedChoice = choice.Read(),
                SearchChoices = true,
                CloseOnChoice = true,
                Invoker = Slot,
                ChoiceChanged = index =>
                {
                    write(index);
                    if (!View.IsDestroyed)
                        View.RefreshValues();
                },
            };
            for (int i = 0; i < choice.Options.Count; i++)
                request.AddChoice(choice.Options[i], i < choice.OptionDetails.Count ? choice.OptionDetails[i] : string.Empty);
            request.AddOption("Modal.Cancel".AsLocale("Cancel"));
            ModalHost.For(Slot)?.Open(request);
        }

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            var choice = (ListingDropdown)item;
            int selected = choice.Read();
            ListingStyle.SetText(Chip.Text, selected >= 0 && selected < choice.Options.Count ? choice.Options[selected] : "-");
            Chip.SetInteractable(choice.Interactable && choice.Options.Count > 0);
            Chip.SetPaint(DashTheme.Field, DashTheme.SurfaceHover, DashTheme.Outline,
                choice.Interactable ? DashTheme.Text : DashTheme.TextMuted);
        }
    }
}
