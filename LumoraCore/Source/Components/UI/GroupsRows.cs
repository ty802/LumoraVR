// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Every string the Groups screen can put on screen, in one place, so a locale sweep can walk it by
// reflection and prove nothing ships a key with no English behind it. Same shape as PermissionLocale.
//
// The wording stays out of the service's vocabulary. Nobody knows what "visibility: Private" means the
// first time they read it, so the pill says what happens instead: Open, Request, Invite only. -xlinka
public static class GroupsLocale
{
    public static readonly LocaleText Title = "Groups.Title".AsLocale("Groups");
    public static readonly LocaleText SignedOut =
        "Groups.SignedOut".AsLocale("Sign in from the Home screen to see groups.");
    public static readonly LocaleText Loading = "Groups.Loading".AsLocale("Loading");
    public static readonly LocaleText LoadFailed = "Groups.LoadFailed".AsLocale("Could not load that.");
    public static readonly LocaleText ActionFailed =
        "Groups.ActionFailed".AsLocale("That did not go through.");

    public static readonly LocaleText TabBrowse = "Groups.Tab.Browse".AsLocale("Browse");
    public static readonly LocaleText TabMine = "Groups.Tab.Mine".AsLocale("Mine");
    public static readonly LocaleText Back = "Groups.Back".AsLocale("Back");
    public static readonly LocaleText Refresh = "Groups.Refresh".AsLocale("Refresh");
    public static readonly LocaleText New = "Groups.New".AsLocale("New");
    public static readonly LocaleText Search = "Groups.Search".AsLocale("Search");
    public static readonly LocaleText SearchHint = "Groups.Search.Hint".AsLocale("Search groups");

    public static readonly LocaleText BrowseEmpty = "Groups.Browse.Empty".AsLocale("No groups to show.");
    public static readonly LocaleText MineEmpty =
        "Groups.Mine.Empty".AsLocale("You are not in a group yet. Browse to find one.");
    public static readonly LocaleText Invites = "Groups.Invites".AsLocale("Invites");
    public static readonly LocaleText Accept = "Groups.Accept".AsLocale("Accept");
    public static readonly LocaleText Decline = "Groups.Decline".AsLocale("Decline");

    public static readonly LocaleText VisibilityOpen = "Groups.Visibility.Open".AsLocale("Open");
    public static readonly LocaleText VisibilityRequest = "Groups.Visibility.Request".AsLocale("Request");
    public static readonly LocaleText VisibilityInvite = "Groups.Visibility.Invite".AsLocale("Invite only");

    public static readonly LocaleText Join = "Groups.Join".AsLocale("Join");
    public static readonly LocaleText RequestToJoin = "Groups.Request".AsLocale("Request");
    public static readonly LocaleText Member = "Groups.Member".AsLocale("Member");
    public static readonly LocaleText Leave = "Groups.Leave".AsLocale("Leave");
    public static readonly LocaleText Manage = "Groups.Manage".AsLocale("Manage");
    public static readonly LocaleText Represent = "Groups.Represent".AsLocale("Represent this group");
    public static readonly LocaleText Represented = "Groups.Represented".AsLocale("Represented");
    public static readonly LocaleText You = "Groups.You".AsLocale("you");

    public static readonly LocaleText CreateName = "Groups.Create.Name".AsLocale("Group name");
    public static readonly LocaleText CreateNameHint = "Groups.Create.NameHint".AsLocale("Name your group");
    public static readonly LocaleText Create = "Groups.Create".AsLocale("Create");
    public static readonly LocaleText Cancel = "Groups.Cancel".AsLocale("Cancel");
    public static readonly LocaleText NameEmpty = "Groups.Create.Empty".AsLocale("A group needs a name.");

    public static readonly LocaleText SectionEvents = "Groups.Section.Events".AsLocale("Upcoming events");
    public static readonly LocaleText SectionAnnouncements =
        "Groups.Section.Announcements".AsLocale("Announcements");
    public static readonly LocaleText SectionMembers = "Groups.Section.Members".AsLocale("Members");
    public static readonly LocaleText SectionEdit = "Groups.Section.Edit".AsLocale("Edit");
    public static readonly LocaleText SectionRequests = "Groups.Section.Requests".AsLocale("Requests");
    public static readonly LocaleText SectionInvites = "Groups.Section.Invites".AsLocale("Invites");
    public static readonly LocaleText SectionBans = "Groups.Section.Bans".AsLocale("Bans");
    public static readonly LocaleText SectionDanger = "Groups.Section.Danger".AsLocale("Danger");

    public static readonly LocaleText EventsEmpty = "Groups.Events.Empty".AsLocale("Nothing planned.");
    public static readonly LocaleText AnnouncementsEmpty =
        "Groups.Announcements.Empty".AsLocale("Nothing posted yet.");
    public static readonly LocaleText MembersEmpty = "Groups.Members.Empty".AsLocale("No members listed.");
    public static readonly LocaleText RequestsEmpty = "Groups.Requests.Empty".AsLocale("Nobody waiting.");
    public static readonly LocaleText InvitesEmpty = "Groups.Invites.Empty".AsLocale("No invites out.");
    public static readonly LocaleText BansEmpty = "Groups.Bans.Empty".AsLocale("Nobody banned.");

    public static readonly LocaleText Post = "Groups.Post".AsLocale("Post");
    public static readonly LocaleText Delete = "Groups.Delete".AsLocale("Delete");
    public static readonly LocaleText AnnouncementHint =
        "Groups.Announcement.Hint".AsLocale("Say something to the group");
    public static readonly LocaleText EventTitle = "Groups.Event.Title".AsLocale("Event");
    public static readonly LocaleText EventTitleHint = "Groups.Event.TitleHint".AsLocale("What is happening");
    public static readonly LocaleText EventWorld = "Groups.Event.World".AsLocale("World");
    public static readonly LocaleText EventWorldHint = "Groups.Event.WorldHint".AsLocale("Where");
    public static readonly LocaleText EventDate = "Groups.Event.Date".AsLocale("Date");
    public static readonly LocaleText EventDateHint = "Groups.Event.DateHint".AsLocale("2026-01-31");
    public static readonly LocaleText EventTime = "Groups.Event.Time".AsLocale("Start");
    public static readonly LocaleText EventTimeHint = "Groups.Event.TimeHint".AsLocale("19:30");
    public static readonly LocaleText EventBadDate =
        "Groups.Event.BadDate".AsLocale("That date and time did not read.");
    public static readonly LocaleText EventNoTitle = "Groups.Event.NoTitle".AsLocale("An event needs a title.");
    public static readonly LocaleText AnnouncementEmpty =
        "Groups.Announcement.Empty".AsLocale("Nothing typed yet.");

    public static readonly LocaleText FieldName = "Groups.Field.Name".AsLocale("Name");
    public static readonly LocaleText FieldTag = "Groups.Field.Tag".AsLocale("Tag");
    public static readonly LocaleText FieldTagHint = "Groups.Field.TagHint".AsLocale("2 to 6 characters");
    public static readonly LocaleText FieldBio = "Groups.Field.Bio".AsLocale("Bio");
    public static readonly LocaleText FieldBioHint = "Groups.Field.BioHint".AsLocale("What this group is for");
    public static readonly LocaleText FieldColor = "Groups.Field.Color".AsLocale("Colour");
    public static readonly LocaleText FieldColorHint = "Groups.Field.ColorHint".AsLocale("#6F5FF0");
    public static readonly LocaleText FieldVisibility = "Groups.Field.Visibility".AsLocale("Who can join");
    public static readonly LocaleText Save = "Groups.Save".AsLocale("Save");
    public static readonly LocaleText NothingToSave = "Groups.NothingToSave".AsLocale("Nothing changed.");

