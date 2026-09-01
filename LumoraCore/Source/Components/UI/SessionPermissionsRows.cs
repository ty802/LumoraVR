// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Which line of the role matrix a row is. The ROLES are not on the item: the column set is read off the
// panel at bind time, so a mode switch that drops Admin and Builder re-columns every row at once without
// a single item being rebuilt. -xlinka
internal enum PermMatrixKind
{
    Header,
    Capability,
    MinScale,
    MaxScale,
}

internal sealed class PermMatrixItem : ListingItem
{
    public PermMatrixKind Row;
    public DataModelPermissionDomain Domain;

    // Header rows only: whether the "hand it all back to the world mode" pill rides along. Host-only, and
    // it lives on the item rather than on the template because the template is shared with every other
    // line of the matrix.
    public bool ShowReset;

    public PermMatrixItem(string key, PermMatrixKind row, LocaleText label) : base(key)
    {
        Row = row;
        LabelText = label;
        Kind = SessionPermissionsPanel.KindMatrix;
    }
}

// A pill row whose selected option can be an answer the host did not choose. Dimmed() saying true is the
// row telling the template "this came from the world mode", which paints the lit pill grey with a ring
// instead of in the accent - the same distinction the capability cells draw. -xlinka
internal sealed class PermChoiceItem : ListingChoice
{
    public Func<bool>? Dimmed;

    public PermChoiceItem(string key, LocaleText label) : base(key, label) { }
}

// Column table for the permission rows. Same reasoning as the settings rows: absolute columns anchored
// off the row edges, no layout controller, because the virtual list already owns the row rect and a
// controller inside it would fight the free-anchored placement.
internal static class PermMetrics
{
    public const float NoteHeight = 26f;
    public const float UserRowHeight = 48f;
    public const float HeaderRowHeight = 24f;
    public const float CapRowHeight = 52f;
    public const float ScaleRowHeight = 44f;

    public const float LabelWidth = 190f;
    public const float CellGap = 8f;
    public const float CapInset = 7f;
    public const float ScaleInset = 8f;

    // A capability cell is two pills and, when the world mode has taken the answer out of the host's
    // hands, one small line under them. The reset pill rides a band above the role names rather than
    // beside them, so the column table underneath stays exactly aligned with the rows it heads.
    public const float CapPillGap = 6f;
    public const float CapNoteHeight = 13f;
    public const float ResetBand = 24f;
    public const float ResetWidth = 164f;
    public const float ResetHeight = 18f;

    // User row, left to right, then the control block measured back from the right edge.
    public const float NameWidth = 210f;
    public const float YouLeft = 232f;
    public const float YouWidth = 40f;
    public const float HostLeft = 278f;
    public const float HostWidth = 48f;
    public const float RefusalLeft = 338f;
    public const float DotSize = 8f;
    public const float DotGap = 14f;

    public const float BanRight = 14f;
    public const float BanWidth = 86f;
    public const float KickRight = 108f;
    public const float KickWidth = 62f;
    public const float StripRight = 182f;
    public const float StripWidth = 300f;
    // The group marker sits immediately left of the role strip, so it reads as a note ON the role rather
    // than as one more control.
    public const float GroupMarkRight = StripRight + StripWidth + 10f;
    public const float GroupMarkWidth = 58f;
    public const float ControlBand = 28f;
    public const float TagBand = 20f;

    public static float CellsLeft => SettingsMetrics.RowInset + LabelWidth + CellGap;
}

// A single line of quiet text with no row behind it. The intro and the empty-roster line live here
// rather than in a label row: they are prose, not settings, and a pill around a sentence reads as a
// control nobody can press. Shrink-to-fit, never wrap, so a long world name cannot push the line into
// its neighbour's row the way the old notes did. -xlinka
internal sealed class PermNoteTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;

    public PermNoteTemplate(SettingsFonts fonts) => _fonts = fonts;

    public override float Height => PermMetrics.NoteHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var text = SettingsUI.Label(row, "Note", _fonts.Body, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(SettingsMetrics.RowInset, 0f), new float2(-SettingsMetrics.RowInset, 0f),
            shrinkToFit: true);
        return new NoteRow { Text = text };
    }

    private sealed class NoteRow : ListingRow
    {
        public required Text Text;

        public override void Bind(ListingItem item) => ListingStyle.SetText(Text, item.Label);
    }
}

// A strip of pills that divide their band evenly, pressed through the relay.
//
// The settings screen has one of these already, but its pills fire a closure. A button in this panel
// binds a component method instead (see SessionPermissionRelay), so the strip has to hand the relay the
// ROW and the column index at build time and let the relay read the item off the row when it fires: the
// rows are pooled and land on a different user or a different setting every scroll step. -xlinka
internal sealed class PermSegments
{
    // Short of the control column's right edge. Pills that ran the full width came out as two slabs on a
    // two-option row.
    private const float StripSpan = 0.74f;

