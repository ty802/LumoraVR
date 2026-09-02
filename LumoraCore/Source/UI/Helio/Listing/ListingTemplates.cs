// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Helio.UI.Listing;

// A section title inside the list. Virtualization means groups cannot be nested containers any more -
// a header is just another row, which is also why it scrolls at exactly the right speed. -xlinka
public sealed class ListingHeader : ListingItem
{
    public ListingHeader(string key, LocaleText label) : base(key)
    {
        LabelText = label;
        Interactable = false;
    }
}

public static class ListingTemplates
{
    // Every built-in item type mapped to its house row. A screen adds its own templates on top and
    // overrides any of these by mapping the same type again.
    public static ListingTemplateMapper Standard(ListingStyle style)
    {
        var mapper = new ListingTemplateMapper { DefaultHeight = style.RowHeight };
        mapper.Map<ListingHeader>(new HeaderTemplate(style));
        mapper.Map<ListingCategory>(new CategoryTemplate(style));
        mapper.Map<ListingToggle>(new ToggleTemplate(style));
        mapper.Map<ListingSlider>(new SliderTemplate(style));
        mapper.Map<ListingChoice>(new ChoiceTemplate(style));
        mapper.Map<ListingAction>(new ActionTemplate(style));
        mapper.Map<ListingLabel>(new LabelTemplate(style));
        mapper.Fallback(new LabelTemplate(style));
        return mapper;
    }

    public abstract class StyledTemplate : ListingRowTemplate
    {
        protected readonly ListingStyle Style;
        protected StyledTemplate(ListingStyle style) => Style = style;
        public override float Height => Style.RowHeight;
    }

    // HEADER

    private sealed class HeaderTemplate : StyledTemplate
    {
        public HeaderTemplate(ListingStyle style) : base(style) { }

        public override bool UsesRowBackground => false;
        public override float Height => 32f;

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(120f).FlexibleWidth(1f);
            var text = Style.Label(builder, string.Empty, 20f, Style.HeaderText, TextHorizontalAlignment.Left);
            return new HeaderRow { Text = text };
        }