    public static readonly LocaleText InviteById = "Groups.Invite.ById".AsLocale("Invite by user id");
    public static readonly LocaleText InviteHint = "Groups.Invite.Hint".AsLocale("U-...");
    public static readonly LocaleText Invite = "Groups.Invite".AsLocale("Invite");
    public static readonly LocaleText Withdraw = "Groups.Withdraw".AsLocale("Withdraw");
    public static readonly LocaleText Approve = "Groups.Approve".AsLocale("Approve");
    public static readonly LocaleText Deny = "Groups.Deny".AsLocale("Deny");
    public static readonly LocaleText Kick = "Groups.Kick".AsLocale("Kick");
    public static readonly LocaleText Ban = "Groups.Ban".AsLocale("Ban");
    public static readonly LocaleText Unban = "Groups.Unban".AsLocale("Unban");
    public static readonly LocaleText MakeOwner = "Groups.MakeOwner".AsLocale("Make owner");
    public static readonly LocaleText DeleteGroup = "Groups.DeleteGroup".AsLocale("Delete group");
    public static readonly LocaleText Confirm = "Groups.Confirm".AsLocale("Confirm");

    public static readonly LocaleText Storage = "Groups.Storage".AsLocale("Storage");
    public static readonly LocaleText StorageLocked = "Groups.Storage.Locked".AsLocale(
        "Group storage is locked: the owner's support lapsed, so no new uploads. Existing content stays.");
    public static readonly LocaleText NoStorage = "Groups.Storage.None".AsLocale("No storage pool.");

    public static LocaleText StorageGrace(string when) => "Groups.Storage.Grace".AsLocale(
        "The owner's support has lapsed. Group storage stays usable until {0}, then it locks.", when);

    public static LocaleText Members(int count) => count == 1
        ? "Groups.Members.One".AsLocale("1 member")
        : "Groups.Members.Count".AsLocale("{0} members", count);

    public static LocaleText MembersWith(int count) => "Groups.Section.MembersCount".AsLocale("Members ({0})", count);

    public static LocaleText RequestsWith(int count) => "Groups.Section.RequestsCount".AsLocale("Requests ({0})", count);

    public static LocaleText StoragePool(string used, string quota, string allocated)
        => "Groups.Storage.Pool".AsLocale("{0} of {1} used, {2} allocated", used, quota, allocated);

    public static LocaleText Slice(string value) => "Groups.Storage.Slice".AsLocale("Slice {0}", value);

    public static LocaleText Used(string value) => "Groups.Storage.Used".AsLocale("{0} used", value);

    public static readonly LocaleText HostEventWorld = "Groups.Event.HostWorld".AsLocale("Host");
    public static readonly LocaleText EventLive = "Groups.Event.Live".AsLocale("Live");

    public static LocaleText HostedBy(string name) => "Groups.Event.Host".AsLocale("Hosted by {0}", name);

    public static LocaleText PostedBy(string name, string when)
        => "Groups.Announcement.By".AsLocale("{0}, {1}", name, when);

    public static LocaleText BannedFor(string reason, string when)
        => "Groups.Ban.Reason".AsLocale("{0}, {1}", reason, when);

    public static LocaleText BannedOn(string when) => "Groups.Ban.When".AsLocale("Banned {0}", when);
}

// What a pressed control on this page means. The row items carry one of these per button, so the relay
// stays a lookup and every decision lives on the screen. -xlinka
internal enum GroupAction
{
    None,
    Open,
    Join,
    AcceptInvite,
    DeclineInvite,
    Leave,
    Represent,
    Unrepresent,
    Manage,
    Approve,
    Deny,
    Kick,
    Ban,
    Unban,
    MakeOwner,
    Withdraw,
    Invite,
    Save,
    CycleVisibility,
    PostAnnouncement,
    DeleteAnnouncement,
    CreateEvent,
    DeleteEvent,
    HostEventWorld,
    JoinEventWorld,
    DeleteGroup,
    Create,
    CancelCreate,
}

// Which typing buffer a field row edits.
internal enum GroupField
{
    None,
    Search,
    CreateName,
    EditName,
    EditTag,
    EditBio,
    EditColor,
    InviteId,
    Announcement,
    EventTitle,
    EventWorld,
    EventDate,
    EventTime,
}

internal static class GroupMetrics
{
    public const float NoteHeight = 26f;
    public const float CardHeight = 76f;
    public const float HeaderCardHeight = 108f;
    public const float MemberHeight = 44f;
    public const float MemberStorageHeight = 72f;
    public const float PersonHeight = 46f;
    public const float PostHeight = 52f;
    public const float FieldHeight = 44f;
    public const float ActionsHeight = 40f;
    public const float StorageHeight = 58f;

    public const float IconSize = 48f;
    public const float HeaderIconSize = 64f;
    public const float IconGap = 12f;

    public const float PillHeight = 26f;
    public const float PillGap = 8f;
    public const float ActionWidth = 104f;
    public const float ChipWidth = 92f;
    public const float MetaWidth = 96f;

    // Card right band, measured back from the row's right edge.
    public const float PrimaryRight = 14f;
    public const float SecondaryRight = PrimaryRight + ActionWidth + PillGap;
    public const float VisibilityRight = SecondaryRight + ActionWidth + PillGap;
    public const float RoleRight = VisibilityRight + ChipWidth + PillGap;
    public const float MetaRight = RoleRight + ChipWidth + PillGap;
    public const float CardTextRight = MetaRight + MetaWidth + IconGap;

    // An event row carries Host and Live beside Delete. Both are one short word, so they are narrower
    // than the delete pill. The body text clears all three whether or not they are up, so an announcement
    // and an event stay the same shape and a row that gains a pill does not reflow its own text. -xlinka
    public const float PostActionWidth = 72f;
    public const float PostHostRight = PrimaryRight + ActionWidth + PillGap;
    public const float PostLiveRight = PostHostRight + PostActionWidth + PillGap;
    public const float PostTextRight = PostLiveRight + PostActionWidth + PillGap;

    // Member row control band.
    public const float MemberActionWidth = 84f;
    public const float MemberRoleStrip = 260f;
    public const float SliderWidth = 220f;
    public const float SliderValueWidth = 88f;

    public const float FieldLabelWidth = 150f;
    public const float FieldPillWidth = 108f;
}

// The group pictures this page is showing, as textures. One provider per content hash, built on demand on
// a hidden slot and kept for as long as the screen is open: the same icon shows up on a browse row, a
// group page header and a member's group card, and re-fetching it per row would be three downloads for one
// picture. -xlinka
internal sealed class GroupIconCache
{
    private readonly Dictionary<string, ImageProvider> _byHash = new(StringComparer.Ordinal);
    private readonly Slot _host;

    public GroupIconCache(Slot host) => _host = host;

    public ImageProvider? Get(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || _host == null || _host.IsDestroyed)
            return null;
        if (_byHash.TryGetValue(hash!, out var existing) && existing != null && !existing.IsDestroyed)
            return existing;

        Uri url;
        try
        {
            url = new Uri(Nexus.Cloud.Cdn.ServiceConfig.Current.GetContentUrl(hash!));
        }
        catch (UriFormatException)
        {
            return null;
        }