    private readonly Slot _strip;
    private readonly SettingsFonts _fonts;
    private readonly SessionPermissionRelay _relay;
    private readonly ListingRow _row;
    private readonly Action<Button, UIInteractionContext> _action;
    private readonly List<SettingsChip> _chips = new();

    public PermSegments(Slot strip, SettingsFonts fonts, SessionPermissionRelay relay, ListingRow row,
        Action<Button, UIInteractionContext> action)
    {
        _strip = strip;
        _fonts = fonts;
        _relay = relay;
        _row = row;
        _action = action;
    }

    public static Slot Strip(Slot control)
        => SettingsUI.Child(control, "Segments", new float2(0f, 0.5f), new float2(StripSpan, 0.5f),
            new float2(0f, -PermMetrics.ControlBand * 0.5f), new float2(0f, PermMetrics.ControlBand * 0.5f));

    // A fixed-width control band measured back from the row's right edge.
    public static Slot Band(Slot row, string name, float right, float width)
        => SettingsUI.Child(row, name, new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-(right + width), -PermMetrics.ControlBand * 0.5f),
            new float2(-right, PermMetrics.ControlBand * 0.5f));

    // Built on demand and never destroyed, only hidden: a recycled row lands on items with different
    // option counts and churning slots inside a live chunk every scroll step is what this list exists to
    // avoid.
    public void Grow(int count)
    {
        while (_chips.Count < count)
        {
            int index = _chips.Count;
            var host = SettingsUI.Child(_strip, "Segment", float2.Zero, float2.One, float2.Zero, float2.Zero);
            var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
            host.ActiveSelf.Value = false;
            _relay.Register(chip.Button, _row, index);
            chip.Button.SetAction(_action);
            _chips.Add(chip);
        }
    }

    // Fractional anchors so any option count fills the band exactly; a fixed pill width either overflows
    // the column or leaves a gap that changes with the count.
    public void Layout(int count)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            bool used = i < count;
            ListingStyle.SetActive(_chips[i].Slot, used);
            if (!used)
                continue;
            var rect = SettingsUI.Rect(_chips[i].Slot);
            SettingsUI.SetAnchors(rect, new float2(i / (float)count, 0f), new float2((i + 1) / (float)count, 1f));
            SettingsUI.SetOffsets(rect, float2.Zero, new float2(-SettingsMetrics.SegmentGap, 0f));
        }
    }

    // dimmed says the selected pill is an answer the host never chose - it is the one the world mode
    // hands out. Painted grey with a ring instead of in the accent, so "this is what happens" and "this is
    // what I set" stay two different-looking things across the whole page. -xlinka
    public void Paint(int index, string label, bool active, bool interactable, bool dimmed = false)
    {
        if (index < 0 || index >= _chips.Count)
            return;
        var chip = _chips[index];
        ListingStyle.SetText(chip.Text, label);
        if (!interactable)
        {
            // Read-only still says which one is on. Washing the whole strip grey hides the answer the
            // guest came here to read.
            var fill = active && !dimmed ? DashTheme.AccentSoft : DashTheme.Field;
            chip.SetPaint(fill, fill, DashTheme.Outline, active ? DashTheme.Text : DashTheme.TextMuted,
                active ? PermPaint.LitRing : DashTheme.OutlineWidth);
        }
        else if (active && dimmed)
        {
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim,
                PermPaint.LitRing);
        }
        else if (active)
        {
            chip.SetPaint(DashTheme.Accent, DashTheme.AccentHover, DashTheme.Accent, DashTheme.OnAccent);
        }
        else
        {
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
        }
        chip.SetInteractable(interactable);
    }
}

// A matrix cell: one pill carrying the configured state and, under it, what the gate will actually
// answer. Two lines because they are two different facts - the top one is what the host chose, the
// bottom one is what a user in that role gets, and a world mode floor moves the second without touching
// the first. -xlinka
internal sealed class PermCell
{
    public readonly Slot Slot;
    public readonly RoundedPanel Panel;
    public readonly Text Title;
    public readonly Text Note;
    public readonly Button Button;

    private readonly RectTransform _titleRect;
    private color _fill = DashTheme.Surface;
    private color _hoverFill = DashTheme.SurfaceHover;
    private color _outline = DashTheme.Outline;
    private bool _hovered;

    private PermCell(Slot slot, RoundedPanel panel, Text title, Text note, Button button, RectTransform titleRect)
    {
        Slot = slot;
        Panel = panel;
        Title = title;
        Note = note;
        Button = button;
        _titleRect = titleRect;
    }