        private sealed class HeaderRow : ListingRow
        {
            public required Text Text;
            public override void Bind(ListingItem item) => ListingStyle.SetText(Text, item.Label);
        }
    }

    // CATEGORY

    private sealed class CategoryTemplate : StyledTemplate
    {
        public CategoryTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            var button = row.AttachComponent<Button>();

            builder.MinWidth(140f).FlexibleWidth(1f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);
            builder.MinWidth(70f).PreferredWidth(70f).FlexibleWidth(0f);
            var count = Style.Label(builder, string.Empty, Style.ValueSize, Style.TextDim, TextHorizontalAlignment.Right);
            builder.MinWidth(20f).PreferredWidth(20f).FlexibleWidth(0f);
            Style.Label(builder, "▶", Style.ValueSize, Style.TextDim, TextHorizontalAlignment.Center);

            var listingRow = new CategoryRow { Label = label, Count = count };
            button.Clicked += (_, _) => view.Activate(listingRow.Item);
            return listingRow;
        }

        private sealed class CategoryRow : ListingRow
        {
            public required Text Label;
            public required Text Count;

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                int entries = (item as ListingCategory)?.EntryCount?.Invoke() ?? -1;
                ListingStyle.SetText(Count, entries >= 0 ? entries.ToString() : item.Detail);
            }
        }
    }

    // LABEL

    private sealed class LabelTemplate : StyledTemplate
    {
        public LabelTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(Style.LabelWidth).PreferredWidth(Style.LabelWidth).FlexibleWidth(0f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);
            builder.MinWidth(80f).FlexibleWidth(1f);
            var value = Style.Label(builder, string.Empty, Style.ValueSize, Style.TextDim, TextHorizontalAlignment.Right);
            return new LabelRow { Label = label, Value = value };
        }

        private sealed class LabelRow : ListingRow
        {
            public required Text Label;
            public required Text Value;

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                ListingStyle.SetText(Value, item is ListingLabel listingLabel ? listingLabel.Value : item.Detail);
            }
        }
    }

    // TOGGLE

    private sealed class ToggleTemplate : StyledTemplate
    {
        public ToggleTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(Style.LabelWidth).PreferredWidth(Style.LabelWidth).FlexibleWidth(0f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);

            builder.MinWidth(28f).PreferredWidth(28f).FlexibleWidth(0f);
            var checkbox = builder.Checkbox(false);

            builder.MinWidth(60f).FlexibleWidth(1f);
            var state = Style.Label(builder, string.Empty, Style.ValueSize, Style.TextDim, TextHorizontalAlignment.Left);

            var listingRow = new ToggleRow { Label = label, Box = checkbox, State = state, Style = Style };
            // Reads the row's CURRENT item, never the one it was first built for: this instance is
            // recycled across the whole list.
            checkbox.ValueChanged += (_, isChecked) =>
            {
                if (listingRow.Item is not ListingToggle toggle || !toggle.Interactable)
                    return;
                toggle.Write(isChecked);
                bool applied = toggle.Read();
                ListingStyle.SetText(state, applied ? "On" : "Off");
                // The write can refuse or clamp; show what the setting actually holds, not the click.
                if (applied != isChecked)
                    checkbox.IsChecked.Value = applied;
            };
            return listingRow;
        }

        private sealed class ToggleRow : ListingRow
        {
            public required Text Label;
            public required Checkbox Box;
            public required Text State;
            public required ListingStyle Style;

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                bool value = item is ListingToggle toggle && toggle.Read();
                if (Box.IsChecked.Value != value)
                    Box.IsChecked.Value = value;
                ListingStyle.SetInteractable(Box, item.Interactable);
                ListingStyle.SetText(State, value ? "On" : "Off");
                ListingStyle.SetTextColor(State, item.Interactable ? Style.TextDim : Style.TextDisabled);
            }
        }
    }

    // SLIDER

    private sealed class SliderTemplate : StyledTemplate
    {
        public SliderTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(Style.LabelWidth).PreferredWidth(Style.LabelWidth).FlexibleWidth(0f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);

            var slider = builder.Slider(0f, 0f, 1f);
            // Slider() hard-sets a 96px cell; widen it or the track renders as a stub in a full row.
            var element = slider.Slot.GetComponent<LayoutElement>() ?? slider.Slot.AttachComponent<LayoutElement>();
            element.MinWidth.Value = 120f;
            element.PreferredWidth.Value = 240f;
            element.FlexibleWidth.Value = 1f;

            builder.MinWidth(Style.ValueWidth).PreferredWidth(Style.ValueWidth).FlexibleWidth(0f);
            var value = Style.Label(builder, string.Empty, Style.ValueSize, Style.TextDim, TextHorizontalAlignment.Right);

            var listingRow = new SliderRow { Label = label, Bar = slider, Value = value };
            slider.ValueChanged += (_, raw) =>
            {
                if (listingRow.Item is not ListingSlider item || !item.Interactable)
                    return;
                float snapped = item.Snap(raw);
                item.Write(snapped);
                float applied = item.Read();
                ListingStyle.SetText(value, item.Describe(applied));
                // The write can clamp or bucket the value; put the handle where the setting actually
                // landed instead of where the finger did.
                if (applied != raw)
                {
                    slider.Value.Value = applied;
                    slider.UpdateHandleDrives();
                }
            };
            return listingRow;
        }

        private sealed class SliderRow : ListingRow
        {
            public required Text Label;
            public required Slider Bar;
            public required Text Value;

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                if (item is not ListingSlider slider)
                {
                    ListingStyle.SetText(Value, item.Detail);
                    return;
                }
                float current = slider.Read();
                ListingStyle.SetSlider(Bar, slider.Min, slider.Max, current);
                ListingStyle.SetInteractable(Bar, item.Interactable);
                ListingStyle.SetText(Value, slider.Describe(current));
            }
        }
    }

    // CHOICE

    private sealed class ChoiceTemplate : StyledTemplate
    {
        // Segments are built on demand and never destroyed, only hidden: a recycled row lands on
        // items with different option counts and rebuilding the strip each time would churn slots
        // inside a live chunk every scroll step. -xlinka
        private const int InitialSegments = 4;

        public ChoiceTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(Style.LabelWidth).PreferredWidth(Style.LabelWidth).FlexibleWidth(0f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);

            var strip = row.AddSlot("Segments");
            strip.AttachComponent<RectTransform>();
            var element = strip.AttachComponent<LayoutElement>();
            element.MinWidth.Value = 160f;
            element.FlexibleWidth.Value = 1f;
            element.FlexibleHeight.Value = 1f;
            var layout = strip.AttachComponent<HorizontalLayout>();
            layout.Spacing.Value = 8f;
            layout.ForceExpandWidth.Value = true;
            layout.ForceExpandHeight.Value = true;

            var listingRow = new ChoiceRow { Label = label, Strip = strip, Style = Style };
            for (int i = 0; i < InitialSegments; i++)
                listingRow.AddSegment();
            return listingRow;
        }

        private sealed class ChoiceRow : ListingRow
        {
            public required Text Label;
            public required Slot Strip;
            public required ListingStyle Style;

            private readonly List<(Slot slot, BorderedImage background, Text text)> _segments = new();

            public void AddSegment()
            {
                int index = _segments.Count;
                var cell = Strip.AddSlot("Segment");
                cell.AttachComponent<RectTransform>();
                var element = cell.AttachComponent<LayoutElement>();
                element.MinWidth.Value = 56f;
                element.FlexibleWidth.Value = 1f;
                element.FlexibleHeight.Value = 1f;
                var background = Style.ApplyPanel(cell, Style.NeutralFill, Style.RowBorder);
                var button = cell.AttachComponent<Button>();
                var text = Style.FillLabel(cell, string.Empty, 15f, Style.TextPrimary);
                cell.ActiveSelf.Value = false;
                button.Clicked += (_, _) =>
                {
                    if (Item is not ListingChoice choice || !choice.Interactable)
                        return;
                    if (index >= choice.Options.Count)
                        return;
                    choice.Write(index);
                    Bind(choice);
                };
                _segments.Add((cell, background, text));
            }

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                var choice = item as ListingChoice;
                int count = choice?.Options.Count ?? 0;
                while (_segments.Count < count)
                    AddSegment();

                int selected = choice?.Read() ?? -1;
                for (int i = 0; i < _segments.Count; i++)
                {
                    var (slot, background, text) = _segments[i];
                    bool used = i < count;
                    ListingStyle.SetActive(slot, used);
                    if (!used)
                        continue;
                    ListingStyle.SetText(text, choice!.Options[i]);
                    bool active = i == selected;
                    ListingStyle.SetTint(background, !item.Interactable
                        ? Style.DisabledFill
                        : active ? Style.AccentFill : Style.NeutralFill);
                    ListingStyle.SetTextColor(text, item.Interactable ? Style.TextPrimary : Style.TextDisabled);
                    ListingStyle.SetInteractable(slot.GetComponent<Button>(), item.Interactable);
                }
            }
        }
    }

    // ACTION

    private sealed class ActionTemplate : StyledTemplate
    {
        public ActionTemplate(ListingStyle style) : base(style) { }

        public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
        {
            builder.MinWidth(Style.LabelWidth).FlexibleWidth(1f);
            var label = Style.Label(builder, string.Empty, Style.LabelSize, Style.TextPrimary, TextHorizontalAlignment.Left);

            var cell = row.AddSlot("Action");
            cell.AttachComponent<RectTransform>();
            ListingStyle.SetFixedWidth(cell, 132f);
            var background = Style.ApplyPanel(cell, Style.NeutralFill, Style.RowBorder);
            var button = cell.AttachComponent<Button>();
            var buttonText = Style.FillLabel(cell, string.Empty, 15f, Style.TextPrimary);

            var listingRow = new ActionRow { Label = label, Background = background, ButtonText = buttonText, Style = Style };
            button.Clicked += (_, _) =>
            {
                if (listingRow.Item is ListingAction action && action.Interactable)
                    action.Invoke();
            };
            return listingRow;
        }

        private sealed class ActionRow : ListingRow
        {
            public required Text Label;
            public required BorderedImage Background;
            public required Text ButtonText;
            public required ListingStyle Style;

            public override void Bind(ListingItem item)
            {
                ListingStyle.SetText(Label, item.Label);
                var action = item as ListingAction;
                ListingStyle.SetText(ButtonText, action != null ? action.ButtonLabel.Resolve() : "Apply");
                ListingStyle.SetTint(Background, !item.Interactable
                    ? Style.DisabledFill
                    : action != null && action.Destructive ? Style.WarningFill : Style.NeutralFill);
                ListingStyle.SetTextColor(ButtonText, item.Interactable ? Style.TextPrimary : Style.TextDisabled);
                ListingStyle.SetInteractable(Slot.FindChild("Action", recursive: false)?.GetComponent<Button>(), item.Interactable);
            }
        }
    }
}