        var provider = _host.AddSlot("Icon").AttachComponent<ImageProvider>();
        provider.URL.Value = url;
        // A 64-unit square never samples at an angle, so the mip chain is dead weight.
        provider.GenerateMipmaps.Value = false;
        _byHash[hash!] = provider;
        return provider;
    }

    public void Clear()
    {
        foreach (var provider in _byHash.Values)
        {
            if (provider != null && !provider.IsDestroyed)
                provider.Slot?.Destroy();
        }
        _byHash.Clear();
    }
}

// ITEMS

internal sealed class GroupNoteItem : ListingItem
{
    public color Tint = DashTheme.TextMuted;

    public GroupNoteItem(string key, LocaleText text) : base(key)
    {
        LabelText = text;
        Kind = GroupsScreen.KindNote;
    }
}

// One group, as a browse row, a "mine" row or a waiting invite. All three are the same shape and only
// differ in which pills they carry, so they are one item and one template rather than three of each.
internal sealed class GroupCardItem : ListingItem
{
    public string GroupId = string.Empty;
    public string Name = string.Empty;
    public string GroupTag = string.Empty;
    public string Bio = string.Empty;
    public string? IconHash;
    public color Tint = DashTheme.Accent;
    public string Meta = string.Empty;
    public string Visibility = string.Empty;
    public string Chip = string.Empty;
    public bool ChipLit;

    public string PrimaryLabel = string.Empty;
    public GroupAction Primary;
    public bool PrimaryEnabled = true;
    public string SecondaryLabel = string.Empty;
    public GroupAction Secondary;
    public bool Openable = true;

    public GroupCardItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindCard;
    }
}

internal sealed class GroupHeaderItem : ListingItem
{
    public string Name = string.Empty;
    public string GroupTag = string.Empty;
    public string Bio = string.Empty;
    public string? IconHash;
    public color Tint = DashTheme.Accent;
    public string Visibility = string.Empty;
    public string Meta = string.Empty;

    public GroupHeaderItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindHeaderCard;
    }
}

internal sealed class GroupMemberItem : ListingItem
{
    public string UserId = string.Empty;
    public string Name = string.Empty;
    public string Role = string.Empty;
    public bool IsSelf;

    public IReadOnlyList<string> Roles = Array.Empty<string>();
    public bool ShowStorage;
    public long Allocated;
    public long UsedBytes;
    public long SliderMax;
    public Func<string, long>? PendingSlice;

    public bool ShowKick;
    public bool ShowBan;
    public bool BanArmed;
    public bool ShowMakeOwner;

    public GroupMemberItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindMember;
    }
}

// A person with a note and up to two answers: a join request, an invite, a ban.
internal sealed class GroupPersonItem : ListingItem
{
    public string UserId = string.Empty;
    public string Name = string.Empty;
    public string Note = string.Empty;

    public string PrimaryLabel = string.Empty;
    public GroupAction Primary;
    public bool PrimaryDanger;
    public string SecondaryLabel = string.Empty;
    public GroupAction Secondary;
    public bool SecondaryDanger;

    public GroupPersonItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindPerson;
    }
}

internal sealed class GroupPostItem : ListingItem
{
    public string PostId = string.Empty;
    public string Body = string.Empty;
    public string Meta = string.Empty;
    public bool CanDelete;
    public GroupAction Delete;

    // Event rows only. Host opens the Worlds create form prefilled and hosts nothing on its own; Live is
    // lit when the session browser is actually listing a session for this group under this event's name.
    public bool CanHost;
    public GroupAction HostAction;
    public bool IsLive;
    public GroupAction LiveAction;

    public GroupPostItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindPost;
    }
}

internal sealed class GroupFieldItem : ListingItem
{
    public GroupField Field;
    public LocaleText Placeholder;
    public Func<string>? Read;
    public Func<bool>? Focused;
    public string PillLabel = string.Empty;
    public GroupAction Pill;

    public GroupFieldItem(string key, LocaleText label) : base(key)
    {
        LabelText = label;
        Kind = GroupsScreen.KindField;
    }
}

internal sealed class GroupActionsItem : ListingItem
{
    internal readonly struct Entry
    {
        public readonly string Label;
        public readonly GroupAction Action;
        public readonly bool Danger;
        public readonly bool Lit;
        public readonly float Width;

        public Entry(string label, GroupAction action, bool danger, bool lit, float width)
        {
            Label = label;
            Action = action;
            Danger = danger;
            Lit = lit;
            Width = width;
        }
    }

    public readonly List<Entry> Buttons = new();

    public GroupActionsItem(string key) : base(key)
    {
        Kind = GroupsScreen.KindActions;
    }

    public GroupActionsItem Add(string label, GroupAction action, bool danger = false, bool lit = false,
        float width = GroupMetrics.ActionWidth)
    {
        Buttons.Add(new Entry(label, action, danger, lit, width));
        return this;
    }
}

internal sealed class GroupStorageItem : ListingItem
{
    public long Used;
    public long Allocated;
    public long Quota;

    public GroupStorageItem(string key, LocaleText label) : base(key)
    {
        LabelText = label;
        Kind = GroupsScreen.KindStorage;
    }
}

// Owns every button on this page. A Helio button carries ONE press action and it is a SyncDelegate, which
// a closure cannot be named as, so the action is a method here and the component works out which button
// fired. Listing rows are POOLED and rebound as the list scrolls, so what is registered is the ROW and a
// column index; the item comes off row.Item at fire time. -xlinka
[ComponentCategory("Hidden")]
public sealed class GroupsRelay : Component
{
    internal readonly struct Binding
    {
        public readonly ListingRow Row;
        public readonly int Index;

        public Binding(ListingRow row, int index)
        {
            Row = row;
            Index = index;
        }
    }

    internal GroupsScreen? Screen;

    private readonly Dictionary<Button, Binding> _bindings = new();

    internal void Register(Button button, ListingRow row, int index)
    {
        if (button != null && row != null)
            _bindings[button] = new Binding(row, index);
    }

    private bool Resolve(Button button, out ListingItem item, out int index)
    {
        item = null!;
        index = -1;
        if (button == null || Screen == null || Screen.IsDestroyed)
            return false;
        if (!_bindings.TryGetValue(button, out var binding))
            return false;
        index = binding.Index;
        item = binding.Row.Item!;
        return item != null;
    }

    [SyncMethod]
    public void OnCardPressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupCardItem card)
            return;
        if (index < 0)
        {
            if (card.Openable)
                Screen!.Dispatch(GroupAction.Open, card.GroupId, string.Empty);
            return;
        }
        var action = index == 0 ? card.Primary : card.Secondary;
        Screen!.Dispatch(action, card.GroupId, string.Empty);
    }

    [SyncMethod]
    public void OnMemberRolePressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupMemberItem member)
            return;
        if (index < 0 || index >= member.Roles.Count)
            return;
        Screen!.SetMemberRole(member.UserId, member.Roles[index]);
    }

    [SyncMethod]
    public void OnMemberActionPressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupMemberItem member)
            return;
        var action = index switch
        {
            0 => GroupAction.Kick,
            1 => GroupAction.Ban,
            _ => GroupAction.MakeOwner,
        };
        Screen!.Dispatch(action, string.Empty, member.UserId);
    }

    [SyncMethod]
    public void OnPersonPressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupPersonItem person)
            return;
        Screen!.Dispatch(index == 0 ? person.Primary : person.Secondary, string.Empty, person.UserId);
    }

    [SyncMethod]
    public void OnPostPressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupPostItem post)
            return;
        var action = index switch
        {
            1 => post.HostAction,
            2 => post.LiveAction,
            _ => post.Delete,
        };
        Screen!.Dispatch(action, string.Empty, post.PostId);
    }

    [SyncMethod]
    public void OnFieldPressed(Button button, UIInteractionContext context)
    {
        if (Resolve(button, out var item, out _) && item is GroupFieldItem field)
            Screen!.FocusField(field.Field);
    }

    [SyncMethod]
    public void OnFieldPillPressed(Button button, UIInteractionContext context)
    {
        if (Resolve(button, out var item, out _) && item is GroupFieldItem field)
            Screen!.Dispatch(field.Pill, string.Empty, string.Empty);
    }

    [SyncMethod]
    public void OnActionPressed(Button button, UIInteractionContext context)
    {
        if (!Resolve(button, out var item, out int index) || item is not GroupActionsItem actions)
            return;
        if (index < 0 || index >= actions.Buttons.Count)
            return;
        Screen!.Dispatch(actions.Buttons[index].Action, string.Empty, string.Empty);
    }

}

