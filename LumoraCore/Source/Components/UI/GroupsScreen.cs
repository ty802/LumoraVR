// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Helio.UI;
using Helio.UI.Layout;
using Helio.UI.Listing;
using Lumora.Core.Components.Network;
using Lumora.Core.Components.UI.Worlds;
using Lumora.Core.Localization;
using Lumora.Core.Math;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Components.UI;

// Dashboard "Groups" screen: browse and join groups, read a group's page, and manage one you run.
//
// Four views over one virtualized Listing, not four screens. The chrome at the top is a plain row that
// gets rebuilt when the view changes; everything under it is items, so a page with two members and a page
// with two hundred cost the same and the scroll holds its place across a refresh.
//
// Every control here calls a real route. Where the service has nothing to answer with (setting the icon,
// hosting for a group) there is no control, rather than one that looks pressable and does nothing.
// -xlinka
[ComponentCategory("Hidden")]
public sealed class GroupsScreen : WidgetScreen, IDashboardKeyInput
{
    public const string KindNote = "grpnote";
    public const string KindCard = "grpcard";
    public const string KindHeaderCard = "grphead";
    public const string KindMember = "grpmember";
    public const string KindPerson = "grpperson";
    public const string KindPost = "grppost";
    public const string KindField = "grpfield";
    public const string KindActions = "grpactions";
    public const string KindStorage = "grpstorage";

    // Between the viewport's right edge and the scrollbar track.
    private const float ScrollbarGap = 6f;
    private const float HeaderHeight = 38f;
    // Seconds a destructive pill stays armed after the first press.
    private const double ConfirmWindow = 3.0;
    // The slice slider moves in whole 64 MB steps: a byte-exact drag is not a thing anybody wants.
    private const long SliceStep = 64L * 1024L * 1024L;

    // Highest first. The service refuses anything a caller has no rank for, so this is only what the page
    // is willing to OFFER; it is never the enforcement.
    private static readonly string[] RoleLadder = { "Owner", "Admin", "Moderator", "Builder", "Member" };

    private enum View { Browse, Mine, Group, Admin }

    private enum LoadState { Idle, Loading, Loaded, Error }

    private readonly ListingItemSource _items = new();
    private readonly SettingsFonts _fonts = new();
    private ListingView? _listing;
    private DashScrollbar? _scrollbar;
    private GroupsRelay? _relay;
    private GroupIconCache? _icons;
    private Slot? _header;
    private Slot? _body;
    private Text? _searchText;
    private bool _localeHooked;

    private View _view = View.Browse;

    private LoadState _listLoad = LoadState.Idle;
    private List<GroupInfo> _browse = new();
    private List<GroupInfo> _mine = new();
    private List<GroupInviteSummary> _myInvites = new();
    private string? _listError;

    private string _groupId = string.Empty;
    private LoadState _groupLoad = LoadState.Idle;
    private GroupInfo? _group;
    private List<GroupMemberInfo> _members = new();
    private List<GroupEventInfo> _events = new();
    private List<GroupAnnouncementInfo> _announcements = new();
    private string? _groupError;

    private LoadState _adminLoad = LoadState.Idle;
    private List<GroupJoinRequestInfo> _requests = new();
    private List<GroupInviteInfo> _invites = new();
    private List<GroupBanInfo> _bans = new();
    private string? _adminError;

    private bool _createActive;
    private string _createName = string.Empty;
    private string _createVisibility = "Public";
    private string? _createError;

    private string _search = string.Empty;
    private string _editName = string.Empty;
    private string _editTag = string.Empty;
    private string _editBio = string.Empty;
    private string _editColor = string.Empty;
    private string _editVisibility = "Public";
    private bool _editLoaded;
    private string _inviteId = string.Empty;
    private string _announcement = string.Empty;
    private string _eventTitle = string.Empty;
    private string _eventWorld = string.Empty;
    private string _eventDate = string.Empty;
    private string _eventTime = string.Empty;

    private GroupField _focus = GroupField.None;
    private string _armed = string.Empty;
    private DateTime _armedAt;

    private readonly Dictionary<string, long> _pendingSlice = new(StringComparer.Ordinal);

    // LIVE EVENTS
    // Which of this group's events currently have a session up, and which session that is. Read off the
    // browser's own cache on the tick this screen already runs - no polling of our own, and no request:
    // the browser is discovering sessions anyway for as long as the Worlds tab has been opened once.
    // -xlinka
    private readonly Dictionary<string, SessionListEntry> _liveEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionListEntry> _liveByName = new(StringComparer.Ordinal);
    private float _liveScanCountdown;

    protected override float RowHeight => HeaderHeight;

    private static LumoraClient? Client => Engine.Current?.CDNClient;
    private static string MyId => Client?.AccountUserId ?? string.Empty;
    private static bool SignedIn => Client != null && Client.IsAuthenticated;

    protected override void OnShow()
    {
        base.OnShow();
        if (_view == View.Group || _view == View.Admin)
        {
            if (_groupId.Length > 0)
                LoadGroup(_groupId);
        }
        else
        {
            LoadList();
        }
        World?.RunInUpdates(2, () => _scrollbar?.Refresh());
    }

    public override void OnDestroy()
    {
        if (_localeHooked)
        {
            LocaleManager.Changed -= OnLocaleChanged;
            _localeHooked = false;
        }
        _icons?.Clear();
        _icons = null;
        base.OnDestroy();
    }

