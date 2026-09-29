// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A segmented choice whose second line says what the picked option does. The line is read on every bind
// rather than fixed when the item was built, so it follows the answer when the host changes it.
internal sealed class SessionChoiceItem : ListingChoice
{
    public Func<LocaleText>? Hint;

    public SessionChoiceItem(string key, LocaleText label) : base(key, label) { }
}

// The settings screen's choice row with that live second line.
internal sealed class SessionChoiceTemplate : SettingsRowTemplate
{
    private const int InitialSegments = 5;

    public SessionChoiceTemplate(SettingsFonts fonts) : base(fonts) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = SettingsSegments.Strip(shell.Control);
        var listingRow = new ChoiceRow { Shell = shell, Segments = new SettingsSegments(strip, Fonts) };
        for (int i = 0; i < InitialSegments; i++)
            listingRow.Segments.Add(index => listingRow.Pick(index));
        return listingRow;
    }

    private sealed class ChoiceRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public required SettingsSegments Segments;

        public void Pick(int index)
        {
            if (Item is not ListingChoice choice || !choice.Interactable)
                return;
            if (index < 0 || index >= choice.Options.Count)
                return;
            choice.Write(index);
            Bind(choice);
        }

        public override void Bind(ListingItem item)
        {
            if (item is SessionChoiceItem { Hint: { } hint })
                item.DetailText = hint();
            Shell.Bind(item, useDetailAsHint: true);

            var choice = item as ListingChoice;
            int count = choice?.Options.Count ?? 0;
            Segments.Grow(count, index => Pick(index));
            int selected = choice?.Read() ?? -1;
            Segments.Layout(count);
            for (int i = 0; i < count; i++)
                Segments.Paint(i, choice!.Options[i], i == selected, item.Interactable);
        }
    }
}

// ONE PERSON IN THE SESSION
//
// Name over role, then two groups of controls measured back from the right edge. The far group is the
// host's moderation (mute, silence, respawn, kick, ban) and only exists on other people's rows on the
// host's machine. The near group is your own ears (how loud they are to you, or not at all), which only
// ever writes local state and so is on every row but your own. Roles are not changed here: the
// Permissions tab lays every role out as a pill, which beats a button that cycles through them blind.
// -xlinka
internal sealed class SessionUserRowTemplate : ListingRowTemplate
{
    public const float RowHeight = 56f;

    private const float NameWidth = 250f;
    private const float ControlGap = 6f;
    private const float GroupGap = 18f;
    private const float MuteWidth = 84f;
    private const float SilenceWidth = 92f;
    private const float RespawnWidth = 86f;
    private const float KickWidth = 64f;
    private const float BanWidth = 64f;
    private const float BlockWidth = 92f;
    private const float VolumeWidth = 120f;
    private const float VolumeValueWidth = 44f;

    private readonly SettingsFonts _fonts;
    private readonly SessionScreen _screen;

    public SessionUserRowTemplate(SettingsFonts fonts, SessionScreen screen)
    {
        _fonts = fonts;
        _screen = screen;
    }