// PARTS

// The picture on a card: a rounded, stencil-clipped box holding the group's icon, or the group's colour
// with its tag on it when there is no icon. The placeholder is deliberately not a picture of anything.
internal sealed class GroupIcon
{
    public readonly Slot Root;

    private readonly RoundedPanel _panel;
    private readonly Text _letters;
    private readonly Slot _imageSlot;
    private readonly RawImage _image;

    private GroupIcon(Slot root, RoundedPanel panel, Text letters, Slot imageSlot, RawImage image)
    {
        Root = root;
        _panel = panel;
        _letters = letters;
        _imageSlot = imageSlot;
        _image = image;
    }

    public static GroupIcon Build(Slot host, SettingsFonts fonts, float size, float fontSize)
    {
        var panel = SettingsUI.Panel(host, DashTheme.Field, color.Transparent, DashTheme.RadiusControl);
        var mask = host.AttachComponent<Mask>();
        mask.StencilMasking.Value = true;
        mask.ShowMaskGraphic.Value = true;

        var letters = SettingsUI.FillLabel(host, "Letters", fonts.Strong, fontSize, DashTheme.Text,
            TextHorizontalAlignment.Center, shrinkToFit: true);

        var imageSlot = SettingsUI.Child(host, "Image", float2.Zero, float2.One, float2.Zero, float2.Zero);
        var image = imageSlot.AttachComponent<RawImage>();
        imageSlot.ActiveSelf.Value = false;
        return new GroupIcon(host, panel, letters, imageSlot, image);
    }

    public void Bind(GroupIconCache? cache, string? hash, string tag, in color tint)
    {
        var provider = cache?.Get(hash);
        bool hasArt = provider != null;
        if (hasArt && !ReferenceEquals(_image.Texture.Target, provider))
            _image.Texture.Target = provider!;
        ListingStyle.SetActive(_imageSlot, hasArt);
        ListingStyle.SetActive(_letters.Slot, !hasArt);
        if (hasArt)
            return;

        // Knocked well down: at full strength a group colour behind two letters reads as a warning banner.
        SettingsUI.SetPaint(_panel,
            new color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, 1f), color.Transparent);
        ListingStyle.SetText(_letters, string.IsNullOrEmpty(tag) ? "?" : tag);
        ListingStyle.SetTextColor(_letters, DashTheme.Text);
    }
}

// A fixed-width pill measured back from the row's right edge, on the shared control band.
internal static class GroupBand
{
    public static Slot Right(Slot row, string name, float right, float width, float height = GroupMetrics.PillHeight)
        => SettingsUI.Child(row, name, new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-(right + width), -height * 0.5f), new float2(-right, height * 0.5f));

    public static Slot Left(Slot row, string name, float left, float width, float height = GroupMetrics.PillHeight)
        => SettingsUI.Child(row, name, new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(left, -height * 0.5f), new float2(left + width, height * 0.5f));

    public static void PaintAction(SettingsChip chip, string label, bool enabled, bool danger, bool lit)
    {
        ListingStyle.SetText(chip.Text, label);
        if (danger)
            chip.SetPaint(GroupPaint.DangerFill, GroupPaint.DangerHover, DashTheme.Outline, DashTheme.Negative);
        else if (lit)
            chip.SetPaint(DashTheme.Accent, DashTheme.AccentHover, DashTheme.Accent, DashTheme.OnAccent);
        else
            chip.SetPaint(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.Outline, DashTheme.TextDim);
        chip.SetInteractable(enabled);
    }

    // A chip that says what something IS. No press, no accent: the word carries it.
    public static void PaintChip(SettingsChip chip, string label, bool lit)
    {
        ListingStyle.SetText(chip.Text, label);
        chip.SetPaint(lit ? DashTheme.AccentSoft : DashTheme.Field, lit ? DashTheme.AccentSoft : DashTheme.Field,
            color.Transparent, lit ? DashTheme.Text : DashTheme.TextDim);
        chip.SetInteractable(false);
    }
}

internal static class GroupPaint
{
    public static readonly color DangerFill = DashTheme.Negative.WithAlpha(0.14f);
    public static readonly color DangerHover = DashTheme.Negative.WithAlpha(0.26f);
    public static readonly color ArmedFill = DashTheme.Negative.WithAlpha(0.35f);
}

// TEMPLATES

internal sealed class GroupNoteTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;

    public GroupNoteTemplate(SettingsFonts fonts) => _fonts = fonts;

    public override float Height => GroupMetrics.NoteHeight;
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

        public override void Bind(ListingItem item)
        {
            ListingStyle.SetText(Text, item.Label);
            ListingStyle.SetTextColor(Text, (item as GroupNoteItem)?.Tint ?? DashTheme.TextMuted);
        }
    }
}