    public static PermCell Build(Slot host, SettingsFonts fonts)
    {
        // Panel before Button: Button.OnAttach adopts an Image on its own slot into a color driver and a
        // driven tint can no longer be written from Bind. RoundedPanel is not an Image.
        var panel = SettingsUI.Panel(host, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
        var button = host.AttachComponent<Button>();
        var title = SettingsUI.FillLabel(host, "State", fonts.Medium, DashTheme.FontSmall, DashTheme.Text,
            TextHorizontalAlignment.Center, shrinkToFit: true);
        var note = SettingsUI.Label(host, "Effective", fonts.Body, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Center, float2.Zero, new float2(1f, 0f), new float2(0f, 3f), new float2(0f, 15f));
        note.Slot.ActiveSelf.Value = false;

        var cell = new PermCell(host, panel, title, note, button, SettingsUI.Rect(title.Slot));
        button.HoverEntered += _ => cell.SetHovered(true);
        button.HoverExited += _ => cell.SetHovered(false);
        return cell;
    }

    public void SetPaint(in color fill, in color hoverFill, in color outline, in color textColor)
    {
        _fill = fill;
        _hoverFill = hoverFill;
        _outline = outline;
        ListingStyle.SetTextColor(Title, textColor);
        Repaint();
    }

    // The title centres in the whole cell with no second line and in the space above it with one, so a
    // capability cell and a scale cell still share a baseline.
    public void SetNote(string value, in color noteColor)
    {
        bool has = value.Length > 0;
        ListingStyle.SetActive(Note.Slot, has);
        if (has)
        {
            ListingStyle.SetText(Note, value);
            ListingStyle.SetTextColor(Note, noteColor);
        }
        SettingsUI.SetOffsets(_titleRect, new float2(0f, has ? 12f : 0f), float2.Zero);
    }

    public void SetInteractable(bool value)
    {
        ListingStyle.SetInteractable(Button, value);
        if (!value && _hovered)
        {
            _hovered = false;
            Repaint();
        }
    }

    private void SetHovered(bool value)
    {
        if (_hovered == value)
            return;
        _hovered = value;
        Repaint();
    }

    private void Repaint()
        => SettingsUI.SetPaint(Panel, _hovered && Button.Interactable.Value ? _hoverFill : _fill, _outline);
}

// A CAPABILITY CELL: Yes | No
//
// The owner's read of the old cell was "Default is confusing, it's like yes or no?", and he was right -
// it was three states dressed as one word, and the word was the setting rather than the outcome. So the
// cell says the outcome: two pills, and the lit one is what a user in that role actually gets, asked of
// the gate itself rather than inferred from the toggle. Press either one and it becomes a decision.
//
// The tri-state did not go anywhere. It is still what the config stores and what the gate resolves; what
// changed is that Inherit stopped being a thing you press and became a thing you can SEE - the lit pill
// goes grey with a ring instead of taking a colour, which is what the legend line means by "dimmed". A
// mode ceiling is the one answer nobody can press past, so both pills go dead and the cell says why.
// -xlinka
internal sealed class PermCapCell
{
    public readonly Slot Slot;
    public readonly SettingsChip Allow;
    public readonly SettingsChip Deny;

    private readonly Text _note;
    private readonly RectTransform _pillsRect;

    private PermCapCell(Slot slot, SettingsChip allow, SettingsChip deny, Text note, RectTransform pillsRect)
    {
        Slot = slot;
        Allow = allow;
        Deny = deny;
        _note = note;
        _pillsRect = pillsRect;
    }

    public static PermCapCell Build(Slot host, SettingsFonts fonts)
    {
        var pills = SettingsUI.Child(host, "Pills", float2.Zero, float2.One, float2.Zero, float2.Zero);
        float half = PermMetrics.CapPillGap * 0.5f;

        var allowHost = SettingsUI.Child(pills, "Yes", float2.Zero, new float2(0.5f, 1f),
            float2.Zero, new float2(-half, 0f));
        var allow = SettingsChip.Build(allowHost, fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);

        var denyHost = SettingsUI.Child(pills, "No", new float2(0.5f, 0f), float2.One,
            new float2(half, 0f), float2.Zero);
        var deny = SettingsChip.Build(denyHost, fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);

        var note = SettingsUI.Label(host, "Note", fonts.Body, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Center, float2.Zero, new float2(1f, 0f), float2.Zero,
            new float2(0f, PermMetrics.CapNoteHeight));
        note.Slot.ActiveSelf.Value = false;

        return new PermCapCell(host, allow, deny, note, SettingsUI.Rect(pills));
    }