    public override float Height => RowHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        float left = SettingsMetrics.RowInset;
        var name = SettingsUI.Label(row, "Name", _fonts.Medium, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0.5f), new float2(0f, 1f),
            new float2(left, 0f), new float2(left + NameWidth, -7f), shrinkToFit: true);
        var detail = SettingsUI.Label(row, "Detail", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0f, 0.5f),
            new float2(left, 7f), new float2(left + NameWidth, -2f), shrinkToFit: true);

        var listingRow = new UserRow
        {
            Background = background,
            Name = name,
            Detail = detail,
            Screen = _screen,
        };

        listingRow.Ban = Chip(row, "Ban", listingRow, user => _screen.RequestBan(user));
        listingRow.Kick = Chip(row, "Kick", listingRow, user => _screen.RequestKick(user));
        listingRow.Respawn = Chip(row, "Respawn", listingRow, user => _screen.Respawn(user));
        listingRow.Silence = Chip(row, "Silence", listingRow, user => _screen.ToggleSilenced(user));
        listingRow.Mute = Chip(row, "Mute", listingRow, user => _screen.ToggleMuted(user));
        listingRow.Block = Chip(row, "Block", listingRow, user => _screen.ToggleBlocked(user));

        listingRow.VolumeValue = SettingsUI.Label(row, "VolumeValue", _fonts.Body, DashTheme.FontSmall,
            DashTheme.TextDim, TextHorizontalAlignment.Right, new float2(1f, 0.5f), new float2(1f, 0.5f),
            float2.Zero, float2.Zero);

        var volumeHost = SettingsUI.Child(row, "Volume", new float2(1f, 0.5f), new float2(1f, 0.5f),
            float2.Zero, float2.Zero);
        builder.PushStyle();
        builder.ForegroundColor(DashTheme.Accent);
        builder.NestInto(volumeHost);
        var slider = builder.Slider(1f, 0f, 1f, null, DashTheme.Field);
        builder.NestOut();
        builder.PopStyle();
        // Slider() sizes itself for a layout cell; this row anchors instead, so pin it to its host.
        var sliderRect = SettingsUI.Rect(slider.Slot);
        sliderRect.AnchorMin.Value = float2.Zero;
        sliderRect.AnchorMax.Value = float2.One;
        sliderRect.OffsetMin.Value = float2.Zero;
        sliderRect.OffsetMax.Value = float2.Zero;
        var handle = slider.Slot.FindChild("HandleArea", recursive: false)
            ?.FindChild("Handle", recursive: false)?.GetComponent<ArcSegment>();
        if (handle != null)
            slider.AddColorDriver(handle.Tint, DashTheme.OnAccent);
        listingRow.Volume = slider;
        listingRow.VolumeHost = volumeHost;

        // Reads the row's CURRENT user: this instance is recycled down the whole roster.
        slider.ValueChanged += (_, value) =>
        {
            if (listingRow.Item?.Tag is not User user || user.IsDestroyed || user.IsLocal)
                return;
            user.LocalVolume = value;
            ListingStyle.SetText(listingRow.VolumeValue, SessionLocale.Percent(user.LocalVolume));
        };
        return listingRow;
    }

    private SettingsChip Chip(Slot row, string name, ListingRow listingRow, Action<User> press)
    {
        var host = SettingsUI.Child(row, name, new float2(1f, 0.5f), new float2(1f, 0.5f), float2.Zero, float2.Zero);
        var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        chip.Button.Clicked += (_, _) =>
        {
            if (listingRow.Item?.Tag is User user && !user.IsDestroyed)
                press(user);
        };
        host.ActiveSelf.Value = false;
        return chip;
    }

    private sealed class UserRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Name;
        public required Text Detail;
        public required SessionScreen Screen;

        public SettingsChip Ban = null!;
        public SettingsChip Kick = null!;
        public SettingsChip Respawn = null!;
        public SettingsChip Silence = null!;
        public SettingsChip Mute = null!;
        public SettingsChip Block = null!;
        public Text VolumeValue = null!;
        public Slider Volume = null!;
        public Slot VolumeHost = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            ListingStyle.SetText(Name, item.Label);

            var user = item.Tag as User;
            if (user == null || user.IsDestroyed)
            {
                ListingStyle.SetText(Detail, string.Empty);
                float none = SettingsMetrics.RowInset;
                Place(Ban.Slot, false, 0f, ref none);
                Place(Kick.Slot, false, 0f, ref none);
                Place(Respawn.Slot, false, 0f, ref none);
                Place(Silence.Slot, false, 0f, ref none);
                Place(Mute.Slot, false, 0f, ref none);
                Place(Block.Slot, false, 0f, ref none);
                Place(VolumeValue.Slot, false, 0f, ref none);
                Place(VolumeHost, false, 0f, ref none);
                return;
            }

            bool hostUser = Screen.IsHostUser(user);
            bool moderate = Screen.CanModerate(user);
            ListingStyle.SetText(Detail, Screen.DescribeUser(user, hostUser));

            float right = SettingsMetrics.RowInset;
            Place(Ban.Slot, moderate, BanWidth, ref right);
            Place(Kick.Slot, moderate, KickWidth, ref right);
            // Your own row keeps Respawn: getting yourself out of a wall works solo too.
            Place(Respawn.Slot, moderate || user.IsLocal, RespawnWidth, ref right);
            Place(Silence.Slot, moderate, SilenceWidth, ref right);
            Place(Mute.Slot, moderate, MuteWidth, ref right);

            bool ears = !user.IsLocal;
            if (ears && right > SettingsMetrics.RowInset)
                right += GroupGap - ControlGap;
            Place(Block.Slot, ears, BlockWidth, ref right);
            Place(VolumeValue.Slot, ears, VolumeValueWidth, ref right);
            Place(VolumeHost, ears, VolumeWidth, ref right, SettingsMetrics.SliderBand);

            if (moderate)
            {
                PaintState(Mute, user.IsMuted.Value, SessionLocale.Muted, SessionLocale.Mute);
                PaintState(Silence, user.IsSilenced.Value, SessionLocale.Silenced, SessionLocale.Silence);
                PaintDanger(Kick, PermissionLocale.Kick);
                PaintDanger(Ban, SessionLocale.Ban);
            }
            if (moderate || user.IsLocal)
                PaintPlain(Respawn, SessionLocale.Respawn);

            if (ears)
            {
                bool blocked = user.IsAudioLocallyBlocked;
                ListingStyle.SetText(Block.Text, (blocked ? SessionLocale.Blocked : SessionLocale.Block).Resolve());
                if (blocked)
                    Block.SetPaint(PermPaint.DangerFill, PermPaint.DangerHover, DashTheme.Outline, DashTheme.Negative);
                else
                    Block.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
                Block.SetInteractable(true);

                ListingStyle.SetSlider(Volume, 0f, 1f, user.LocalVolume);
                ListingStyle.SetInteractable(Volume, !blocked);
                ListingStyle.SetText(VolumeValue, SessionLocale.Percent(user.LocalVolume));
                ListingStyle.SetTextColor(VolumeValue, blocked ? DashTheme.TextMuted : DashTheme.TextDim);
            }
        }

        private static void PaintState(SettingsChip chip, bool on, LocaleText onLabel, LocaleText offLabel)
        {
            ListingStyle.SetText(chip.Text, (on ? onLabel : offLabel).Resolve());
            if (on)
                chip.SetPaint(DashTheme.AccentSoft, DashTheme.AccentSoft, DashTheme.Accent, DashTheme.Accent);
            else
                chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
            chip.SetInteractable(true);
        }

        private static void PaintDanger(SettingsChip chip, LocaleText label)
        {
            ListingStyle.SetText(chip.Text, label.Resolve());
            chip.SetPaint(PermPaint.DangerFill, PermPaint.DangerHover, DashTheme.Outline, DashTheme.Negative);
            chip.SetInteractable(true);
        }

        private static void PaintPlain(SettingsChip chip, LocaleText label)
        {
            ListingStyle.SetText(chip.Text, label.Resolve());
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
            chip.SetInteractable(true);
        }

        // Right to left off the row's edge, skipping whatever this row does not show, so a guest's row
        // has no hole where the host's buttons would have been.
        private static void Place(Slot slot, bool show, float width, ref float right,
            float height = SettingsMetrics.ControlBand)
        {
            ListingStyle.SetActive(slot, show);
            if (!show)
                return;
            SettingsUI.SetOffsets(SettingsUI.Rect(slot), new float2(-(right + width), -height * 0.5f),
                new float2(-right, height * 0.5f));
            right += width + ControlGap;
        }
    }
}