// ONE GROUP
//
// Icon, name and tag, one line of bio, then the right band: how many people, the caller's standing, what
// the group's door is, and the one thing to press. Pressing anywhere else opens it.
internal sealed class GroupCardTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;
    private readonly GroupIconCache _icons;

    public GroupCardTemplate(SettingsFonts fonts, GroupsRelay relay, GroupIconCache icons)
    {
        _fonts = fonts;
        _relay = relay;
        _icons = icons;
    }

    public override float Height => GroupMetrics.CardHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusCard);
        var open = row.AttachComponent<Button>();

        var iconHost = GroupBand.Left(row, "Icon", SettingsMetrics.RowInset, GroupMetrics.IconSize,
            GroupMetrics.IconSize);
        var icon = GroupIcon.Build(iconHost, _fonts, GroupMetrics.IconSize, DashTheme.FontHeading);

        float textLeft = SettingsMetrics.RowInset + GroupMetrics.IconSize + GroupMetrics.IconGap;
        var name = SettingsUI.Label(row, "Name", _fonts.Medium, DashTheme.FontHeading, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, float2.One,
            new float2(textLeft, 22f), new float2(-GroupMetrics.CardTextRight, -12f), shrinkToFit: true);
        var bio = SettingsUI.Label(row, "Bio", _fonts.Body, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 0f),
            new float2(textLeft, 14f), new float2(-GroupMetrics.CardTextRight, 34f), shrinkToFit: true);

        var meta = SettingsUI.Label(row, "Meta", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(-(GroupMetrics.MetaRight + GroupMetrics.MetaWidth), -12f),
            new float2(-GroupMetrics.MetaRight, 12f), shrinkToFit: true);

        var listingRow = new CardRow
        {
            Background = background,
            Icon = icon,
            Name = name,
            Bio = bio,
            Meta = meta,
            Icons = _icons,
        };

        _relay.Register(open, listingRow, -1);
        open.SetAction(_relay.OnCardPressed);
        listingRow.Open = open;

        listingRow.Chip = SettingsChip.Build(
            GroupBand.Right(row, "Chip", GroupMetrics.RoleRight, GroupMetrics.ChipWidth),
            _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);
        listingRow.Visibility = SettingsChip.Build(
            GroupBand.Right(row, "Door", GroupMetrics.VisibilityRight, GroupMetrics.ChipWidth),
            _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);

        listingRow.Secondary = BuildAction(row, listingRow, "Second", GroupMetrics.SecondaryRight, 1);
        listingRow.Primary = BuildAction(row, listingRow, "Primary", GroupMetrics.PrimaryRight, 0);
        return listingRow;
    }

    private SettingsChip BuildAction(Slot row, ListingRow listingRow, string name, float right, int index)
    {
        var chip = SettingsChip.Build(GroupBand.Right(row, name, right, GroupMetrics.ActionWidth),
            _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, index);
        chip.Button.SetAction(_relay.OnCardPressed);
        chip.Slot.ActiveSelf.Value = false;
        return chip;
    }

    private sealed class CardRow : ListingRow
    {
        public required RoundedPanel Background;
        public required GroupIcon Icon;
        public required Text Name;
        public required Text Bio;
        public required Text Meta;
        public required GroupIconCache Icons;

        public Button Open = null!;
        public SettingsChip Chip = null!;
        public SettingsChip Visibility = null!;
        public SettingsChip Primary = null!;
        public SettingsChip Secondary = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupCardItem card)
                return;

            Icon.Bind(Icons, card.IconHash, card.GroupTag, card.Tint);
            ListingStyle.SetText(Name, card.GroupTag.Length > 0 ? card.Name + "  [" + card.GroupTag + "]" : card.Name);
            ListingStyle.SetText(Bio, card.Bio);
            ListingStyle.SetActive(Bio.Slot, card.Bio.Length > 0);
            ListingStyle.SetText(Meta, card.Meta);
            ListingStyle.SetActive(Meta.Slot, card.Meta.Length > 0);
            ListingStyle.SetInteractable(Open, card.Openable);

            ListingStyle.SetActive(Chip.Slot, card.Chip.Length > 0);
            if (card.Chip.Length > 0)
                GroupBand.PaintChip(Chip, card.Chip, card.ChipLit);

            ListingStyle.SetActive(Visibility.Slot, card.Visibility.Length > 0);
            if (card.Visibility.Length > 0)
                GroupBand.PaintChip(Visibility, card.Visibility, false);

            ListingStyle.SetActive(Primary.Slot, card.PrimaryLabel.Length > 0);
            if (card.PrimaryLabel.Length > 0)
                GroupBand.PaintAction(Primary, card.PrimaryLabel, card.PrimaryEnabled, false, card.PrimaryEnabled);

            ListingStyle.SetActive(Secondary.Slot, card.SecondaryLabel.Length > 0);
            if (card.SecondaryLabel.Length > 0)
                GroupBand.PaintAction(Secondary, card.SecondaryLabel, true, false, false);
        }
    }
}

// THE GROUP PAGE HEADER
internal sealed class GroupHeaderTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly GroupIconCache _icons;

    public GroupHeaderTemplate(SettingsFonts fonts, GroupIconCache icons)
    {
        _fonts = fonts;
        _icons = icons;
    }

    public override float Height => GroupMetrics.HeaderCardHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusCard);

        var iconHost = GroupBand.Left(row, "Icon", SettingsMetrics.RowInset, GroupMetrics.HeaderIconSize,
            GroupMetrics.HeaderIconSize);
        var icon = GroupIcon.Build(iconHost, _fonts, GroupMetrics.HeaderIconSize, DashTheme.FontTitle);

        float textLeft = SettingsMetrics.RowInset + GroupMetrics.HeaderIconSize + GroupMetrics.IconGap;
        var name = SettingsUI.Label(row, "Name", _fonts.Strong, DashTheme.FontTitle, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(textLeft, -40f), new float2(-GroupMetrics.CardTextRight, -12f), shrinkToFit: true);
        var bio = SettingsUI.Label(row, "Bio", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 0f),
            new float2(textLeft, 14f), new float2(-SettingsMetrics.RowInset, 44f), shrinkToFit: true);

        var meta = SettingsUI.Label(row, "Meta", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 1f), new float2(1f, 1f),
            new float2(-(GroupMetrics.MetaRight + GroupMetrics.MetaWidth), -40f),
            new float2(-GroupMetrics.MetaRight, -14f), shrinkToFit: true);

        var listingRow = new HeaderRow
        {
            Background = background,
            Icon = icon,
            Name = name,
            Bio = bio,
            Meta = meta,
            Icons = _icons,
        };

        listingRow.Tag = SettingsChip.Build(
            GroupBand.Right(row, "Tag", GroupMetrics.RoleRight, GroupMetrics.ChipWidth),
            _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);
        SettingsUI.SetAnchors(SettingsUI.Rect(listingRow.Tag.Slot), new float2(1f, 1f), new float2(1f, 1f));
        SettingsUI.SetOffsets(SettingsUI.Rect(listingRow.Tag.Slot),
            new float2(-(GroupMetrics.RoleRight + GroupMetrics.ChipWidth), -39f),
            new float2(-GroupMetrics.RoleRight, -15f));

        listingRow.Visibility = SettingsChip.Build(
            GroupBand.Right(row, "Door", GroupMetrics.VisibilityRight, GroupMetrics.ChipWidth),
            _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);
        SettingsUI.SetAnchors(SettingsUI.Rect(listingRow.Visibility.Slot), new float2(1f, 1f), new float2(1f, 1f));
        SettingsUI.SetOffsets(SettingsUI.Rect(listingRow.Visibility.Slot),
            new float2(-(GroupMetrics.VisibilityRight + GroupMetrics.ChipWidth), -39f),
            new float2(-GroupMetrics.VisibilityRight, -15f));
        return listingRow;
    }

    private sealed class HeaderRow : ListingRow
    {
        public required RoundedPanel Background;
        public required GroupIcon Icon;
        public required Text Name;
        public required Text Bio;
        public required Text Meta;
        public required GroupIconCache Icons;

        public SettingsChip Tag = null!;
        public SettingsChip Visibility = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupHeaderItem header)
                return;

            Icon.Bind(Icons, header.IconHash, header.GroupTag, header.Tint);
            ListingStyle.SetText(Name, header.Name);
            ListingStyle.SetText(Bio, header.Bio);
            ListingStyle.SetActive(Bio.Slot, header.Bio.Length > 0);
            ListingStyle.SetText(Meta, header.Meta);

            ListingStyle.SetActive(Tag.Slot, header.GroupTag.Length > 0);
            if (header.GroupTag.Length > 0)
                GroupBand.PaintChip(Tag, header.GroupTag, true);
            GroupBand.PaintChip(Visibility, header.Visibility, false);
        }
    }
}

// ONE MEMBER
//
// Read-only on the group page (name, role, "you") and the whole management set on the admin page: the role
// ladder as a pill strip, the storage slice as a slider that commits on release, and the two escalations.
internal sealed class GroupMemberTemplate : ListingRowTemplate
{
    private const int MaxRoles = 4;

    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;
    private readonly GroupsScreen _screen;

    public GroupMemberTemplate(SettingsFonts fonts, GroupsRelay relay, GroupsScreen screen)
    {
        _fonts = fonts;
        _relay = relay;
        _screen = screen;
    }