    // allowed is the gate's own answer for this role, so it is what lights. state is only how the answer
    // was arrived at, which is what decides whether the lit pill takes a colour or stays grey.
    public void Paint(string yes, string no, bool allowed, DataModelPermissionToggle state, bool forced,
        bool interactable, string note, in color noteColor)
    {
        bool hasNote = note.Length > 0;
        ListingStyle.SetActive(_note.Slot, hasNote);
        if (hasNote)
        {
            ListingStyle.SetText(_note, note);
            ListingStyle.SetTextColor(_note, noteColor);
        }
        // The pills centre in the whole cell with no note and in the space above it with one, so a cell
        // that has something to say still lines up with the ones that do not.
        SettingsUI.SetOffsets(_pillsRect, new float2(0f, hasNote ? PermMetrics.CapNoteHeight : 0f), float2.Zero);

        // A choice only counts as the host's when it is also the answer. A Grant the world mode overruled
        // is not something to paint green: the role does not get it, and the cell has to say so.
        bool chosen = !forced
            && ((state == DataModelPermissionToggle.Grant && allowed)
                || (state == DataModelPermissionToggle.Deny && !allowed));
        bool live = interactable && !forced;

        PaintPill(Allow, yes, allowed, chosen, positive: true, live);
        PaintPill(Deny, no, !allowed, chosen, positive: false, live);
    }

    private static void PaintPill(SettingsChip chip, string label, bool lit, bool chosen, bool positive, bool live)
    {
        ListingStyle.SetText(chip.Text, label);
        if (!lit)
        {
            // The answer this role does not get. Quiet enough that the eye lands on the other one, still
            // readable enough to be worth pressing.
            chip.SetPaint(DashTheme.Field, DashTheme.SurfaceHover, color.Transparent, DashTheme.TextMuted);
        }
        else if (chosen)
        {
            chip.SetPaint(positive ? PermPaint.AllowFill : PermPaint.DenyFill,
                positive ? PermPaint.AllowHover : PermPaint.DenyHover,
                positive ? DashTheme.Positive : DashTheme.Negative,
                positive ? DashTheme.Positive : DashTheme.Negative,
                PermPaint.LitRing);
        }
        else
        {
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim,
                PermPaint.LitRing);
        }
        chip.SetInteractable(live);
    }
}

// ONE SESSION USER
//
// Name, the two tags that can apply to it, what the session has refused them lately, the role, and the
// two escalations. Role pills and the escalations only exist for the host, and never on the host's own
// row: the engine never reassigns or escalates against the host, so offering it would be a control that
// cannot fire.
internal sealed class PermUserRowTemplate : ListingRowTemplate
{
    private const int MaxRoles = 6;

    private readonly SettingsFonts _fonts;
    private readonly SessionPermissionsPanel _panel;
    private readonly SessionPermissionRelay _relay;

    public PermUserRowTemplate(SettingsFonts fonts, SessionPermissionsPanel panel, SessionPermissionRelay relay)
    {
        _fonts = fonts;
        _panel = panel;
        _relay = relay;
    }

    public override float Height => PermMetrics.UserRowHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var nameColumn = SettingsUI.Column(row, "Name", SettingsMetrics.RowInset, PermMetrics.NameWidth);
        var name = SettingsUI.FillLabel(nameColumn, "Text", _fonts.Medium, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, shrinkToFit: true);

        var you = BuildTag(row, "You", PermMetrics.YouLeft, PermMetrics.YouWidth, DashTheme.Accent);
        var hostTag = BuildTag(row, "Host", PermMetrics.HostLeft, PermMetrics.HostWidth, DashTheme.TextDim);

        // Dot and count sit together at the left of the gap, not right-aligned against the pills: the
        // count is a sentence about the person, so it reads out of the name, not out of the controls.
        var refusals = SettingsUI.Child(row, "Refusals", float2.Zero, float2.One,
            new float2(PermMetrics.RefusalLeft, 0f),
            new float2(-(PermMetrics.GroupMarkRight + PermMetrics.GroupMarkWidth + 12f), 0f));
        var dotSlot = SettingsUI.Child(refusals, "Dot", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(0f, -PermMetrics.DotSize * 0.5f), new float2(PermMetrics.DotSize, PermMetrics.DotSize * 0.5f));
        var dot = SettingsUI.Panel(dotSlot, DashTheme.Warning, color.Transparent, PermMetrics.DotSize * 0.5f);
        var refusalText = SettingsUI.Label(refusals, "Text", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(PermMetrics.DotGap, 0f), float2.Zero, shrinkToFit: true);

