// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public sealed partial class ModalHost
{
    private const float CompactChoiceHeight = 44f;
    private const float CompactChoiceSpacing = 4f;
    private TextInput? _choiceSearch;

    private float2 AvailableSize
    {
        get
        {
            var rect = Slot.GetComponent<RectTransform>()?.LocalComputeRect ?? default;
            return new float2(rect.width > 0f ? rect.width : 720f, rect.height > 0f ? rect.height : 720f);
        }
    }

    private static float SearchableChromeHeight(ModalRequest request)
        => SidePadding * 2f + TitleHeight + PromptHeight + ButtonsHeight + RowSpacing * 3f
            + (request.Message.IsEmpty ? 0f : MessageHeight + RowSpacing)
            + (request.Prompt ? PromptHeight + RowSpacing : 0f);

    private float SearchableListHeight(ModalRequest request)
    {
        int visible = System.Math.Clamp(request.Choices.Count, 1, MaxVisibleChoices);
        float desired = visible * CompactChoiceHeight + (visible - 1) * CompactChoiceSpacing;
        return System.Math.Clamp(AvailableSize.y - 32f - SearchableChromeHeight(request), CompactChoiceHeight, desired);
    }

    private void BuildSearchableChoices(Slot panel, ModalRequest request, IAssetProvider<FontSet>? font,
        IAssetProvider<TextureAsset>? rounded)
    {
        var searchHost = panel.AddSlot("ChoiceSearch");
        searchHost.AttachComponent<RectTransform>();
        SetFixedHeight(searchHost, PromptHeight);
        var builder = new UIBuilder(searchHost);
        builder.Font(font).TextColor(TextPrimary).ForegroundColor(AccentFill).BackgroundColor(FieldFill);
        var search = builder.TextInput(string.Empty, "Modal.Search".AsLocale("Search…").Resolve());
        Fill(search.Slot.GetComponent<RectTransform>()!);
        // Do not summon the VR keyboard until the user actually wants to search.
        _choiceSearch = search;

        var host = panel.AddSlot("Choices");
        host.AttachComponent<RectTransform>();
        SetFixedHeight(host, SearchableListHeight(request));
        var style = new ListingStyle
        {
            Font = font,
            RoundedSprite = rounded,
            RowFill = NeutralFill,
            RowSelectedFill = AccentFill,
            RowBorder = PanelBorder,
            RowSpacing = CompactChoiceSpacing,
        };
        var source = new ListingItemSource();
        for (int i = 0; i < request.Choices.Count; i++)
        {
            var choice = request.Choices[i];
            source.AddRoot(new ListingLabel(i.ToString(), choice.Label) { DetailText = choice.Detail, Tag = i });
        }
        var templates = new ListingTemplateMapper().Map<ListingLabel>(new CompactChoiceTemplate());
        var view = ListingView.Attach(host, style, templates, source);
        view.SelectedKey = request.SelectedChoice.ToString();
        view.ItemActivated += (_, item) =>
        {
            if (ReferenceEquals(Current, request) && item.Tag is int index)
                SelectChoice(index);
        };

        var empty = AddFillLabel(host, "Modal.NoMatches".AsLocale("No matches"), 17f, TextDim, font);
        empty.Slot.ActiveSelf.Value = request.Choices.Count == 0;
        search.Text.OnChanged += _ =>
        {
            if (view.IsDestroyed)
                return;
            string query = search.Text.Value.Trim();
            view.Search = query;
            bool any = false;
            foreach (var item in source.ItemsAt(string.Empty))
            {
                if (ListingSearch.Matches(item, query))
                {
                    any = true;
                    break;
                }
            }
            ListingStyle.SetActive(empty.Slot, !any);
        };

        foreach (var chunk in panel.GetComponentsInChildren<GraphicChunkRoot>(false))
            chunk.OverlayLevel = 3;
    }

    private sealed class CompactChoiceTemplate : ListingRowTemplate
    {
        public override float Height => CompactChoiceHeight;
        public override void ConfigureRow(Slot row, ListingStyle style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            // Virtual rows are created after the modal opens, so each needs the modal render band.
            row.GetComponent<GraphicChunkRoot>()!.OverlayLevel = 3;
            var label = AddChoiceLabel(row, string.Empty, 16f, TextPrimary, view.Style.Font, TextHorizontalAlignment.Left,
                float2.Zero, float2.One, new float2(12f, 0f), new float2(-12f, 0f));
            var detail = AddChoiceLabel(row, string.Empty, 12f, TextDim, view.Style.Font, TextHorizontalAlignment.Left,
                float2.Zero, new float2(1f, 0.45f), new float2(12f, 1f), new float2(-12f, 0f));
            label.Ellipsis.Value = true;
            detail.Ellipsis.Value = true;
            var live = new CompactChoiceRow { Label = label, Detail = detail };
            var button = row.AttachComponent<Button>();
            button.ActOnRelease.Value = true;
            button.Clicked += (_, _) => view.Activate(live.Item);
            return live;
        }
    }

    private sealed class CompactChoiceRow : ListingRow
    {
        public required Text Label;
        public required Text Detail;

        public override void Bind(ListingItem item)
        {
            ListingStyle.SetText(Label, item.Label);
            ListingStyle.SetText(Detail, item.Detail);
            bool hasDetail = item.Detail.Length > 0;
            ListingStyle.SetActive(Detail.Slot, hasDetail);
            var rect = Label.Slot.GetComponent<RectTransform>()!;
            var anchor = new float2(0f, hasDetail ? 0.4f : 0f);
            if (!rect.AnchorMin.Value.Equals(anchor))
                rect.AnchorMin.Value = anchor;
        }

        public override void OnSelectionChanged()
        {
            ListingStyle.SetTextColor(Label, Selected ? DashTheme.OnAccent : TextPrimary);
            ListingStyle.SetTextColor(Detail, DetailColor(Selected));
        }
    }
}