    public override float Height => GroupMetrics.MemberHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override float HeightFor(ListingItem item)
        => item is GroupMemberItem { ShowStorage: true } ? GroupMetrics.MemberStorageHeight : GroupMetrics.MemberHeight;

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var name = SettingsUI.Label(row, "Name", _fonts.Medium, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(0f, 1f),
            new float2(SettingsMetrics.RowInset, -34f), new float2(SettingsMetrics.RowInset + 260f, -8f),
            shrinkToFit: true);
        var used = SettingsUI.Label(row, "Used", _fonts.Body, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0f, 0f),
            new float2(SettingsMetrics.RowInset, 8f), new float2(SettingsMetrics.RowInset + 260f, 24f));
        used.Slot.ActiveSelf.Value = false;

        var roleText = SettingsUI.Label(row, "Role", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 1f), new float2(1f, 1f),
            new float2(-(GroupMetrics.PrimaryRight + GroupMetrics.ChipWidth), -34f),
            new float2(-GroupMetrics.PrimaryRight, -8f), shrinkToFit: true);

        var listingRow = new MemberRow
        {
            Background = background,
            Name = name,
            Used = used,
            RoleText = roleText,
            Screen = _screen,
        };

        var strip = SettingsUI.Child(row, "Roles", new float2(1f, 1f), new float2(1f, 1f),
            new float2(-(GroupMetrics.PrimaryRight + 3f * (GroupMetrics.MemberActionWidth + GroupMetrics.PillGap)
                + GroupMetrics.MemberRoleStrip), -35f),
            new float2(-(GroupMetrics.PrimaryRight + 3f * (GroupMetrics.MemberActionWidth + GroupMetrics.PillGap)), -9f));
        listingRow.Strip = strip;
        for (int i = 0; i < MaxRoles; i++)
        {
            var host = SettingsUI.Child(strip, "Role", float2.Zero, float2.One, float2.Zero, float2.Zero);
            var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusChip);
            host.ActiveSelf.Value = false;
            _relay.Register(chip.Button, listingRow, i);
            chip.Button.SetAction(_relay.OnMemberRolePressed);
            listingRow.RoleChips.Add(chip);
        }
        strip.ActiveSelf.Value = false;

        listingRow.Kick = BuildAction(row, listingRow, "Kick",
            GroupMetrics.PrimaryRight + 2f * (GroupMetrics.MemberActionWidth + GroupMetrics.PillGap), 0);
        listingRow.Ban = BuildAction(row, listingRow, "Ban",
            GroupMetrics.PrimaryRight + GroupMetrics.MemberActionWidth + GroupMetrics.PillGap, 1);
        listingRow.Owner = BuildAction(row, listingRow, "Owner", GroupMetrics.PrimaryRight, 2);

        // The slice lives on the second line, under the name, because it belongs to the person rather than
        // to the row's actions.
        var sliderHost = SettingsUI.Child(row, "Slice", new float2(1f, 0f), new float2(1f, 0f),
            new float2(-(GroupMetrics.PrimaryRight + GroupMetrics.SliderValueWidth + GroupMetrics.PillGap
                + GroupMetrics.SliderWidth), 8f),
            new float2(-(GroupMetrics.PrimaryRight + GroupMetrics.SliderValueWidth + GroupMetrics.PillGap), 32f));
        builder.PushStyle();
        builder.ForegroundColor(DashTheme.Accent);
        builder.NestInto(sliderHost);
        var slider = builder.Slider(0f, 0f, 1f, null, DashTheme.Field);
        builder.NestOut();
        builder.PopStyle();
        var sliderRect = SettingsUI.Rect(slider.Slot);
        SettingsUI.SetAnchors(sliderRect, float2.Zero, float2.One);
        SettingsUI.SetOffsets(sliderRect, float2.Zero, float2.Zero);
        // Same wiring the settings slider rows use: a slider's value event is a plain C# event, so the
        // handler closes over the ROW and reads whichever item is bound to it at fire time. The drag only
        // moves the number; the call to the service waits for the release, so dragging across the whole
        // track is one request rather than one per sample. -xlinka
        var screen = _screen;
        slider.ValueChanged += (_, raw) =>
        {
            if (listingRow.Item is GroupMemberItem member && member.ShowStorage)
                screen.PreviewStorage(member.UserId, raw);
        };
        slider.Released += _ =>
        {
            if (listingRow.Item is GroupMemberItem member && member.ShowStorage)
                screen.CommitStorage(member.UserId, slider.Value.Value);
        };
        sliderHost.ActiveSelf.Value = false;
        listingRow.SliderHost = sliderHost;
        listingRow.Bar = slider;

        listingRow.SliceText = SettingsUI.Label(row, "SliceValue", _fonts.Body, DashTheme.FontSmall,
            DashTheme.TextDim, TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 0f),
            new float2(-(GroupMetrics.PrimaryRight + GroupMetrics.SliderValueWidth), 8f),
            new float2(-GroupMetrics.PrimaryRight, 32f));
        listingRow.SliceText.Slot.ActiveSelf.Value = false;
        return listingRow;
    }

    private SettingsChip BuildAction(Slot row, ListingRow listingRow, string name, float right, int index)
    {
        var host = SettingsUI.Child(row, name, new float2(1f, 1f), new float2(1f, 1f),
            new float2(-(right + GroupMetrics.MemberActionWidth), -35f), new float2(-right, -9f));
        var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontLabel, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, index);
        chip.Button.SetAction(_relay.OnMemberActionPressed);
        host.ActiveSelf.Value = false;
        return chip;
    }

    private sealed class MemberRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Name;
        public required Text Used;
        public required Text RoleText;
        public required GroupsScreen Screen;

        public readonly List<SettingsChip> RoleChips = new();
        public Slot Strip = null!;
        public SettingsChip Kick = null!;
        public SettingsChip Ban = null!;
        public SettingsChip Owner = null!;
        public Slot SliderHost = null!;
        public Slider Bar = null!;
        public Text SliceText = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupMemberItem member)
                return;

            ListingStyle.SetText(Name, member.IsSelf
                ? member.Name + "  (" + GroupsLocale.You.Resolve() + ")"
                : member.Name);

            bool pills = member.Roles.Count > 0;
            ListingStyle.SetActive(Strip, pills);
            ListingStyle.SetActive(RoleText.Slot, !pills);
            if (!pills)
                ListingStyle.SetText(RoleText, member.Role);

            for (int i = 0; i < RoleChips.Count; i++)
            {
                bool used = pills && i < member.Roles.Count;
                ListingStyle.SetActive(RoleChips[i].Slot, used);
                if (!used)
                    continue;
                var rect = SettingsUI.Rect(RoleChips[i].Slot);
                SettingsUI.SetAnchors(rect, new float2(i / (float)member.Roles.Count, 0f),
                    new float2((i + 1) / (float)member.Roles.Count, 1f));
                SettingsUI.SetOffsets(rect, float2.Zero, new float2(-SettingsMetrics.SegmentGap, 0f));
                bool lit = string.Equals(member.Roles[i], member.Role, StringComparison.Ordinal);
                GroupBand.PaintAction(RoleChips[i], member.Roles[i], true, false, lit);
            }

            ListingStyle.SetActive(Kick.Slot, member.ShowKick);
            if (member.ShowKick)
                GroupBand.PaintAction(Kick, GroupsLocale.Kick.Resolve(), true, true, false);

            ListingStyle.SetActive(Ban.Slot, member.ShowBan);
            if (member.ShowBan)
            {
                GroupBand.PaintAction(Ban,
                    member.BanArmed ? GroupsLocale.Confirm.Resolve() : GroupsLocale.Ban.Resolve(), true, true, false);
                if (member.BanArmed)
                    Ban.SetPaint(GroupPaint.ArmedFill, GroupPaint.DangerHover, DashTheme.Negative, DashTheme.Text);
            }

            ListingStyle.SetActive(Owner.Slot, member.ShowMakeOwner);
            if (member.ShowMakeOwner)
                GroupBand.PaintAction(Owner, GroupsLocale.MakeOwner.Resolve(), true, false, false);

            ListingStyle.SetActive(SliderHost, member.ShowStorage);
            ListingStyle.SetActive(SliceText.Slot, member.ShowStorage);
            ListingStyle.SetActive(Used.Slot, member.ShowStorage);
            if (!member.ShowStorage)
                return;

            long pending = member.PendingSlice?.Invoke(member.UserId) ?? member.Allocated;
            float max = System.Math.Max(1f, member.SliderMax);
            ListingStyle.SetSlider(Bar, 0f, max, System.Math.Clamp(pending, 0L, member.SliderMax));
            ListingStyle.SetText(SliceText, GroupsScreen.FormatBytes(pending));
            ListingStyle.SetText(Used, GroupsLocale.Used(GroupsScreen.FormatBytes(member.UsedBytes)).Resolve());
        }
    }
}