        var roleText = SettingsUI.Label(row, "Role", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 1f),
            new float2(-(PermMetrics.StripRight + PermMetrics.StripWidth), 0f),
            new float2(-PermMetrics.StripRight, 0f), shrinkToFit: true);

        // WHY THIS PERSON LANDS WHERE THEY LAND, in the plate's muted colour and nothing louder. A dot and
        // the word "group": it says they are on the group's roster, not that they hold any office here.
        // Anything that read as a title would be mistaken for staff, and staff is not what this is. -xlinka
        var groupMark = SettingsUI.Child(row, "GroupMark", new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-(PermMetrics.GroupMarkRight + PermMetrics.GroupMarkWidth), -PermMetrics.TagBand * 0.5f),
            new float2(-PermMetrics.GroupMarkRight, PermMetrics.TagBand * 0.5f));
        var groupDotSlot = SettingsUI.Child(groupMark, "Dot", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(0f, -PermMetrics.DotSize * 0.5f),
            new float2(PermMetrics.DotSize, PermMetrics.DotSize * 0.5f));
        var groupDot = SettingsUI.Panel(groupDotSlot, DashTheme.TextMuted, color.Transparent,
            PermMetrics.DotSize * 0.5f);
        var groupText = SettingsUI.Label(groupMark, "Text", _fonts.Body, DashTheme.FontSmall,
            DashTheme.TextMuted, TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(PermMetrics.DotSize + 6f, 0f), float2.Zero, shrinkToFit: true);
        groupMark.ActiveSelf.Value = false;

        var listingRow = new UserRow
        {
            Background = background,
            Name = name,
            You = you,
            HostTag = hostTag,
            Refusals = refusals,
            Dot = dot,
            RefusalText = refusalText,
            RoleText = roleText,
            GroupMark = groupMark,
            GroupDot = groupDot,
            GroupText = groupText,
            Panel = _panel,
        };

        var strip = PermSegments.Band(row, "Roles", PermMetrics.StripRight, PermMetrics.StripWidth);
        listingRow.Strip = strip;
        listingRow.Roles = new PermSegments(strip, _fonts, _relay, listingRow, _relay.OnRolePillPressed);
        listingRow.Roles.Grow(MaxRoles);

        listingRow.Kick = BuildAction(row, listingRow, "Kick", PermMetrics.KickRight, PermMetrics.KickWidth,
            _relay.OnKickPressed);
        listingRow.Ban = BuildAction(row, listingRow, "Ban", PermMetrics.BanRight, PermMetrics.BanWidth,
            _relay.OnTempBanPressed);
        return listingRow;
    }

    private Tag BuildTag(Slot row, string name, float left, float width, in color tint)
    {
        var host = SettingsUI.Child(row, name, new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(left, -PermMetrics.TagBand * 0.5f), new float2(left + width, PermMetrics.TagBand * 0.5f));
        SettingsUI.Panel(host, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusChip);
        var text = SettingsUI.FillLabel(host, "Text", _fonts.Medium, DashTheme.FontLabel, tint);
        host.ActiveSelf.Value = false;
        return new Tag { Slot = host, Text = text };
    }

    private SettingsChip BuildAction(Slot row, ListingRow listingRow, string name, float right, float width,
        Action<Button, UIInteractionContext> action)
    {
        var host = PermSegments.Band(row, name, right, width);
        var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, -1);
        chip.Button.SetAction(action);
        host.ActiveSelf.Value = false;
        return chip;
    }

    internal sealed class Tag
    {
        public required Slot Slot;
        public required Text Text;
    }

    private sealed class UserRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Name;
        public required Tag You;
        public required Tag HostTag;
        public required Slot Refusals;
        public required RoundedPanel Dot;
        public required Text RefusalText;
        public required Text RoleText;
        public required Slot GroupMark;
        public required RoundedPanel GroupDot;
        public required Text GroupText;
        public required SessionPermissionsPanel Panel;

        public Slot Strip = null!;
        public PermSegments Roles = null!;
        public SettingsChip Kick = null!;
        public SettingsChip Ban = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            ListingStyle.SetText(Name, item.Label);

            var user = item.Tag as User;
            if (user == null || user.IsDestroyed)
            {
                ListingStyle.SetActive(You.Slot, false);
                ListingStyle.SetActive(HostTag.Slot, false);
                ListingStyle.SetActive(Refusals, false);
                ListingStyle.SetActive(Strip, false);
                ListingStyle.SetActive(Kick.Slot, false);
                ListingStyle.SetActive(Ban.Slot, false);
                ListingStyle.SetActive(GroupMark, false);
                ListingStyle.SetText(RoleText, string.Empty);
                return;
            }

            bool isHostRow = Panel.IsHostUser(user);
            bool controls = Panel.IsHost && !isHostRow;

            ListingStyle.SetText(You.Text, PermissionLocale.You.Resolve());
            ListingStyle.SetActive(You.Slot, user.IsLocal);
            ListingStyle.SetText(HostTag.Text, PermissionLocale.RoleHost.Resolve());
            ListingStyle.SetActive(HostTag.Slot, isHostRow);

            BindRefusals(user);

            bool onRoster = !isHostRow && Panel.IsGroupActor(user);
            ListingStyle.SetActive(GroupMark, onRoster);
            if (onRoster)
            {
                ListingStyle.SetText(GroupText, PermissionLocale.GroupMark.Resolve());
                ListingStyle.SetTextColor(GroupText, DashTheme.TextMuted);
                SettingsUI.SetPaint(GroupDot, DashTheme.TextMuted, color.Transparent);
            }

            ListingStyle.SetActive(Strip, controls);
            ListingStyle.SetActive(Kick.Slot, controls);
            ListingStyle.SetActive(Ban.Slot, controls);
            // The role still reads as text wherever the pills are gone, so a guest sees what a role IS
            // even though they cannot reach for it. The host's own row already carries the Host tag, so
            // it gets no role text: "Host  Host" was the first thing the owner noticed.
            ListingStyle.SetActive(RoleText.Slot, !controls && !isHostRow);

            var role = Panel.RoleOf(user);
            if (!controls)
            {
                ListingStyle.SetText(RoleText, isHostRow ? string.Empty : role?.Name ?? string.Empty);
                ListingStyle.SetTextColor(RoleText, DashTheme.TextDim);
                return;
            }

            var roles = Panel.AssignableRoles();
            Roles.Grow(roles.Count);
            Roles.Layout(roles.Count);
            for (int i = 0; i < roles.Count; i++)
                Roles.Paint(i, roles[i].Name, role != null && ReferenceEquals(role, roles[i]), true);

            ListingStyle.SetText(Kick.Text, PermissionLocale.Kick.Resolve());
            Kick.SetPaint(PermPaint.DangerFill, PermPaint.DangerHover, DashTheme.Outline, DashTheme.Negative);
            Kick.SetInteractable(true);
            ListingStyle.SetText(Ban.Text, PermissionLocale.TempBan.Resolve());
            Ban.SetPaint(PermPaint.DangerFill, PermPaint.DangerHover, DashTheme.Outline, DashTheme.Negative);
            Ban.SetInteractable(true);
        }

        // Only shown once the ledger has actually seen something. A permanent "0 refusals" next to every
        // name reads as an accusation nobody earned.
        private void BindRefusals(User user)
        {
            int score = (int)MathF.Round((float)Panel.DenialScore(user));
            if (score <= 0)
            {
                ListingStyle.SetActive(Refusals, false);
                return;
            }
            Panel.DenialThresholds(out float warn, out float kick);
            var tint = score >= kick ? DashTheme.Negative : score >= warn ? DashTheme.Warning : DashTheme.TextDim;
            ListingStyle.SetActive(Refusals, true);
            ListingStyle.SetText(RefusalText, PermissionLocale.Refusals(score).Resolve());
            ListingStyle.SetTextColor(RefusalText, tint);
            SettingsUI.SetPaint(Dot, tint, color.Transparent);
        }
    }
}