    // The scrollbar is sized off rects the listing pins from its own update, so it lands a frame behind
    // whatever just changed the item set. Every write inside Refresh is equality-gated.
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!Slot.ActiveSelf.Value)
            return;
        _scrollbar?.Refresh();

        // A destructive pill that was armed and never pressed again goes back to saying what it does.
        if (_armed.Length > 0 && (DateTime.UtcNow - _armedAt).TotalSeconds > ConfirmWindow)
        {
            _armed = string.Empty;
            Rebuild();
        }

        _liveScanCountdown -= delta;
        if (_liveScanCountdown <= 0f)
        {
            _liveScanCountdown = LiveScanSeconds;
            ScanLiveEvents();
        }
    }

    // Sessions are re-reported about once a second, so asking more often than that buys nothing. A
    // rebuild only happens when the answer actually moves; a room that stays up costs one dictionary
    // walk a second and nothing else.
    private const float LiveScanSeconds = 1f;

    private void ScanLiveEvents()
    {
        if (IsDestroyed || _view != View.Group || _groupId.Length == 0 || _events.Count == 0)
        {
            if (_liveEvents.Count > 0)
            {
                _liveEvents.Clear();
                Rebuild();
            }
            return;
        }

        _liveByName.Clear();
        var sessions = FindBrowser()?.GetSessions();
        if (sessions != null)
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                var entry = sessions[i];
                if (entry == null || entry.JoinUrl == null)
                    continue;
                if (!GroupSessionTags.TryRead(entry.Tags, out var id, out _)
                    || !string.Equals(id, _groupId, StringComparison.Ordinal))
                    continue;
                string key = WorldGroup.NormalizeName(entry.Name);
                if (!_liveByName.ContainsKey(key))
                    _liveByName[key] = entry;
            }
        }

        bool changed = false;
        for (int i = 0; i < _events.Count; i++)
        {
            var entry = _events[i];
            if (entry == null || string.IsNullOrEmpty(entry.Id))
                continue;
            _liveByName.TryGetValue(WorldGroup.NormalizeName(entry.Title), out var session);
            bool wasLive = _liveEvents.ContainsKey(entry.Id);
            if (session != null)
            {
                _liveEvents[entry.Id] = session;
                changed |= !wasLive;
            }
            else if (wasLive)
            {
                _liveEvents.Remove(entry.Id);
                changed = true;
            }
        }

        if (changed)
            Rebuild();
    }

    // The browser lives on the userspace root, one per client. Read only: the Worlds screen is what
    // creates it and starts discovery, and this page will not start scanning on its own.
    private static SessionBrowser? FindBrowser()
    {
        var root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;
        return root.GetComponent<SessionBrowser>();
    }

    private void OnLocaleChanged()
    {
        if (IsDestroyed)
            return;
        BuildHeader();
        Rebuild();
    }

    // BUILD

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        _fonts.Regular = _dashboard?.Font.Target;
        _fonts.Semibold = _dashboard?.FontSemibold.Target;
        _fonts.Bold = _dashboard?.FontBold.Target;

        var root = builder.Current;
        var col = root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 8f;
        col.PaddingLeft.Value = 12f;
        col.PaddingRight.Value = 12f;
        col.PaddingTop.Value = 12f;
        col.PaddingBottom.Value = 12f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        _header = BeginRow(root, "Header");

        _body = root.AddSlot("Body");
        _body.AttachComponent<RectTransform>();
        var bodyElement = _body.AttachComponent<LayoutElement>();
        bodyElement.FlexibleWidth.Value = 1f;
        bodyElement.FlexibleHeight.Value = 1f;

        BuildListing(_body);
        BuildHeader();

        if (!_localeHooked)
        {
            LocaleManager.Changed += OnLocaleChanged;
            _localeHooked = true;
        }

        LoadList();
    }

    private void BuildListing(Slot body)
    {
        // House palette, not the ListingStyle defaults: those are raw literals that never went through the
        // theme's sRGB decode, so they land about two stops brighter than they read. RoundedSprite stays
        // null because every row here paints a procedural rounded panel. -xlinka
        var style = new ListingStyle
        {
            Font = _fonts.Body,
            CornerRadius = DashTheme.RadiusControl,
            RowHeight = SettingsMetrics.RowHeight,
            RowSpacing = DashTheme.Gap,
            RowFill = DashTheme.Surface,
            RowBorder = DashTheme.Outline,
            RowSelectedFill = DashTheme.SurfaceHover,
            ControlFill = DashTheme.Surface,
            NeutralFill = DashTheme.Surface,
            AccentFill = DashTheme.Accent,
            WarningFill = DashTheme.Negative,
            DisabledFill = DashTheme.Field,
            Accent = DashTheme.Accent,
            TextPrimary = DashTheme.Text,
            TextDim = DashTheme.TextDim,
            TextDisabled = DashTheme.TextMuted,
            HeaderText = DashTheme.TextMuted,
        };

        // Body runs no layout controller, so the host stretches to it by anchors. A flexible LayoutElement
        // under a slot with no layout is laid out by nobody and collapses to a point at the centre. -xlinka
        var host = body.AddSlot("Groups");
        var hostRect = host.AttachComponent<RectTransform>();
        hostRect.AnchorMin.Value = float2.Zero;
        hostRect.AnchorMax.Value = float2.One;
        hostRect.OffsetMin.Value = float2.Zero;
        hostRect.OffsetMax.Value = float2.Zero;

        _relay = host.AttachComponent<GroupsRelay>();
        _relay.Screen = this;

        // Asset providers on their own hidden slot so a picture never sits in the middle of a layout.
        var iconHost = host.AddSlot("Icons");
        iconHost.ActiveSelf.Value = false;
        _icons = new GroupIconCache(iconHost);

        var viewport = SettingsUI.Fill(host, "Viewport", SettingsMetrics.PanelInset, SettingsMetrics.PanelInset,
            SettingsMetrics.PanelInset + DashScrollbar.Width + ScrollbarGap, 0f);

        var templates = new ListingTemplateMapper { DefaultHeight = SettingsMetrics.RowHeight };
        var note = new GroupNoteTemplate(_fonts);
        templates.Map<ListingHeader>(new SettingsSectionTemplate(_fonts));
        templates.MapKind(KindNote, note);
        templates.MapKind(KindCard, new GroupCardTemplate(_fonts, _relay, _icons));
        templates.MapKind(KindHeaderCard, new GroupHeaderTemplate(_fonts, _icons));
        templates.MapKind(KindMember, new GroupMemberTemplate(_fonts, _relay, this));
        templates.MapKind(KindPerson, new GroupPersonTemplate(_fonts, _relay));
        templates.MapKind(KindPost, new GroupPostTemplate(_fonts, _relay));
        templates.MapKind(KindField, new GroupFieldTemplate(_fonts, _relay));
        templates.MapKind(KindActions, new GroupActionsTemplate(_fonts, _relay));
        templates.MapKind(KindStorage, new GroupStorageTemplate(_fonts));
        templates.Fallback(note);

        _listing = ListingView.Attach(viewport, style, templates, _items);

        _scrollbar = DashScrollbar.OnRightEdge(host, SettingsMetrics.PanelInset, 0f, SettingsMetrics.PanelInset);
        _scrollbar.Bind(_listing.Scroll, SettingsUI.Rect(viewport),
            _listing.ContentSlot == null ? null : SettingsUI.Rect(_listing.ContentSlot), MarkDirty);
        host.World?.RunInUpdates(2, () => _scrollbar?.Refresh());
    }

    private void BuildHeader()
    {
        var header = _header;
        if (header == null || header.IsDestroyed)
            return;
        header.DestroyChildren();
        _searchText = null;

        var titleBuilder = RowBuilder(header);
        titleBuilder.MinWidth(120f).FlexibleWidth(1f);
        string title = _view == View.Group || _view == View.Admin
            ? (_group?.Name ?? GroupsLocale.Title.Resolve())
            : GroupsLocale.Title.Resolve();
        AddRowLabel(titleBuilder, title, DashTheme.FontHeading, SectionTitleColor, TextHorizontalAlignment.Left);

        if (_view == View.Group || _view == View.Admin)
        {
            AddInlineButton(header, GroupsLocale.Back.Resolve(), TabFill, 84f, BackToList);
            AddInlineButton(header, GroupsLocale.Refresh.Resolve(), TabFill, 88f, RefreshCurrent);
            return;
        }

        BuildSearchWell(header);
        AddInlineButton(header, GroupsLocale.TabBrowse.Resolve(),
            _view == View.Browse ? AccentColor : TabFill, 84f, () => SwitchTab(View.Browse));
        AddInlineButton(header, GroupsLocale.TabMine.Resolve(),
            _view == View.Mine ? AccentColor : TabFill, 74f, () => SwitchTab(View.Mine));
        AddInlineButton(header, GroupsLocale.New.Resolve(), AccentColor, 66f, OpenCreate);
        AddInlineButton(header, GroupsLocale.Refresh.Resolve(), TabFill, 88f, RefreshCurrent);
    }

    // A pressable well whose label is the search buffer. There is no text input widget in the dash, so the
    // screen holds the string and routes keystrokes at whichever well is focused.
    private void BuildSearchWell(Slot header)
    {
        var cell = header.AddSlot("SearchWell");
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 200f;
        element.PreferredWidth.Value = 200f;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(cell, ControlFill, _focus == GroupField.Search ? AccentColor : RowBorder);
        var button = cell.AttachComponent<Button>();
        button.Clicked += (_, _) => FocusField(GroupField.Search);
        bool empty = _search.Length == 0;
        _searchText = AddFillLabel(cell, empty ? GroupsLocale.SearchHint.Resolve() : _search,
            DashTheme.FontBody, empty ? TextDim : TextPrimary);
        _searchText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    // ITEMS

    private void Rebuild()
    {
        if (IsDestroyed || Slot == null)
            return;

        _items.ClearAll();
        if (!SignedIn)
        {
            AddNote("signedout", GroupsLocale.SignedOut);
            PushSearch();
            MarkDirty();
            return;
        }

        switch (_view)
        {
            case View.Group:
                BuildGroupPage();
                break;
            case View.Admin:
                BuildAdminPage();
                break;
            default:
                BuildList();
                break;
        }

        PushSearch();
        _scrollbar?.Refresh();
        MarkDirty();
    }

    // Only the two list views filter locally. A group page is not a list of things anybody searches.
    private void PushSearch()
    {
        if (_listing == null)
            return;
        _listing.Search = _view == View.Browse || _view == View.Mine ? _search : string.Empty;
    }

    private void BuildList()
    {
        if (_createActive)
            BuildCreateForm();

        switch (_listLoad)
        {
            case LoadState.Loading:
                AddNote("loading", GroupsLocale.Loading);
                return;
            case LoadState.Error:
                AddNote("error", _listError ?? GroupsLocale.LoadFailed.Resolve(), DashTheme.Negative);
                return;
        }

        if (_view == View.Mine)
        {
            if (_myInvites.Count > 0)
            {
                _items.AddRoot(new ListingHeader("h.invites", GroupsLocale.Invites));
                for (int i = 0; i < _myInvites.Count; i++)
                    _items.AddRoot(InviteCard(_myInvites[i]));
            }

            if (_mine.Count == 0)
            {
                AddNote("mine.empty", GroupsLocale.MineEmpty);
                return;
            }
            for (int i = 0; i < _mine.Count; i++)
                _items.AddRoot(MineCard(_mine[i]));
            return;
        }

        if (_browse.Count == 0)
        {
            AddNote("browse.empty", GroupsLocale.BrowseEmpty);
            return;
        }
        for (int i = 0; i < _browse.Count; i++)
            _items.AddRoot(BrowseCard(_browse[i]));
    }

    private void BuildCreateForm()
    {
        _items.AddRoot(new GroupFieldItem("create.name", GroupsLocale.CreateName)
        {
            Field = GroupField.CreateName,
            Placeholder = GroupsLocale.CreateNameHint,
            Read = () => _createName,
            Focused = () => _focus == GroupField.CreateName,
        });
        _items.AddRoot(new GroupFieldItem("create.visibility", GroupsLocale.FieldVisibility)
        {
            Field = GroupField.None,
            Read = () => VisibilityLabel(_createVisibility),
            Focused = () => false,
            PillLabel = VisibilityLabel(NextVisibility(_createVisibility)),
            Pill = GroupAction.CycleVisibility,
        });
        _items.AddRoot(new GroupActionsItem("create.actions")
            .Add(GroupsLocale.Create.Resolve(), GroupAction.Create, danger: false, lit: true)
            .Add(GroupsLocale.Cancel.Resolve(), GroupAction.CancelCreate));
        if (!string.IsNullOrEmpty(_createError))
            AddNote("create.error", _createError!, DashTheme.Negative);
    }

    private GroupCardItem BrowseCard(GroupInfo group)
    {
        var card = BaseCard(group);
        bool member = !string.IsNullOrEmpty(group.MyRole);
        if (member)
        {
            card.Chip = GroupsLocale.Member.Resolve();
            card.ChipLit = false;
        }
        else if (group.Visibility == "Hidden")
        {
            // No route lets a stranger into a hidden group, so the pill says so and does not pretend.
            card.PrimaryLabel = GroupsLocale.VisibilityInvite.Resolve();
            card.PrimaryEnabled = false;
        }
        else
        {
            card.PrimaryLabel = group.Visibility == "Public"
                ? GroupsLocale.Join.Resolve()
                : GroupsLocale.RequestToJoin.Resolve();
            card.Primary = GroupAction.Join;
        }
        return card;
    }

    private GroupCardItem MineCard(GroupInfo group)
    {
        var card = BaseCard(group);
        bool represented = Client?.RepresentedGroup?.Id == group.Id;
        card.Chip = represented ? GroupsLocale.Represented.Resolve() : group.MyRole ?? string.Empty;
        card.ChipLit = represented;
        return card;
    }

    private GroupCardItem InviteCard(GroupInviteSummary invite)
    {
        return new GroupCardItem("invite." + invite.GroupId)
        {
            GroupId = invite.GroupId,
            Name = invite.Name,
            GroupTag = invite.Tag,
            IconHash = invite.IconHash,
            Tint = ParseColor(invite.Color),
            PrimaryLabel = GroupsLocale.Accept.Resolve(),
            Primary = GroupAction.AcceptInvite,
            SecondaryLabel = GroupsLocale.Decline.Resolve(),
            Secondary = GroupAction.DeclineInvite,
        };
    }

    private GroupCardItem BaseCard(GroupInfo group)
    {
        return new GroupCardItem("group." + group.Id)
        {
            GroupId = group.Id,
            Name = group.Name,
            GroupTag = group.Tag,
            Bio = group.Description,
            IconHash = group.IconHash,
            Tint = ParseColor(group.Color),
            Meta = GroupsLocale.Members(group.MemberCount).Resolve(),
            Visibility = VisibilityLabel(group.Visibility),
        };
    }

    private void BuildGroupPage()
    {
        switch (_groupLoad)
        {
            case LoadState.Loading:
                AddNote("loading", GroupsLocale.Loading);
                return;
            case LoadState.Error:
                AddNote("error", _groupError ?? GroupsLocale.LoadFailed.Resolve(), DashTheme.Negative);
                return;
        }

        var group = _group;
        if (group == null)
        {
            AddNote("missing", GroupsLocale.LoadFailed);
            return;
        }

        _items.AddRoot(new GroupHeaderItem("head")
        {
            Name = group.Name,
            GroupTag = group.Tag,
            Bio = group.Description,
            IconHash = group.IconHash,
            Tint = ParseColor(group.Color),
            Visibility = VisibilityLabel(group.Visibility),
            Meta = GroupsLocale.Members(group.MemberCount).Resolve(),
        });

        BuildStorage(group);
        BuildGroupActions(group);

        if (!string.IsNullOrEmpty(_groupError))
            AddNote("action.error", _groupError!, DashTheme.Negative);

        bool member = !string.IsNullOrEmpty(group.MyRole);
        if (!member)
            return;

        BuildEvents(group);
        BuildAnnouncements(group);

        _items.AddRoot(new ListingHeader("h.members", GroupsLocale.MembersWith(_members.Count)));
        if (_members.Count == 0)
        {
            AddNote("members.empty", GroupsLocale.MembersEmpty);
            return;
        }
        foreach (var entry in SortedMembers())
        {
            _items.AddRoot(new GroupMemberItem("member." + entry.UserId)
            {
                UserId = entry.UserId,
                Name = DisplayName(entry.Username, entry.UserId),
                Role = entry.Role,
                IsSelf = entry.UserId == MyId,
            });
        }
    }

    private void BuildStorage(GroupInfo group)
    {
        long allocated = 0;
        for (int i = 0; i < _members.Count; i++)
            allocated += _members[i].AllocatedBytes;

        _items.AddRoot(new GroupStorageItem("storage", GroupsLocale.StoragePool(
            FormatBytes(group.UsedStorageBytes), FormatBytes(group.StorageQuotaBytes), FormatBytes(allocated)))
        {
            Used = group.UsedStorageBytes,
            Allocated = allocated,
            Quota = group.StorageQuotaBytes,
        });

        if (group.StorageStatus == "Grace")
            AddNote("storage.grace", GroupsLocale.StorageGrace(ShortDate(group.StorageLockAt)), DashTheme.Warning);
        else if (group.StorageStatus == "Locked")
            AddNote("storage.locked", GroupsLocale.StorageLocked, DashTheme.Negative);
    }

    private void BuildGroupActions(GroupInfo group)
    {
        var actions = new GroupActionsItem("actions");
        bool member = !string.IsNullOrEmpty(group.MyRole);
        bool owner = group.MyRole == "Owner";

        if (!member)
        {
            if (group.Visibility == "Hidden")
            {
                actions.Add(GroupsLocale.VisibilityInvite.Resolve(), GroupAction.Join, danger: false, lit: false,
                    width: 130f);
            }
            else
            {
                actions.Add(group.Visibility == "Public"
                    ? GroupsLocale.Join.Resolve()
                    : GroupsLocale.RequestToJoin.Resolve(), GroupAction.Join, danger: false, lit: true, width: 130f);
            }
        }
        else
        {
            bool represented = Client?.RepresentedGroup?.Id == group.Id;
            actions.Add(represented ? GroupsLocale.Represented.Resolve() : GroupsLocale.Represent.Resolve(),
                represented ? GroupAction.Unrepresent : GroupAction.Represent, danger: false, lit: represented,
                width: 178f);
            if (!owner)
                actions.Add(GroupsLocale.Leave.Resolve(), GroupAction.Leave, danger: true);
            if (RankOf(group.MyRole) <= RankOf("Admin"))
                actions.Add(GroupsLocale.Manage.Resolve(), GroupAction.Manage, danger: false, lit: false, width: 110f);
        }

        if (actions.Buttons.Count > 0)
            _items.AddRoot(actions);
    }

    private void BuildEvents(GroupInfo group)
    {
        _items.AddRoot(new ListingHeader("h.events", GroupsLocale.SectionEvents));

        if (CanPost(group))
        {
            _items.AddRoot(Field("event.title", GroupsLocale.EventTitle, GroupField.EventTitle,
                GroupsLocale.EventTitleHint, () => _eventTitle));
            _items.AddRoot(Field("event.world", GroupsLocale.EventWorld, GroupField.EventWorld,
                GroupsLocale.EventWorldHint, () => _eventWorld));
            _items.AddRoot(Field("event.date", GroupsLocale.EventDate, GroupField.EventDate,
                GroupsLocale.EventDateHint, () => _eventDate));
            var time = Field("event.time", GroupsLocale.EventTime, GroupField.EventTime,
                GroupsLocale.EventTimeHint, () => _eventTime);
            time.PillLabel = GroupsLocale.Post.Resolve();
            time.Pill = GroupAction.CreateEvent;
            _items.AddRoot(time);
        }

        if (_events.Count == 0)
        {
            AddNote("events.empty", GroupsLocale.EventsEmpty);
            return;
        }

        for (int i = 0; i < _events.Count; i++)
        {
            var entry = _events[i];
            string where = string.IsNullOrEmpty(entry.WorldName) ? string.Empty : entry.WorldName + ", ";
            _items.AddRoot(new GroupPostItem("event." + entry.Id)
            {
                PostId = entry.Id,
                Body = entry.Title,
                Meta = where + LongDate(entry.StartsAt) + ", "
                    + GroupsLocale.HostedBy(ShortId(entry.HostUserId)).Resolve(),
                CanDelete = entry.HostUserId == MyId || RankOf(group.MyRole) <= RankOf("Moderator"),
                Delete = GroupAction.DeleteEvent,
                // Builder and above, the same rung that may announce the event in the first place. It
                // opens the create form filled in; nothing is hosted until Create is pressed.
                CanHost = CanPost(group),
                HostAction = GroupAction.HostEventWorld,
                IsLive = _liveEvents.ContainsKey(entry.Id),
                LiveAction = GroupAction.JoinEventWorld,
            });
        }
    }

    private void BuildAnnouncements(GroupInfo group)
    {
        _items.AddRoot(new ListingHeader("h.news", GroupsLocale.SectionAnnouncements));

        if (CanPost(group))
        {
            var field = Field("news.text", GroupsLocale.SectionAnnouncements, GroupField.Announcement,
                GroupsLocale.AnnouncementHint, () => _announcement);
            field.PillLabel = GroupsLocale.Post.Resolve();
            field.Pill = GroupAction.PostAnnouncement;
            _items.AddRoot(field);
        }

        if (_announcements.Count == 0)
        {
            AddNote("news.empty", GroupsLocale.AnnouncementsEmpty);
            return;
        }

        for (int i = 0; i < _announcements.Count; i++)
        {
            var entry = _announcements[i];
            _items.AddRoot(new GroupPostItem("news." + entry.Id)
            {
                PostId = entry.Id,
                Body = entry.Text,
                Meta = GroupsLocale.PostedBy(ShortId(entry.AuthorId), LongDate(entry.CreatedAt)).Resolve(),
                CanDelete = entry.AuthorId == MyId || RankOf(group.MyRole) <= RankOf("Moderator"),
                Delete = GroupAction.DeleteAnnouncement,
            });
        }
    }

    private void BuildAdminPage()
    {
        var group = _group;
        if (group == null || RankOf(group.MyRole) > RankOf("Admin"))
        {
            // The service answers 403 for anyone below Admin, so the page does not draw controls it knows
            // will be refused.
            AddNote("admin.denied", GroupsLocale.LoadFailed, DashTheme.Negative);
            return;
        }

        if (_adminLoad == LoadState.Loading)
            AddNote("admin.loading", GroupsLocale.Loading);

        bool owner = group.MyRole == "Owner";

        _items.AddRoot(new ListingHeader("h.edit", GroupsLocale.SectionEdit));
        _items.AddRoot(Field("edit.name", GroupsLocale.FieldName, GroupField.EditName,
            GroupsLocale.CreateNameHint, () => _editName));
        _items.AddRoot(Field("edit.tag", GroupsLocale.FieldTag, GroupField.EditTag,
            GroupsLocale.FieldTagHint, () => _editTag));
        _items.AddRoot(Field("edit.bio", GroupsLocale.FieldBio, GroupField.EditBio,
            GroupsLocale.FieldBioHint, () => _editBio));
        _items.AddRoot(Field("edit.color", GroupsLocale.FieldColor, GroupField.EditColor,
            GroupsLocale.FieldColorHint, () => _editColor));
        _items.AddRoot(new GroupFieldItem("edit.visibility", GroupsLocale.FieldVisibility)
        {
            Field = GroupField.None,
            Read = () => VisibilityLabel(_editVisibility),
            Focused = () => false,
            PillLabel = VisibilityLabel(NextVisibility(_editVisibility)),
            Pill = GroupAction.CycleVisibility,
        });
        _items.AddRoot(new GroupActionsItem("edit.save")
            .Add(GroupsLocale.Save.Resolve(), GroupAction.Save, danger: false, lit: true));

        if (!string.IsNullOrEmpty(_adminError))
            AddNote("admin.error", _adminError!, DashTheme.Negative);

        BuildAdminMembers(group, owner);
        BuildAdminRequests();
        BuildAdminInvites();
        BuildAdminBans();

        _items.AddRoot(new ListingHeader("h.danger", GroupsLocale.SectionDanger));
        if (owner)
        {
            _items.AddRoot(new GroupActionsItem("danger.delete")
                .Add(IsArmed("delete") ? GroupsLocale.Confirm.Resolve() : GroupsLocale.DeleteGroup.Resolve(),
                    GroupAction.DeleteGroup, danger: true, lit: false, width: 140f));
        }
        else
        {
            AddNote("danger.owner", GroupsLocale.NothingToSave);
        }
    }

    private void BuildAdminMembers(GroupInfo group, bool owner)
    {
        _items.AddRoot(new ListingHeader("h.members", GroupsLocale.MembersWith(_members.Count)));
        if (_members.Count == 0)
        {
            AddNote("members.empty", GroupsLocale.MembersEmpty);
            return;
        }

        long allocated = 0;
        for (int i = 0; i < _members.Count; i++)
            allocated += _members[i].AllocatedBytes;
        long free = System.Math.Max(0L, group.StorageQuotaBytes - allocated);

        int myRank = RankOf(group.MyRole);
        var assignable = AssignableRoles(group.MyRole);

        foreach (var entry in SortedMembers())
        {
            bool self = entry.UserId == MyId;
            bool below = RankOf(entry.Role) > myRank;
            // The slider tops out at what is free plus this row's own slice, so lowering somebody is always
            // possible even when the pool is fully handed out. -xlinka
            long sliderMax = free + entry.AllocatedBytes;

            _items.AddRoot(new GroupMemberItem("member." + entry.UserId)
            {
                UserId = entry.UserId,
                Name = DisplayName(entry.Username, entry.UserId),
                Role = entry.Role,
                IsSelf = self,
                Roles = below && !self ? assignable : Array.Empty<string>(),
                ShowStorage = group.StorageQuotaBytes > 0,
                Allocated = entry.AllocatedBytes,
                UsedBytes = entry.UsedBytes,
                SliderMax = sliderMax,
                PendingSlice = PendingSliceFor,
                ShowKick = below && !self,
                ShowBan = below && !self,
                BanArmed = IsArmed("ban." + entry.UserId),
                ShowMakeOwner = owner && !self,
            });
        }
    }

    private void BuildAdminRequests()
    {
        _items.AddRoot(new ListingHeader("h.requests", GroupsLocale.RequestsWith(_requests.Count)));
        if (_requests.Count == 0)
        {
            AddNote("requests.empty", GroupsLocale.RequestsEmpty);
            return;
        }
        for (int i = 0; i < _requests.Count; i++)
        {
            var entry = _requests[i];
            _items.AddRoot(new GroupPersonItem("request." + entry.UserId)
            {
                UserId = entry.UserId,
                Name = DisplayName(entry.Username, entry.UserId),
                Note = LongDate(entry.RequestedAt),
                PrimaryLabel = GroupsLocale.Approve.Resolve(),
                Primary = GroupAction.Approve,
                SecondaryLabel = GroupsLocale.Deny.Resolve(),
                Secondary = GroupAction.Deny,
                SecondaryDanger = true,
            });
        }
    }

    private void BuildAdminInvites()
    {
        _items.AddRoot(new ListingHeader("h.invites", GroupsLocale.SectionInvites));

        var field = Field("invite.id", GroupsLocale.InviteById, GroupField.InviteId,
            GroupsLocale.InviteHint, () => _inviteId);
        field.PillLabel = GroupsLocale.Invite.Resolve();
        field.Pill = GroupAction.Invite;
        _items.AddRoot(field);

        if (_invites.Count == 0)
        {
            AddNote("invites.empty", GroupsLocale.InvitesEmpty);
            return;
        }
        for (int i = 0; i < _invites.Count; i++)
        {
            var entry = _invites[i];
            _items.AddRoot(new GroupPersonItem("invite." + entry.UserId)
            {
                UserId = entry.UserId,
                Name = DisplayName(entry.Username, entry.UserId),
                Note = LongDate(entry.InvitedAt),
                PrimaryLabel = GroupsLocale.Withdraw.Resolve(),
                Primary = GroupAction.Withdraw,
                PrimaryDanger = true,
            });
        }
    }

    private void BuildAdminBans()
    {
        _items.AddRoot(new ListingHeader("h.bans", GroupsLocale.SectionBans));
        if (_bans.Count == 0)
        {
            AddNote("bans.empty", GroupsLocale.BansEmpty);
            return;
        }
        for (int i = 0; i < _bans.Count; i++)
        {
            var entry = _bans[i];
            string note = string.IsNullOrWhiteSpace(entry.Reason)
                ? GroupsLocale.BannedOn(LongDate(entry.BannedAt)).Resolve()
                : GroupsLocale.BannedFor(entry.Reason, LongDate(entry.BannedAt)).Resolve();
            _items.AddRoot(new GroupPersonItem("ban." + entry.UserId)
            {
                UserId = entry.UserId,
                Name = DisplayName(entry.Username, entry.UserId),
                Note = note,
                PrimaryLabel = GroupsLocale.Unban.Resolve(),
                Primary = GroupAction.Unban,
            });
        }
    }

    private GroupFieldItem Field(string key, LocaleText label, GroupField field, LocaleText placeholder,
        Func<string> read)
    {
        return new GroupFieldItem(key, label)
        {
            Field = field,
            Placeholder = placeholder,
            Read = read,
            Focused = () => _focus == field,
        };
    }

    private void AddNote(string key, LocaleText text) => AddNote(key, text, DashTheme.TextMuted);

    private void AddNote(string key, LocaleText text, in color tint)
        => _items.AddRoot(new GroupNoteItem(key, text) { Tint = tint });

    // NAVIGATION

    private void SwitchTab(View view)
    {
        if (_view == view)
            return;
        _view = view;
        _search = string.Empty;
        _focus = GroupField.None;
        BuildHeader();
        LoadList();
    }

    private void OpenGroup(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            return;
        _view = View.Group;
        _groupId = groupId;
        _editLoaded = false;
        _pendingSlice.Clear();
        _focus = GroupField.None;
        BuildHeader();
        LoadGroup(groupId);
    }

    private void OpenAdmin()
    {
        if (_groupId.Length == 0)
            return;
        _view = View.Admin;
        SeedEditBuffers();
        BuildHeader();
        Rebuild();
        LoadAdmin(_groupId);
    }

    private void BackToList()
    {
        if (_view == View.Admin)
        {
            _view = View.Group;
            _armed = string.Empty;
            BuildHeader();
            Rebuild();
            return;
        }
        _view = View.Browse;
        _groupId = string.Empty;
        _group = null;
        _groupError = null;
        _adminError = null;
        _armed = string.Empty;
        _focus = GroupField.None;
        BuildHeader();
        LoadList();
    }

    private void RefreshCurrent()
    {
        if (_view == View.Group || _view == View.Admin)
        {
            if (_groupId.Length > 0)
            {
                LoadGroup(_groupId);
                if (_view == View.Admin)
                    LoadAdmin(_groupId);
            }
            return;
        }
        LoadList();
    }

    // LOADS

    private async void LoadList()
    {
        _listLoad = LoadState.Loading;
        _listError = null;
        Rebuild();

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _listLoad = LoadState.Idle;
            Rebuild();
            return;
        }

        var world = World;
        bool browse = _view == View.Browse;
        string? query = _search.Length > 0 ? _search : null;
        try
        {
            if (browse)
            {
                var result = await client.GetGroups(query, 0, 50);
                OnUi(world, () =>
                {
                    if (_view != View.Browse)
                        return;
                    if (result.Success && result.Data != null)
                    {
                        _browse = result.Data;
                        _listLoad = LoadState.Loaded;
                    }
                    else
                    {
                        _listLoad = LoadState.Error;
                        _listError = result.Message;
                    }
                    Rebuild();
                });
                return;
            }

            var mineTask = client.GetMyGroups();
            var invitesTask = client.GetMyGroupInvites();
            var mine = await mineTask;
            var invites = await invitesTask;
            OnUi(world, () =>
            {
                if (_view != View.Mine)
                    return;
                if (mine.Success && mine.Data != null)
                {
                    _mine = mine.Data;
                    _listLoad = LoadState.Loaded;
                }
                else
                {
                    _listLoad = LoadState.Error;
                    _listError = mine.Message;
                }
                _myInvites = invites.Success && invites.Data != null ? invites.Data : new();
                Rebuild();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () =>
            {
                _listLoad = LoadState.Error;
                _listError = message;
                Rebuild();
            });
        }
    }

    private async void LoadGroup(string groupId)
    {
        _groupLoad = LoadState.Loading;
        _groupError = null;
        Rebuild();

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _groupLoad = LoadState.Idle;
            Rebuild();
            return;
        }

        var world = World;
        try
        {
            var group = await client.GetGroup(groupId);
            bool member = group.Success && group.Data != null && !string.IsNullOrEmpty(group.Data.MyRole);

            // Members, events and announcements are all member-only routes, so a stranger's page is just the
            // header and the join pill rather than three refusals in a row. -xlinka
            ApiResponse<List<GroupMemberInfo>>? members = null;
            ApiResponse<List<GroupEventInfo>>? events = null;
            ApiResponse<List<GroupAnnouncementInfo>>? news = null;
            if (member)
            {
                var membersTask = client.GetGroupMembers(groupId);
                var eventsTask = client.GetGroupEvents(groupId);
                var newsTask = client.GetGroupAnnouncements(groupId);
                members = await membersTask;
                events = await eventsTask;
                news = await newsTask;
            }

            OnUi(world, () =>
            {
                if (_groupId != groupId)
                    return;
                if (!group.Success || group.Data == null)
                {
                    _groupLoad = LoadState.Error;
                    _groupError = group.Message;
                    Rebuild();
                    return;
                }
                _group = group.Data;
                _members = members is { Success: true, Data: not null } ? members.Data : new();
                _events = events is { Success: true, Data: not null } ? events.Data : new();
                _announcements = news is { Success: true, Data: not null } ? news.Data : new();
                _groupLoad = LoadState.Loaded;
                if (!_editLoaded)
                    SeedEditBuffers();
                BuildHeader();
                Rebuild();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () =>
            {
                _groupLoad = LoadState.Error;
                _groupError = message;
                Rebuild();
            });
        }
    }

    private async void LoadAdmin(string groupId)
    {
        _adminLoad = LoadState.Loading;
        _adminError = null;

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _adminLoad = LoadState.Idle;
            return;
        }

        var world = World;
        try
        {
            var requestsTask = client.GetGroupRequests(groupId);
            var invitesTask = client.GetGroupInvites(groupId);
            var bansTask = client.GetGroupBans(groupId);
            var requests = await requestsTask;
            var invites = await invitesTask;
            var bans = await bansTask;

            OnUi(world, () =>
            {
                if (_groupId != groupId)
                    return;
                _requests = requests is { Success: true, Data: not null } ? requests.Data : new();
                _invites = invites is { Success: true, Data: not null } ? invites.Data : new();
                _bans = bans is { Success: true, Data: not null } ? bans.Data : new();
                _adminLoad = LoadState.Loaded;
                if (_view == View.Admin)
                    Rebuild();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () =>
            {
                _adminLoad = LoadState.Error;
                _adminError = message;
                if (_view == View.Admin)
                    Rebuild();
            });
        }
    }

    // ACTIONS

    internal void Dispatch(GroupAction action, string groupId, string subject)
    {
        if (IsDestroyed)
            return;

        string target = groupId.Length > 0 ? groupId : _groupId;
        switch (action)
        {
            case GroupAction.Open:
                OpenGroup(target);
                break;
            case GroupAction.Join:
            case GroupAction.AcceptInvite:
                JoinGroup(target);
                break;
            case GroupAction.DeclineInvite:
                RunAction(c => c.WithdrawGroupInvite(target, MyId), LoadList);
                break;
            case GroupAction.Leave:
                RunAction(c => c.LeaveGroup(target), () => LoadGroup(target));
                break;
            case GroupAction.Represent:
                RunAction(c => c.RepresentGroup(target), () => LoadGroup(target));
                break;
            case GroupAction.Unrepresent:
                RunAction(c => c.ClearRepresentedGroup(), () => LoadGroup(target));
                break;
            case GroupAction.Manage:
                OpenAdmin();
                break;
            case GroupAction.Approve:
                RunAction(c => c.ApproveGroupRequest(target, subject), ReloadAdmin);
                break;
            case GroupAction.Deny:
                RunAction(c => c.DenyGroupRequest(target, subject), ReloadAdmin);
                break;
            case GroupAction.Kick:
                RunAction(c => c.KickGroupMember(target, subject), ReloadAdmin);
                break;
            case GroupAction.Ban:
                if (Arm("ban." + subject))
                    RunAction(c => c.BanGroupMember(target, subject), ReloadAdmin);
                break;
            case GroupAction.Unban:
                RunAction(c => c.UnbanGroupMember(target, subject), ReloadAdmin);
                break;
            case GroupAction.MakeOwner:
                RunAction(c => c.TransferGroup(target, subject), ReloadAdmin);
                break;
            case GroupAction.Withdraw:
                RunAction(c => c.WithdrawGroupInvite(target, subject), ReloadAdmin);
                break;
            case GroupAction.Invite:
                InviteTyped(target);
                break;
            case GroupAction.Save:
                SaveEdits(target);
                break;
            case GroupAction.CycleVisibility:
                CycleVisibility();
                break;
            case GroupAction.PostAnnouncement:
                PostAnnouncement(target);
                break;
            case GroupAction.DeleteAnnouncement:
                RunAction(c => c.DeleteGroupAnnouncement(target, subject), () => LoadGroup(target));
                break;
            case GroupAction.CreateEvent:
                CreateEvent(target);
                break;
            case GroupAction.DeleteEvent:
                RunAction(c => c.DeleteGroupEvent(target, subject), () => LoadGroup(target));
                break;
            case GroupAction.HostEventWorld:
                HostEventWorld(subject);
                break;
            case GroupAction.JoinEventWorld:
                JoinEventWorld(subject);
                break;
            case GroupAction.DeleteGroup:
                if (Arm("delete"))
                    RunAction(c => c.DeleteGroup(target), BackToList);
                break;
            case GroupAction.Create:
                ConfirmCreate();
                break;
            case GroupAction.CancelCreate:
                CancelCreate();
                break;
        }
    }

    // THE TWO WORLD CONTROLS ON AN EVENT ROW
    //
    // Both hand off to the Worlds screen rather than doing the work here, so there is exactly one create
    // form and exactly one join path in the dash. This page decides WHICH group and WHICH session; what
    // happens next is the Worlds screen's, unchanged.
    private void HostEventWorld(string eventId)
    {
        var group = _group;
        if (group == null || string.IsNullOrEmpty(group.Id) || !CanPost(group))
            return;

        GroupEventInfo? found = null;
        for (int i = 0; i < _events.Count; i++)
        {
            if (string.Equals(_events[i].Id, eventId, StringComparison.Ordinal))
            {
                found = _events[i];
                break;
            }
        }
        if (found == null)
            return;

        var dash = _dashboard;
        if (dash == null)
            return;

        foreach (var screen in dash.Screens)
        {
            if (screen is not WorldsScreen worlds)
                continue;
            dash.SwitchTo(worlds);
            // Members only, because an event announced inside a group is for that group until whoever is
            // running it says otherwise on the form in front of them. -xlinka
            worlds.OpenCreateForGroup(new GroupHosting(group.Id, group.Tag, MembersOnly: true),
                group.Name, found.Title, WorldMode.Event);
            return;
        }
    }

    private void JoinEventWorld(string eventId)
    {
        if (!_liveEvents.TryGetValue(eventId, out var entry) || entry == null)
            return;

        var dash = _dashboard;
        if (dash == null)
            return;

        foreach (var screen in dash.Screens)
        {
            if (screen is WorldsScreen worlds)
            {
                worlds.JoinListedSession(entry);
                return;
            }
        }
    }

    private void ReloadAdmin()
    {
        if (_groupId.Length == 0)
            return;
        LoadGroup(_groupId);
        LoadAdmin(_groupId);
    }

    internal void SetMemberRole(string userId, string role)
    {
        if (_groupId.Length == 0 || userId.Length == 0)
            return;
        string groupId = _groupId;
        RunAction(c => c.SetGroupRole(groupId, userId, role), ReloadAdmin);
    }

    // Join, request and accept-an-invite are all the same call; the answer says which happened, so the
    // page reloads the right thing instead of guessing off the group's visibility. -xlinka
    private async void JoinGroup(string groupId)
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated || groupId.Length == 0)
            return;

        var world = World;
        try
        {
            var result = await client.JoinGroup(groupId);
            OnUi(world, () =>
            {
                if (result.Failed)
                {
                    SetError(result.Message);
                    Rebuild();
                    return;
                }
                if (_view == View.Group)
                    LoadGroup(groupId);
                else
                    LoadList();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () => { SetError(message); Rebuild(); });
        }
    }

    private void InviteTyped(string groupId)
    {
        string userId = _inviteId.Trim();
        if (userId.Length == 0)
            return;
        RunAction(c => c.InviteToGroup(groupId, userId), () =>
        {
            _inviteId = string.Empty;
            ReloadAdmin();
        });
    }

    // Only the fields that actually moved go in the patch. Re-posting the whole group would stamp over an
    // edit another admin made to a field nobody here touched.
    private void SaveEdits(string groupId)
    {
        var group = _group;
        if (group == null)
            return;

        var patch = new GroupPatch
        {
            Name = _editName.Trim() != group.Name ? _editName.Trim() : null,
            Tag = _editTag.Trim().ToUpperInvariant() != group.Tag ? _editTag.Trim().ToUpperInvariant() : null,
            Description = _editBio != group.Description ? _editBio : null,
            Color = _editColor.Trim() != group.Color ? _editColor.Trim() : null,
            Visibility = _editVisibility != group.Visibility ? _editVisibility : null,
        };

        if (patch.IsEmpty)
        {
            _adminError = GroupsLocale.NothingToSave.Resolve();
            Rebuild();
            return;
        }

        RunAction(async c =>
        {
            var result = await c.UpdateGroup(groupId, patch);
            return result.Success ? ApiResponse.Ok() : ApiResponse.Fail(result.Status, result.Message);
        }, () =>
        {
            _editLoaded = false;
            ReloadAdmin();
        });
    }

    private void PostAnnouncement(string groupId)
    {
        string text = _announcement.Trim();
        if (text.Length == 0)
        {
            _groupError = GroupsLocale.AnnouncementEmpty.Resolve();
            Rebuild();
            return;
        }
        RunAction(async c =>
        {
            var result = await c.PostGroupAnnouncement(groupId, text);
            return result.Success ? ApiResponse.Ok() : ApiResponse.Fail(result.Status, result.Message);
        }, () =>
        {
            _announcement = string.Empty;
            LoadGroup(groupId);
        });
    }

    private void CreateEvent(string groupId)
    {
        string title = _eventTitle.Trim();
        if (title.Length == 0)
        {
            _groupError = GroupsLocale.EventNoTitle.Resolve();
            Rebuild();
            return;
        }
        if (!TryReadStart(out var starts))
        {
            _groupError = GroupsLocale.EventBadDate.Resolve();
            Rebuild();
            return;
        }

        string world = _eventWorld.Trim();
        RunAction(async c =>
        {
            var result = await c.CreateGroupEvent(groupId, title, string.Empty, world, starts);
            return result.Success ? ApiResponse.Ok() : ApiResponse.Fail(result.Status, result.Message);
        }, () =>
        {
            _eventTitle = string.Empty;
            _eventWorld = string.Empty;
            _eventDate = string.Empty;
            _eventTime = string.Empty;
            LoadGroup(groupId);
        });
    }

    // Typed as local wall time, sent as UTC: the service stores an instant and every reader turns it back
    // into their own clock.
    private bool TryReadStart(out DateTime starts)
    {
        starts = default;
        string text = (_eventDate.Trim() + " " + _eventTime.Trim()).Trim();
        if (text.Length == 0)
            return false;
        if (!DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var local))
        {
            return false;
        }
        starts = local.ToUniversalTime();
        return true;
    }

    // Run a cloud call, then reload on success or put the service's own message on the page. Stays off the
    // update thread until the answer is back, then marshals the reload onto it. -xlinka
    private async void RunAction(Func<LumoraClient, Task<ApiResponse>> action, Action onSuccess)
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return;

        var world = World;
        try
        {
            var result = await action(client);
            OnUi(world, () =>
            {
                if (result.Success)
                {
                    ClearError();
                    onSuccess();
                    return;
                }
                SetError(result.Message);
                Rebuild();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () => { SetError(message); Rebuild(); });
        }
    }

    private void SetError(string? message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? GroupsLocale.ActionFailed.Resolve() : message!;
        if (_view == View.Admin)
            _adminError = text;
        else if (_view == View.Group)
            _groupError = text;
        else
            _createError = text;
    }

    private void ClearError()
    {
        _adminError = null;
        _groupError = null;
        _createError = null;
    }

    // STORAGE SLICES

    private long PendingSliceFor(string userId)
    {
        if (_pendingSlice.TryGetValue(userId, out long pending))
            return pending;
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].UserId == userId)
                return _members[i].AllocatedBytes;
        }
        return 0L;
    }

    // Drag only moves the number under the handle. The call waits for the release, so dragging across the
    // whole track is one request rather than one per sample.
    internal void PreviewStorage(string userId, float bytes)
    {
        if (userId.Length == 0)
            return;
        _pendingSlice[userId] = SnapSlice(bytes);
        _listing?.RefreshValues();
    }

    internal void CommitStorage(string userId, float bytes)
    {
        if (userId.Length == 0 || _groupId.Length == 0)
            return;
        long snapped = SnapSlice(bytes);
        _pendingSlice[userId] = snapped;

        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].UserId == userId && _members[i].AllocatedBytes == snapped)
                return;   // the handle came back to where it started
        }

        string groupId = _groupId;
        RunAction(c => c.SetGroupMemberStorage(groupId, userId, snapped), () =>
        {
            _pendingSlice.Remove(userId);
            ReloadAdmin();
        });
    }

    private static long SnapSlice(float bytes)
    {
        if (bytes <= 0f)
            return 0L;
        double steps = System.Math.Round(bytes / SliceStep);
        return (long)steps * SliceStep;
    }

    // CREATE FORM

    private void OpenCreate()
    {
        _createActive = true;
        _createName = string.Empty;
        _createError = null;
        _focus = GroupField.CreateName;
        Rebuild();
    }

    private void CancelCreate()
    {
        _createActive = false;
        _createName = string.Empty;
        _createError = null;
        if (_focus == GroupField.CreateName)
            _focus = GroupField.None;
        Rebuild();
    }

    private async void ConfirmCreate()
    {
        string name = _createName.Trim();
        if (name.Length == 0)
        {
            _createError = GroupsLocale.NameEmpty.Resolve();
            Rebuild();
            return;
        }

        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return;

        var world = World;
        string visibility = _createVisibility;
        try
        {
            var result = await client.CreateGroup(name, string.Empty, visibility);
            OnUi(world, () =>
            {
                if (result.Success && result.Data != null)
                {
                    _createActive = false;
                    _createName = string.Empty;
                    _createError = null;
                    _focus = GroupField.None;
                    OpenGroup(result.Data.Id);
                    return;
                }
                // The service refuses creation with a message saying why (no allowance, name taken), and
                // that is more use on the form than "that did not go through".
                _createError = string.IsNullOrWhiteSpace(result.Message)
                    ? GroupsLocale.ActionFailed.Resolve()
                    : result.Message;
                Rebuild();
            });
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            OnUi(world, () => { _createError = message; Rebuild(); });
        }
    }

    private void CycleVisibility()
    {
        if (_view == View.Admin)
            _editVisibility = NextVisibility(_editVisibility);
        else
            _createVisibility = NextVisibility(_createVisibility);
        Rebuild();
    }

    private void SeedEditBuffers()
    {
        var group = _group;
        if (group == null)
            return;
        _editName = group.Name;
        _editTag = group.Tag;
        _editBio = group.Description;
        _editColor = group.Color;
        _editVisibility = group.Visibility;
        _editLoaded = true;
    }

    // KEY INPUT

    internal void FocusField(GroupField field)
    {
        _focus = _focus == field ? GroupField.None : field;
        if (field == GroupField.Search || _focus == GroupField.None)
            BuildHeader();
        _listing?.RefreshValues();
        MarkDirty();
    }

    private void WriteFocused(Func<string, string> edit)
    {
        switch (_focus)
        {
            case GroupField.Search:
                _search = edit(_search);
                BuildHeader();
                PushSearch();
                break;
            case GroupField.CreateName: _createName = edit(_createName); break;
            case GroupField.EditName: _editName = edit(_editName); break;
            case GroupField.EditTag: _editTag = edit(_editTag); break;
            case GroupField.EditBio: _editBio = edit(_editBio); break;
            case GroupField.EditColor: _editColor = edit(_editColor); break;
            case GroupField.InviteId: _inviteId = edit(_inviteId); break;
            case GroupField.Announcement: _announcement = edit(_announcement); break;
            case GroupField.EventTitle: _eventTitle = edit(_eventTitle); break;
            case GroupField.EventWorld: _eventWorld = edit(_eventWorld); break;
            case GroupField.EventDate: _eventDate = edit(_eventDate); break;
            case GroupField.EventTime: _eventTime = edit(_eventTime); break;
            default: return;
        }
        _listing?.RefreshValues();
        MarkDirty();
    }

    private int LimitFor(GroupField field) => field switch
    {
        GroupField.EditTag => 6,
        GroupField.EditColor => 7,
        GroupField.EditBio => 1000,
        GroupField.Announcement => 500,
        GroupField.EventTitle => 80,
        GroupField.EventWorld => 80,
        GroupField.EventDate => 10,
        GroupField.EventTime => 5,
        _ => 64,
    };

    public bool ConsumeChar(char c)
    {
        if (_focus == GroupField.None)
            return false;
        if (char.IsControl(c))
            return true;
        int limit = LimitFor(_focus);
        WriteFocused(text => text.Length >= limit ? text : text + c);
        return true;
    }

    public bool ConsumeBackspace()
    {
        if (_focus == GroupField.None)
            return false;
        WriteFocused(text => text.Length > 0 ? text.Substring(0, text.Length - 1) : text);
        return true;
    }

    public bool ConsumeEnter()
    {
        switch (_focus)
        {
            case GroupField.None:
                return false;
            case GroupField.Search:
                LoadList();
                return true;
            case GroupField.CreateName:
                ConfirmCreate();
                return true;
            case GroupField.InviteId:
                Dispatch(GroupAction.Invite, string.Empty, string.Empty);
                return true;
            case GroupField.Announcement:
                Dispatch(GroupAction.PostAnnouncement, string.Empty, string.Empty);
                return true;
            case GroupField.EventTitle:
            case GroupField.EventWorld:
            case GroupField.EventDate:
            case GroupField.EventTime:
                Dispatch(GroupAction.CreateEvent, string.Empty, string.Empty);
                return true;
            default:
                Dispatch(GroupAction.Save, string.Empty, string.Empty);
                return true;
        }
    }

    public bool ConsumeEscape()
    {
        if (_focus == GroupField.None)
        {
            if (!_createActive)
                return false;
            CancelCreate();
            return true;
        }
        _focus = GroupField.None;
        BuildHeader();
        _listing?.RefreshValues();
        MarkDirty();
        return true;
    }

    // HELPERS

    private static void OnUi(World? world, Action action) => world?.RunInUpdates(0, action);

    // First press arms, a second press inside the window goes through. No modal: a confirm dialog over a
    // scrolled list loses the row it belongs to. -xlinka
    private bool Arm(string key)
    {
        var now = DateTime.UtcNow;
        if (_armed == key && (now - _armedAt).TotalSeconds <= ConfirmWindow)
        {
            _armed = string.Empty;
            return true;
        }
        _armed = key;
        _armedAt = now;
        Rebuild();
        return false;
    }

    private bool IsArmed(string key)
        => _armed == key && (DateTime.UtcNow - _armedAt).TotalSeconds <= ConfirmWindow;

    private IEnumerable<GroupMemberInfo> SortedMembers()
    {
        // The service already sends Owner first, then by rank, then by join date. Sorting again here would
        // only be a second opinion about an order that is already right.
        for (int i = 0; i < _members.Count; i++)
            yield return _members[i];
    }

    private static string DisplayName(string username, string userId)
        => string.IsNullOrEmpty(username) ? ShortId(userId) : username;

    // A raw account id is 40 characters of nothing anybody reads. Where the service had no name to send,
    // the tail is at least short enough to compare against.
    private static string ShortId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return "?";
        return userId.Length <= 10 ? userId : userId.Substring(0, 10);
    }

    private static bool CanPost(GroupInfo group) => RankOf(group.MyRole) <= RankOf("Builder");

    private static int RankOf(string? role)
    {
        if (string.IsNullOrEmpty(role))
            return int.MaxValue;
        for (int i = 0; i < RoleLadder.Length; i++)
        {
            if (string.Equals(RoleLadder[i], role, StringComparison.Ordinal))
                return i;
        }
        return int.MaxValue;
    }

    // What this caller may hand out: everything strictly below their own rung. The service enforces the
    // same rule and refuses anything else, so this is only about not offering a pill that cannot fire.
    private static string[] AssignableRoles(string? myRole)
    {
        int rank = RankOf(myRole);
        var list = new List<string>(RoleLadder.Length);
        for (int i = 0; i < RoleLadder.Length; i++)
        {
            if (i > rank)
                list.Add(RoleLadder[i]);
        }
        return list.ToArray();
    }

    private static string VisibilityLabel(string visibility) => visibility switch
    {
        "Public" => GroupsLocale.VisibilityOpen.Resolve(),
        "Private" => GroupsLocale.VisibilityRequest.Resolve(),
        _ => GroupsLocale.VisibilityInvite.Resolve(),
    };

    private static string NextVisibility(string current) => current switch
    {
        "Public" => "Private",
        "Private" => "Hidden",
        _ => "Public",
    };

    private static color ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return DashTheme.Accent;
        var span = hex.AsSpan();
        if (span.Length > 0 && span[0] == '#')
            span = span.Slice(1);
        if (span.Length < 6)
            return DashTheme.Accent;
        if (!int.TryParse(span.Slice(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r)
            || !int.TryParse(span.Slice(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g)
            || !int.TryParse(span.Slice(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
        {
            return DashTheme.Accent;
        }
        // Written in sRGB the way a colour picker hands it over; the dash palette is linear, so decode once.
        return new color(r / 255f, g / 255f, b / 255f, 1f).ToLinear();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb < 1024.0)
            return mb.ToString("0.#") + " MB";
        return (mb / 1024.0).ToString("0.##") + " GB";
    }

    private static string ShortDate(DateTime? when)
        => when.HasValue ? when.Value.ToLocalTime().ToString("yyyy-MM-dd") : "soon";

    private static string LongDate(DateTime when)
        => when.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