// A PERSON WITH ANSWERS: a join request, an invite, a ban.
internal sealed class GroupPersonTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;

    public GroupPersonTemplate(SettingsFonts fonts, GroupsRelay relay)
    {
        _fonts = fonts;
        _relay = relay;
    }

    public override float Height => GroupMetrics.PersonHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var name = SettingsUI.Label(row, "Name", _fonts.Medium, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(SettingsMetrics.RowInset, -28f),
            new float2(-(GroupMetrics.SecondaryRight + GroupMetrics.ActionWidth), -6f), shrinkToFit: true);
        var note = SettingsUI.Label(row, "Note", _fonts.Body, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 0f),
            new float2(SettingsMetrics.RowInset, 6f),
            new float2(-(GroupMetrics.SecondaryRight + GroupMetrics.ActionWidth), 22f), shrinkToFit: true);

        var listingRow = new PersonRow { Background = background, Name = name, Note = note };
        listingRow.Secondary = BuildAction(row, listingRow, "Second", GroupMetrics.SecondaryRight, 1);
        listingRow.Primary = BuildAction(row, listingRow, "Primary", GroupMetrics.PrimaryRight, 0);
        return listingRow;
    }

    private SettingsChip BuildAction(Slot row, ListingRow listingRow, string name, float right, int index)
    {
        var chip = SettingsChip.Build(GroupBand.Right(row, name, right, GroupMetrics.ActionWidth),
            _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, index);
        chip.Button.SetAction(_relay.OnPersonPressed);
        chip.Slot.ActiveSelf.Value = false;
        return chip;
    }

    private sealed class PersonRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Name;
        public required Text Note;

        public SettingsChip Primary = null!;
        public SettingsChip Secondary = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupPersonItem person)
                return;

            ListingStyle.SetText(Name, person.Name);
            ListingStyle.SetText(Note, person.Note);
            ListingStyle.SetActive(Note.Slot, person.Note.Length > 0);

            ListingStyle.SetActive(Primary.Slot, person.PrimaryLabel.Length > 0);
            if (person.PrimaryLabel.Length > 0)
                GroupBand.PaintAction(Primary, person.PrimaryLabel, true, person.PrimaryDanger, !person.PrimaryDanger);

            ListingStyle.SetActive(Secondary.Slot, person.SecondaryLabel.Length > 0);
            if (person.SecondaryLabel.Length > 0)
                GroupBand.PaintAction(Secondary, person.SecondaryLabel, true, person.SecondaryDanger, false);
        }
    }
}

// AN EVENT OR AN ANNOUNCEMENT
internal sealed class GroupPostTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;

    public GroupPostTemplate(SettingsFonts fonts, GroupsRelay relay)
    {
        _fonts = fonts;
        _relay = relay;
    }

    public override float Height => GroupMetrics.PostHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var body = SettingsUI.Label(row, "Body", _fonts.Medium, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(SettingsMetrics.RowInset, -30f),
            new float2(-GroupMetrics.PostTextRight, -6f),
            shrinkToFit: true);
        var meta = SettingsUI.Label(row, "Meta", _fonts.Body, DashTheme.FontLabel, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 0f),
            new float2(SettingsMetrics.RowInset, 6f),
            new float2(-GroupMetrics.PostTextRight, 24f),
            shrinkToFit: true);

        var listingRow = new PostRow { Background = background, Body = body, Meta = meta };
        listingRow.Delete = BuildChip(row, listingRow, "Delete", GroupMetrics.PrimaryRight,
            GroupMetrics.ActionWidth, 0);
        listingRow.Host = BuildChip(row, listingRow, "Host", GroupMetrics.PostHostRight,
            GroupMetrics.PostActionWidth, 1);
        listingRow.Live = BuildChip(row, listingRow, "Live", GroupMetrics.PostLiveRight,
            GroupMetrics.PostActionWidth, 2);
        return listingRow;
    }

    private SettingsChip BuildChip(Slot row, ListingRow listingRow, string name, float right, float width,
        int index)
    {
        var chip = SettingsChip.Build(GroupBand.Right(row, name, right, width),
            _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, index);
        chip.Button.SetAction(_relay.OnPostPressed);
        chip.Slot.ActiveSelf.Value = false;
        return chip;
    }

    private sealed class PostRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Body;
        public required Text Meta;

        public SettingsChip Delete = null!;
        public SettingsChip Host = null!;
        public SettingsChip Live = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupPostItem post)
                return;
            ListingStyle.SetText(Body, post.Body);
            ListingStyle.SetText(Meta, post.Meta);
            ListingStyle.SetActive(Meta.Slot, post.Meta.Length > 0);
            ListingStyle.SetActive(Delete.Slot, post.CanDelete);
            if (post.CanDelete)
                GroupBand.PaintAction(Delete, GroupsLocale.Delete.Resolve(), true, true, false);

            ListingStyle.SetActive(Host.Slot, post.CanHost);
            if (post.CanHost)
                GroupBand.PaintAction(Host, GroupsLocale.HostEventWorld.Resolve(), true, false, false);

            // Lit, because a session that is up right now is the one thing on this row worth pressing.
            ListingStyle.SetActive(Live.Slot, post.IsLive);
            if (post.IsLive)
                GroupBand.PaintAction(Live, GroupsLocale.EventLive.Resolve(), true, false, true);
        }
    }
}