// SEGMENTED PILL ROW
//
// A ListingChoice drawn as the same pill group the user rows use, so "New joiners get" and the violation
// response read as one control family with the roles.
internal sealed class PermPillRowTemplate : SettingsRowTemplate
{
    private const int InitialSegments = 6;

    private readonly SessionPermissionRelay _relay;

    public PermPillRowTemplate(SettingsFonts fonts, SessionPermissionRelay relay) : base(fonts)
        => _relay = relay;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var shell = BuildShell(row, clickable: false);
        var strip = PermSegments.Strip(shell.Control);
        var listingRow = new PillRow { Shell = shell };
        listingRow.Segments = new PermSegments(strip, Fonts, _relay, listingRow, _relay.OnPillPressed);
        listingRow.Segments.Grow(InitialSegments);
        return listingRow;
    }

    private sealed class PillRow : ListingRow
    {
        public required SettingsRowShell Shell;
        public PermSegments Segments = null!;

        public override void Bind(ListingItem item)
        {
            Shell.Bind(item, useDetailAsHint: true);
            var choice = item as ListingChoice;
            int count = choice?.Options.Count ?? 0;
            Segments.Grow(count);
            Segments.Layout(count);
            int selected = choice?.Read() ?? -1;
            bool dimmed = (item as PermChoiceItem)?.Dimmed?.Invoke() ?? false;
            for (int i = 0; i < count; i++)
                Segments.Paint(i, choice!.Options[i], i == selected, item.Interactable, dimmed);
        }
    }
}

internal static class PermPaint
{
    // The lit answer wears a real ring, not the hairline every other panel edge uses: at a glance the
    // ring is what separates "this is the answer" from "this is the other option".
    public const float LitRing = 2f;

    public static readonly color AllowFill = DashTheme.Positive.WithAlpha(0.16f);
    public static readonly color AllowHover = DashTheme.Positive.WithAlpha(0.26f);
    public static readonly color DenyFill = DashTheme.Negative.WithAlpha(0.16f);
    public static readonly color DenyHover = DashTheme.Negative.WithAlpha(0.26f);
    public static readonly color DangerFill = DashTheme.Negative.WithAlpha(0.14f);
    public static readonly color DangerHover = DashTheme.Negative.WithAlpha(0.26f);
}

// THE ROLE MATRIX
//
// One template for every line of it: the role header, the five capabilities and the two scale bounds.
// One template rather than three because they share the column table, and a column table that is not
// literally the same code drifts by a pixel and stops reading as a table. -xlinka
internal sealed class PermMatrixRowTemplate : ListingRowTemplate
{
    private const int MaxColumns = 6;

    private readonly SettingsFonts _fonts;
    private readonly SessionPermissionsPanel _panel;
    private readonly SessionPermissionRelay _relay;

    public PermMatrixRowTemplate(SettingsFonts fonts, SessionPermissionsPanel panel, SessionPermissionRelay relay)
    {
        _fonts = fonts;
        _panel = panel;
        _relay = relay;
    }

    public override float Height => PermMetrics.CapRowHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override float HeightFor(ListingItem item) => item is PermMatrixItem matrix
        ? matrix.Row switch
        {
            PermMatrixKind.Header => matrix.ShowReset
                ? PermMetrics.HeaderRowHeight + PermMetrics.ResetBand
                : PermMetrics.HeaderRowHeight,
            PermMatrixKind.Capability => PermMetrics.CapRowHeight,
            _ => PermMetrics.ScaleRowHeight,
        }
        : PermMetrics.CapRowHeight;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var labelColumn = SettingsUI.Column(row, "Label", SettingsMetrics.RowInset, PermMetrics.LabelWidth);
        var label = SettingsUI.FillLabel(labelColumn, "Text", _fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, shrinkToFit: true);

        var cells = SettingsUI.Child(row, "Cells", float2.Zero, float2.One,
            new float2(PermMetrics.CellsLeft, 0f), new float2(-SettingsMetrics.RowInset, 0f));

        var listingRow = new MatrixRow
        {
            Background = background,
            Label = label,
            CellsRect = SettingsUI.Rect(cells),
            Panel = _panel,
        };

        for (int i = 0; i < MaxColumns; i++)
        {
            // One column host carrying both cell shapes, one of which is hidden at any moment. Built once
            // and swapped by Bind rather than built per row kind: the columns have to line up across the
            // header, the five capabilities and the two bounds, and the only way that stays true under a
            // pooled row is for all three to come out of the same column table. -xlinka
            var host = SettingsUI.Child(cells, "Cell", float2.Zero, float2.One, float2.Zero, float2.Zero);
            host.ActiveSelf.Value = false;

            var singleHost = SettingsUI.Child(host, "Single", float2.Zero, float2.One, float2.Zero, float2.Zero);
            var single = PermCell.Build(singleHost, _fonts);
            _relay.Register(single.Button, listingRow, i);
            single.Button.SetAction(_relay.OnMatrixCellPressed);

            var capHost = SettingsUI.Child(host, "Caps", float2.Zero, float2.One, float2.Zero, float2.Zero);
            var caps = PermCapCell.Build(capHost, _fonts);
            _relay.Register(caps.Allow.Button, listingRow, i);
            caps.Allow.Button.SetAction(_relay.OnCapAllowPressed);
            _relay.Register(caps.Deny.Button, listingRow, i);
            caps.Deny.Button.SetAction(_relay.OnCapDenyPressed);

            listingRow.Columns.Add(new Column { Host = host, Single = single, Caps = caps });
        }