// A TYPING FIELD
//
// There is no text input widget in the dash: the field is a pressable well whose label shows the buffer
// the screen is holding, and the screen routes keystrokes to whichever field is focused. Same model the
// create form has always used, just one per editable value instead of one per screen. -xlinka
internal sealed class GroupFieldTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;

    public GroupFieldTemplate(SettingsFonts fonts, GroupsRelay relay)
    {
        _fonts = fonts;
        _relay = relay;
    }

    public override float Height => GroupMetrics.FieldHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var label = SettingsUI.Label(row, "Label", _fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, new float2(0f, 1f),
            new float2(SettingsMetrics.RowInset, 0f),
            new float2(SettingsMetrics.RowInset + GroupMetrics.FieldLabelWidth, 0f), shrinkToFit: true);

        var wellHost = SettingsUI.Child(row, "Well", new float2(0f, 0.5f), new float2(1f, 0.5f),
            new float2(SettingsMetrics.RowInset + GroupMetrics.FieldLabelWidth + GroupMetrics.IconGap, -14f),
            new float2(-(GroupMetrics.PrimaryRight + GroupMetrics.FieldPillWidth + GroupMetrics.PillGap), 14f));
        var well = SettingsUI.Panel(wellHost, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusControl);
        var wellButton = wellHost.AttachComponent<Button>();
        var value = SettingsUI.Label(wellHost, "Value", _fonts.Body, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, float2.One, new float2(10f, 0f), new float2(-10f, 0f),
            shrinkToFit: true);

        var listingRow = new FieldRow
        {
            Background = background,
            Label = label,
            Well = well,
            Value = value,
        };
        _relay.Register(wellButton, listingRow, 0);
        wellButton.SetAction(_relay.OnFieldPressed);

        var chip = SettingsChip.Build(
            GroupBand.Right(row, "Pill", GroupMetrics.PrimaryRight, GroupMetrics.FieldPillWidth),
            _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
        _relay.Register(chip.Button, listingRow, 1);
        chip.Button.SetAction(_relay.OnFieldPillPressed);
        chip.Slot.ActiveSelf.Value = false;
        listingRow.Pill = chip;
        return listingRow;
    }

    private sealed class FieldRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Label;
        public required RoundedPanel Well;
        public required Text Value;

        public SettingsChip Pill = null!;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            if (item is not GroupFieldItem field)
                return;

            ListingStyle.SetText(Label, field.Label);
            string text = field.Read?.Invoke() ?? string.Empty;
            bool empty = text.Length == 0;
            ListingStyle.SetText(Value, empty ? field.Placeholder.Resolve() : text);
            ListingStyle.SetTextColor(Value, empty ? DashTheme.TextMuted : DashTheme.Text);

            // The focused field takes the accent ring. Nothing else on this page does, so there is never a
            // question about where a keystroke is going.
            bool focused = field.Focused?.Invoke() ?? false;
            SettingsUI.SetPaint(Well, DashTheme.Field, focused ? DashTheme.Accent : DashTheme.Outline,
                focused ? 2f : DashTheme.OutlineWidth);

            ListingStyle.SetActive(Pill.Slot, field.PillLabel.Length > 0);
            if (field.PillLabel.Length > 0)
                GroupBand.PaintAction(Pill, field.PillLabel, true, false, true);
        }
    }
}

// A ROW OF ANSWERS
internal sealed class GroupActionsTemplate : ListingRowTemplate
{
    private const int MaxButtons = 5;

    private readonly SettingsFonts _fonts;
    private readonly GroupsRelay _relay;

    public GroupActionsTemplate(SettingsFonts fonts, GroupsRelay relay)
    {
        _fonts = fonts;
        _relay = relay;
    }

    public override float Height => GroupMetrics.ActionsHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var listingRow = new ActionsRow();
        for (int i = 0; i < MaxButtons; i++)
        {
            var host = GroupBand.Left(row, "Action", 0f, GroupMetrics.ActionWidth, 30f);
            var chip = SettingsChip.Build(host, _fonts.Medium, DashTheme.FontSmall, DashTheme.RadiusControl);
            host.ActiveSelf.Value = false;
            _relay.Register(chip.Button, listingRow, i);
            chip.Button.SetAction(_relay.OnActionPressed);
            listingRow.Chips.Add(chip);
        }
        return listingRow;
    }

    private sealed class ActionsRow : ListingRow
    {
        public readonly List<SettingsChip> Chips = new();

        public override void Bind(ListingItem item)
        {
            var actions = item as GroupActionsItem;
            int count = actions?.Buttons.Count ?? 0;
            float cursor = SettingsMetrics.RowInset;
            for (int i = 0; i < Chips.Count; i++)
            {
                bool used = i < count;
                ListingStyle.SetActive(Chips[i].Slot, used);
                if (!used)
                    continue;
                var entry = actions!.Buttons[i];
                var rect = SettingsUI.Rect(Chips[i].Slot);
                SettingsUI.SetOffsets(rect, new float2(cursor, -15f), new float2(cursor + entry.Width, 15f));
                cursor += entry.Width + GroupMetrics.PillGap;
                GroupBand.PaintAction(Chips[i], entry.Label, true, entry.Danger, entry.Lit);
            }
        }
    }
}

// THE POOL
//
// One bar, two fills. The dark one is what the group has actually stored and the lighter one behind it is
// what its members have been promised, because those are two different facts and a single bar can only
// ever tell you one of them. -xlinka
internal sealed class GroupStorageTemplate : ListingRowTemplate
{
    private readonly SettingsFonts _fonts;

    public GroupStorageTemplate(SettingsFonts fonts) => _fonts = fonts;

    public override float Height => GroupMetrics.StorageHeight;
    public override bool UsesRowBackground => false;
    public override void ConfigureRow(Slot row, ListingStyle style) { }

    public override ListingRow Build(ListingView view, UIBuilder builder, Slot row)
    {
        var background = SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var caption = SettingsUI.Label(row, "Caption", _fonts.Body, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(SettingsMetrics.RowInset, -26f), new float2(-SettingsMetrics.RowInset, -6f),
            shrinkToFit: true);

        var track = SettingsUI.Child(row, "Track", new float2(0f, 0f), new float2(1f, 0f),
            new float2(SettingsMetrics.RowInset, 12f), new float2(-SettingsMetrics.RowInset, 22f));
        SettingsUI.Panel(track, DashTheme.Field, color.Transparent, 5f);

        var allocatedSlot = SettingsUI.Child(track, "Allocated", float2.Zero, new float2(0f, 1f),
            float2.Zero, float2.Zero);
        var allocated = SettingsUI.Panel(allocatedSlot, DashTheme.AccentSoft, color.Transparent, 5f);

        var usedSlot = SettingsUI.Child(track, "Used", float2.Zero, new float2(0f, 1f), float2.Zero, float2.Zero);
        var used = SettingsUI.Panel(usedSlot, DashTheme.Accent, color.Transparent, 5f);

        return new StorageRow
        {
            Background = background,
            Caption = caption,
            AllocatedRect = SettingsUI.Rect(allocatedSlot),
            Allocated = allocated,
            UsedRect = SettingsUI.Rect(usedSlot),
            Used = used,
        };
    }

    private sealed class StorageRow : ListingRow
    {
        public required RoundedPanel Background;
        public required Text Caption;
        public required RectTransform AllocatedRect;
        public required RoundedPanel Allocated;
        public required RectTransform UsedRect;
        public required RoundedPanel Used;

        public override void Bind(ListingItem item)
        {
            SettingsUI.SetPaint(Background, DashTheme.Surface, DashTheme.Outline);
            ListingStyle.SetText(Caption, item.Label);
            if (item is not GroupStorageItem storage)
                return;

            float quota = storage.Quota > 0 ? storage.Quota : 0f;
            float usedFraction = quota > 0f ? System.Math.Clamp(storage.Used / quota, 0f, 1f) : 0f;
            float allocFraction = quota > 0f ? System.Math.Clamp(storage.Allocated / quota, 0f, 1f) : 0f;
            SettingsUI.SetAnchors(UsedRect, float2.Zero, new float2(usedFraction, 1f));
            SettingsUI.SetAnchors(AllocatedRect, float2.Zero, new float2(allocFraction, 1f));
            SettingsUI.SetPaint(Used, quota > 0f ? DashTheme.Accent : DashTheme.Field, color.Transparent);
            SettingsUI.SetPaint(Allocated, DashTheme.AccentSoft, color.Transparent);
        }
    }
}