        // Anchored to the row's top-right, above the role names rather than beside them. Beside them it
        // would have had to eat into the column band, and a header that no longer sits over its own column
        // stops being a table. -xlinka
        var resetHost = SettingsUI.Child(row, "Reset", new float2(1f, 1f), new float2(1f, 1f),
            new float2(-(SettingsMetrics.RowInset + PermMetrics.ResetWidth), -PermMetrics.ResetHeight),
            new float2(-SettingsMetrics.RowInset, 0f));
        listingRow.Reset = SettingsChip.Build(resetHost, _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);
        resetHost.ActiveSelf.Value = false;
        _relay.Register(listingRow.Reset.Button, listingRow, -1);
        listingRow.Reset.Button.SetAction(_relay.OnResetCapsPressed);
        return listingRow;
    }

    // The two shapes a column can take, on one host.
    internal sealed class Column
    {
        public required Slot Host;
        public required PermCell Single;
        public required PermCapCell Caps;
    }

    private sealed class MatrixRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Label;
        public required RectTransform CellsRect;
        public required SessionPermissionsPanel Panel;

        public SettingsChip Reset = null!;
        public readonly List<Column> Columns = new();

        public override void Bind(ListingItem item)
        {
            var matrix = item as PermMatrixItem;
            var kind = matrix?.Row ?? PermMatrixKind.Capability;
            bool header = kind == PermMatrixKind.Header;
            bool showReset = header && (matrix?.ShowReset ?? false);

            SettingsUI.SetPaint(Background,
                header ? color.Transparent : DashTheme.Surface,
                header ? color.Transparent : DashTheme.Outline);
            ListingStyle.SetText(Label, item.Label);

            ListingStyle.SetActive(Reset.Slot, showReset);
            if (showReset)
            {
                ListingStyle.SetText(Reset.Text, PermissionLocale.CapsReset.Resolve());
                Reset.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
                Reset.SetInteractable(true);
            }

            float inset = header ? 0f : kind == PermMatrixKind.Capability ? PermMetrics.CapInset : PermMetrics.ScaleInset;
            // The reset band comes off the TOP of the header row, so the role names keep sitting directly
            // over the columns they name.
            float top = showReset ? PermMetrics.ResetBand : 0f;
            SettingsUI.SetOffsets(CellsRect, new float2(PermMetrics.CellsLeft, inset),
                new float2(-SettingsMetrics.RowInset, -(inset + top)));

            var roles = Panel.AssignableRoles();
            int count = System.Math.Min(roles.Count, Columns.Count);
            for (int i = 0; i < Columns.Count; i++)
            {
                var column = Columns[i];
                bool used = i < count;
                ListingStyle.SetActive(column.Host, used);
                if (!used)
                    continue;

                var rect = SettingsUI.Rect(column.Host);
                SettingsUI.SetAnchors(rect, new float2(i / (float)count, 0f), new float2((i + 1) / (float)count, 1f));
                SettingsUI.SetOffsets(rect, float2.Zero, new float2(-PermMetrics.CellGap, 0f));
                BindColumn(column, matrix, kind, roles[i], item.Interactable);
            }
        }

        private void BindColumn(Column column, PermMatrixItem? matrix, PermMatrixKind kind,
            DataModelPermissionRole role, bool interactable)
        {
            bool capability = kind == PermMatrixKind.Capability;
            ListingStyle.SetActive(column.Single.Slot, !capability);
            ListingStyle.SetActive(column.Caps.Slot, capability);

            if (capability)
            {
                var domain = matrix?.Domain ?? DataModelPermissionDomain.Spawn;
                // Re-read every bind: a mode ceiling, a role reassignment or another peer's config delta
                // can move the answer without this item having changed at all.
                bool allowed = Panel.CapAllows(role, domain);
                bool forced = Panel.ModeForcesOff(domain);
                column.Caps.Paint(
                    PermissionLocale.ToggleAllow.Resolve(),
                    PermissionLocale.ToggleDeny.Resolve(),
                    allowed,
                    Panel.CapState(role.Name, domain),
                    forced,
                    interactable,
                    forced ? PermissionLocale.ForcedOffByMode.Resolve() : string.Empty,
                    DashTheme.Warning);
                return;
            }

            var cell = column.Single;
            if (kind == PermMatrixKind.Header)
            {
                cell.SetPaint(color.Transparent, color.Transparent, color.Transparent, DashTheme.TextMuted);
                ListingStyle.SetText(cell.Title, role.Name);
                cell.SetNote(string.Empty, DashTheme.TextMuted);
                cell.SetInteractable(false);
                return;
            }

            bool minimum = kind == PermMatrixKind.MinScale;
            bool open = Panel.IsScaleOpen(role.Name, minimum);
            ListingStyle.SetText(cell.Title, Panel.ScaleText(role.Name, minimum));
            cell.SetNote(string.Empty, DashTheme.TextMuted);
            cell.SetPaint(open ? DashTheme.AccentSoft : DashTheme.Surface,
                open ? DashTheme.AccentSoft : DashTheme.SurfaceHover,
                open ? DashTheme.Accent : DashTheme.Outline,
                interactable ? DashTheme.Text : DashTheme.TextDim);
            cell.SetInteractable(interactable);
        }
    }
}
