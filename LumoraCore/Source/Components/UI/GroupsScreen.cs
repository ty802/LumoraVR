// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.CDN;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard "Groups" screen. A two-view cloud page: a list of groups (yours, or a public browse) and a
/// detail page for one group with its info, members, pending join requests and the membership/role actions.
/// Everything talks to the account service through <see cref="Engine.CDNClient"/>; results are marshalled back
/// onto the world update thread before touching the UI tree. The whole screen rebuilds from in-memory state so
/// there's no per-widget diffing to keep straight. -xlinka
/// </summary>
public sealed class GroupsScreen : WidgetScreen, IDashboardKeyInput
{
    private static readonly color JoinFill = new color(0.28f, 0.60f, 0.40f, 0.95f);
    private static readonly color LeaveFill = new color(0.70f, 0.45f, 0.24f, 0.95f);
    private static readonly color DangerFill = new color(0.70f, 0.24f, 0.28f, 0.95f);
    private static readonly color OpenFill = new color(0.34f, 0.42f, 0.74f, 0.95f);
    private static readonly color PublicTag = new color(0.26f, 0.58f, 0.40f, 0.95f);
    private static readonly color PrivateTag = new color(0.52f, 0.40f, 0.74f, 0.95f);
    private static readonly color HiddenTag = new color(0.42f, 0.42f, 0.50f, 0.95f);
    private static readonly color ErrorText = new color(0.95f, 0.55f, 0.55f, 1f);

    private enum View { List, Detail }
    private enum LoadState { Idle, Loading, Loaded, Error }

    private View _view = View.List;
    private string _listTab = "Mine"; // "Mine" | "Browse"

    // List state.
    private LoadState _listLoad = LoadState.Idle;
    private List<GroupInfo> _groups = new();
    private string? _listError;

    // Detail state.
    private string? _detailId;
    private LoadState _detailLoad = LoadState.Idle;
    private GroupInfo? _group;
    private List<GroupMemberInfo> _members = new();
    private List<GroupJoinRequestInfo> _requests = new();
    private readonly Dictionary<string, string> _names = new(); // userId -> resolved username
    private string? _detailError;

    // Inline "create group" form.
    private bool _createActive;
    private string _createName = string.Empty;
    private string _createVisibility = "Public";
    private string? _createError;
    private Text? _createNameText;

    private Slot? _root;

    protected override float RowHeight => 36f;

    private static LumoraClient? Client => Lumora.Core.Engine.Current?.CDNClient;
    private static string? MyId => Client?.AccountUserId;

    protected override void OnShow()
    {
        base.OnShow();
        if (_view == View.List) LoadList();
        else if (_detailId != null) LoadDetail(_detailId);
    }

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        _root = builder.Current;
        LoadList();
    }

    // ---------------------------------------------------------------- render

    private void Rebuild()
    {
        var root = _root;
        if (root == null)
            return;
        root.DestroyChildren();

        var col = root.GetComponent<VerticalLayout>() ?? root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 6f;
        col.PaddingLeft.Value = 16f;
        col.PaddingRight.Value = 16f;
        col.PaddingTop.Value = 16f;
        col.PaddingBottom.Value = 16f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        if (_view == View.Detail)
            RenderDetail(root);
        else
            RenderList(root);

        MarkDirty();
    }

    private void RenderList(Slot root)
    {
        var header = BeginRow(root, "Header");
        var hb = RowBuilder(header);
        hb.MinWidth(120f).FlexibleWidth(1f);
        AddRowLabel(hb, "Groups", 18f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(header, "Mine", _listTab == "Mine" ? AccentColor : TabFill, 64f, () => SwitchTab("Mine"));
        AddInlineButton(header, "Browse", _listTab == "Browse" ? AccentColor : TabFill, 76f, () => SwitchTab("Browse"));
        AddInlineButton(header, "New", JoinFill, 60f, OpenCreate);
        AddInlineButton(header, "Refresh", TabFill, 84f, LoadList);

        if (_createActive)
            RenderCreateForm(root);

        switch (_listLoad)
        {
            case LoadState.Loading:
                AddInfoRow(root, "Loading groups…");
                return;
            case LoadState.Error:
                AddInfoRow(root, _listError ?? "Couldn't load groups.", ErrorText);
                return;
        }

        if (_groups.Count == 0)
        {
            AddInfoRow(root, _listTab == "Mine"
                ? "You're not in any groups yet. Browse to find some, or make your own with New."
                : "No groups to show.");
            return;
        }

        foreach (var group in _groups)
            GroupRow(root, group);
    }

    private void GroupRow(Slot root, GroupInfo group)
    {
        var id = group.Id;
        var row = BeginRow(root, "Group");
        var b = RowBuilder(row);
        b.MinWidth(150f).FlexibleWidth(1f);
        AddRowLabel(b, group.Name, 16f, TextPrimary, TextHorizontalAlignment.Left);

        AddPill(row, Members(group.MemberCount), TabFill, 78f);
        AddPill(row, group.Visibility, VisibilityColor(group.Visibility), 72f);

        if (string.IsNullOrEmpty(group.MyRole))
            AddInlineButton(row, group.Visibility == "Public" ? "Join" : "Request", JoinFill, 84f, () => JoinFromList(id));
        else
            AddPill(row, group.MyRole!, RoleColor(group.MyRole!), 84f);

        AddInlineButton(row, "Open", OpenFill, 72f, () => OpenDetail(id));
    }

    private void RenderCreateForm(Slot root)
    {
        var row = BeginRow(root, "CreateForm");

        // Name "field": a flexible cell whose label shows the typed text (updated live in ConsumeChar). -xlinka
        var field = row.AddSlot("NameField");
        field.AttachComponent<RectTransform>();
        var el = field.AttachComponent<LayoutElement>();
        el.MinWidth.Value = 150f;
        el.FlexibleWidth.Value = 1f;
        el.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(field, ControlFill, RowBorder);
        _createNameText = AddFillLabel(field, _createName.Length == 0 ? "Group name…" : _createName, 14f,
            _createName.Length == 0 ? TextDim : TextPrimary);
        _createNameText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        AddInlineButton(row, _createVisibility, VisibilityColor(_createVisibility), 92f, () =>
        {
            _createVisibility = NextVisibility(_createVisibility);
            Rebuild();
        });
        AddInlineButton(row, "Create", JoinFill, 84f, ConfirmCreate);
        AddInlineButton(row, "Cancel", TabFill, 80f, CancelCreate);

        if (!string.IsNullOrEmpty(_createError))
            AddInfoRow(root, _createError!, ErrorText);
    }

    private void RenderDetail(Slot root)
    {
        var back = BeginRow(root, "Back");
        var bb = RowBuilder(back);
        bb.MinWidth(120f).FlexibleWidth(1f);
        AddRowLabel(bb, _group?.Name ?? "Group", 18f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(back, "← Back", TabFill, 88f, BackToList);
        AddInlineButton(back, "Refresh", TabFill, 84f, () => { if (_detailId != null) LoadDetail(_detailId); });

        switch (_detailLoad)
        {
            case LoadState.Loading:
                AddInfoRow(root, "Loading group…");
                return;
            case LoadState.Error:
                AddInfoRow(root, _detailError ?? "Couldn't load this group.", ErrorText);
                return;
        }

        var group = _group;
        if (group == null)
        {
            AddInfoRow(root, "Group not found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(group.Description))
            AddInfoRow(root, group.Description);

        // Facts pills.
        var facts = BeginRow(root, "Facts");
        var fb = RowBuilder(facts);
        fb.MinWidth(40f).FlexibleWidth(1f);
        AddRowLabel(fb, group.MyRole != null ? $"You: {group.MyRole}" : "Not a member", 13f, TextDim, TextHorizontalAlignment.Left);
        AddPill(facts, group.Visibility, VisibilityColor(group.Visibility), 72f);
        AddPill(facts, Members(group.MemberCount), TabFill, 84f);
        AddPill(facts, $"{FmtBytes(group.UsedStorageBytes)}/{FmtBytes(group.StorageQuotaBytes)}", TabFill, 150f);
        if (!string.IsNullOrEmpty(group.StorageStatus))
            AddPill(facts, group.StorageStatus!, StorageStatusColor(group.StorageStatus!), 86f);

        // Spell out what a lapsed-owner storage state means.
        if (group.StorageStatus == "Grace")
            AddInfoRow(root, $"The owner's support has lapsed. Group storage stays usable until {ShortDate(group.StorageLockAt)}, then it locks.", new color(0.95f, 0.78f, 0.35f, 1f));
        else if (group.StorageStatus == "Locked")
            AddInfoRow(root, "Group storage is locked: the owner's support lapsed, so no new uploads. Existing content stays and the lock lifts if the owner resubscribes.", ErrorText);

        // Membership actions.
        var actions = BeginRow(root, "Actions");
        var ab = RowBuilder(actions);
        ab.MinWidth(40f).FlexibleWidth(1f);
        AddRowLabel(ab, "", 13f, TextDim, TextHorizontalAlignment.Left);
        if (string.IsNullOrEmpty(group.MyRole))
        {
            AddInlineButton(actions, group.Visibility == "Public" ? "Join" : "Request to Join", JoinFill, 150f,
                () => RunAction(c => c.JoinGroup(group.Id), () => LoadDetail(group.Id)));
        }
        else if (!IsOwner(group.MyRole))
        {
            AddInlineButton(actions, "Leave", LeaveFill, 100f,
                () => RunAction(c => c.LeaveGroup(group.Id), () => LoadDetail(group.Id)));
        }
        if (IsOwner(group.MyRole))
        {
            AddInlineButton(actions, "Delete Group", DangerFill, 130f,
                () => RunAction(c => c.DeleteGroup(group.Id), BackToList));
        }

        if (!string.IsNullOrEmpty(_detailError))
            AddInfoRow(root, _detailError!, ErrorText);

        // Pending join requests (moderators+ only; empty otherwise).
        if (CanManage(group.MyRole) && _requests.Count > 0)
        {
            AddSectionRow(root, $"Pending requests ({_requests.Count})");
            foreach (var req in _requests)
                RequestRow(root, group.Id, req);
        }

        // Members.
        AddSectionRow(root, $"Members ({_members.Count})");
        if (_members.Count == 0)
            AddInfoRow(root, "No members listed.");
        foreach (var member in _members)
            MemberRow(root, group, member);
    }

    private void RequestRow(Slot root, string groupId, GroupJoinRequestInfo req)
    {
        var uid = req.UserId;
        var row = BeginRow(root, "Request");
        var b = RowBuilder(row);
        b.MinWidth(150f).FlexibleWidth(1f);
        AddRowLabel(b, DisplayName(uid), 15f, TextPrimary, TextHorizontalAlignment.Left);
        AddInlineButton(row, "Approve", JoinFill, 92f,
            () => RunAction(c => c.ApproveGroupRequest(groupId, uid), () => LoadDetail(groupId)));
        AddInlineButton(row, "Deny", DangerFill, 80f,
            () => RunAction(c => c.DenyGroupRequest(groupId, uid), () => LoadDetail(groupId)));
    }

    private void MemberRow(Slot root, GroupInfo group, GroupMemberInfo member)
    {
        var uid = member.UserId;
        var row = BeginRow(root, "Member");
        var b = RowBuilder(row);
        b.MinWidth(150f).FlexibleWidth(1f);
        var self = uid == MyId;
        AddRowLabel(b, self ? $"{DisplayName(uid)} (you)" : DisplayName(uid), 15f, TextPrimary, TextHorizontalAlignment.Left);
        AddPill(row, member.Role, RoleColor(member.Role), 86f);

        // The owner can't be re-roled or kicked, and you can't act on yourself here.
        bool manageable = CanManage(group.MyRole) && !self && member.Role != "Owner";
        if (manageable)
        {
            AddInlineButton(row, "Role →", OpenFill, 76f,
                () => RunAction(c => c.SetGroupRole(group.Id, uid, NextRole(member.Role)), () => LoadDetail(group.Id)));
            AddInlineButton(row, "Kick", DangerFill, 70f,
                () => RunAction(c => c.KickGroupMember(group.Id, uid), () => LoadDetail(group.Id)));
        }
        if (IsOwner(group.MyRole) && !self)
        {
            AddInlineButton(row, "Make Owner", LeaveFill, 112f,
                () => RunAction(c => c.TransferGroup(group.Id, uid), () => LoadDetail(group.Id)));
        }
    }

    // ---------------------------------------------------------------- navigation

    private void SwitchTab(string tab)
    {
        if (_listTab == tab)
            return;
        _listTab = tab;
        LoadList();
    }

    private void OpenDetail(string groupId)
    {
        _view = View.Detail;
        _detailId = groupId;
        LoadDetail(groupId);
    }

    private void BackToList()
    {
        _view = View.List;
        _detailError = null;
        LoadList();
    }

    // ---------------------------------------------------------------- loads

    private async void LoadList()
    {
        _view = View.List;
        _listLoad = LoadState.Loading;
        _listError = null;
        Rebuild();

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _listLoad = LoadState.Error;
            _listError = "Sign in to view your groups.";
            Rebuild();
            return;
        }

        var world = World;
        var browse = _listTab == "Browse";
        try
        {
            var result = browse ? await client.GetGroups(null, 0, 50) : await client.GetMyGroups();
            OnUi(world, () =>
            {
                if (result.Success && result.Data != null)
                {
                    _groups = result.Data;
                    _listLoad = LoadState.Loaded;
                }
                else
                {
                    _listLoad = LoadState.Error;
                    _listError = result.Message ?? "Couldn't load groups.";
                }
                if (_view == View.List)
                    Rebuild();
            });
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            OnUi(world, () =>
            {
                _listLoad = LoadState.Error;
                _listError = msg;
                if (_view == View.List)
                    Rebuild();
            });
        }
    }

    private async void LoadDetail(string groupId)
    {
        _view = View.Detail;
        _detailId = groupId;
        _detailLoad = LoadState.Loading;
        _detailError = null;
        Rebuild();

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _detailLoad = LoadState.Error;
            _detailError = "Sign in to view this group.";
            Rebuild();
            return;
        }

        var world = World;
        try
        {
            var groupTask = client.GetGroup(groupId);
            var membersTask = client.GetGroupMembers(groupId);
            var group = await groupTask;
            var members = await membersTask;

            // Requests only matter to managers; fetch them but a 403 for non-managers is fine (we just show none).
            List<GroupJoinRequestInfo> requests = new();
            if (group.Success && group.Data != null && CanManage(group.Data.MyRole))
            {
                var reqResult = await client.GetGroupRequests(groupId);
                if (reqResult.Success && reqResult.Data != null)
                    requests = reqResult.Data;
            }

            OnUi(world, () =>
            {
                if (_detailId != groupId)
                    return; // user navigated away while we were loading
                if (!group.Success || group.Data == null)
                {
                    _detailLoad = LoadState.Error;
                    _detailError = group.Message ?? "Couldn't load this group.";
                    Rebuild();
                    return;
                }
                _group = group.Data;
                _members = members.Success && members.Data != null ? members.Data : new();
                _requests = requests;
                _detailLoad = LoadState.Loaded;
                Rebuild();
                ResolveNames();
            });
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            OnUi(world, () =>
            {
                _detailLoad = LoadState.Error;
                _detailError = msg;
                Rebuild();
            });
        }
    }

    // Resolve usernames for the members + requesters we're showing, best-effort, then rebuild so rows that
    // were showing a raw id pick up the real name. Cached so we don't refetch a name we already have. -xlinka
    private async void ResolveNames()
    {
        var client = Client;
        if (client == null)
            return;

        var ids = _members.Select(m => m.UserId)
            .Concat(_requests.Select(r => r.UserId))
            .Where(id => !string.IsNullOrEmpty(id) && !_names.ContainsKey(id))
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return;

        var world = World;
        var tasks = ids.Select(id => client.GetPublicUser(id)).ToList();
        try { await Task.WhenAll(tasks); } catch { /* partial results are fine */ }

        OnUi(world, () =>
        {
            for (int i = 0; i < ids.Count; i++)
            {
                var t = tasks[i];
                if (t.IsCompletedSuccessfully && t.Result.Success && t.Result.Data != null && !string.IsNullOrEmpty(t.Result.Data.Username))
                    _names[ids[i]] = t.Result.Data.Username;
            }
            if (_view == View.Detail)
                Rebuild();
        });
    }

    // ---------------------------------------------------------------- actions

    private void JoinFromList(string groupId)
    {
        RunAction(c => c.JoinGroup(groupId), LoadList);
    }

    // Run a cloud action, then either reload on success or surface the error. Stays off the UI thread until
    // the result is back, then marshals the reload/error onto the update thread. -xlinka
    private async void RunAction(Func<LumoraClient, Task<ApiResponse>> action, Action onSuccess)
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _detailError = "Sign in first.";
            Rebuild();
            return;
        }

        var world = World;
        try
        {
            var result = await action(client);
            OnUi(world, () =>
            {
                if (result.Success)
                {
                    onSuccess();
                }
                else
                {
                    _detailError = result.Message ?? "That action didn't go through.";
                    Rebuild();
                }
            });
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            OnUi(world, () =>
            {
                _detailError = msg;
                Rebuild();
            });
        }
    }

    // ---------------------------------------------------------------- create form + key input

    private void OpenCreate()
    {
        _createActive = true;
        _createName = string.Empty;
        _createError = null;
        Rebuild();
    }

    private void CancelCreate()
    {
        _createActive = false;
        _createName = string.Empty;
        _createError = null;
        Rebuild();
    }

    private async void ConfirmCreate()
    {
        var name = _createName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _createError = "Name can't be empty.";
            Rebuild();
            return;
        }

        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            _createError = "Sign in first.";
            Rebuild();
            return;
        }

        var world = World;
        var vis = _createVisibility;
        try
        {
            var result = await client.CreateGroup(name, "", vis);
            OnUi(world, () =>
            {
                if (result.Success && result.Data != null)
                {
                    _createActive = false;
                    _createName = string.Empty;
                    _createError = null;
                    _listTab = "Mine";
                    OpenDetail(result.Data.Id);
                }
                else
                {
                    _createError = result.Message ?? "Couldn't create the group.";
                    Rebuild();
                }
            });
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            OnUi(world, () =>
            {
                _createError = msg;
                Rebuild();
            });
        }
    }

    private void UpdateCreateNameUI()
    {
        if (_createNameText == null)
            return;
        if (_createName.Length == 0)
        {
            _createNameText.Content.Value = "Group name…";
            _createNameText.Color.Value = TextDim;
        }
        else
        {
            _createNameText.Content.Value = _createName;
            _createNameText.Color.Value = TextPrimary;
        }
        MarkDirty();
    }

    public bool ConsumeChar(char c)
    {
        if (!_createActive) return false;
        if (char.IsControl(c)) return true;
        if (_createName.Length >= 64) return true;
        _createName += c;
        UpdateCreateNameUI();
        return true;
    }

    public bool ConsumeBackspace()
    {
        if (!_createActive) return false;
        if (_createName.Length > 0)
            _createName = _createName.Substring(0, _createName.Length - 1);
        UpdateCreateNameUI();
        return true;
    }

    public bool ConsumeEnter()
    {
        if (!_createActive) return false;
        ConfirmCreate();
        return true;
    }

    public bool ConsumeEscape()
    {
        if (!_createActive) return false;
        CancelCreate();
        return true;
    }

    // ---------------------------------------------------------------- helpers

    // Marshal a UI mutation back onto the world update thread (cloud continuations land on the threadpool).
    private static void OnUi(World? world, Action action)
    {
        if (world != null)
            world.RunInUpdates(0, action);
    }

    private string DisplayName(string userId)
        => _names.TryGetValue(userId, out var name) && !string.IsNullOrEmpty(name) ? name : userId;

    private void AddInfoRow(Slot parent, string text) => AddInfoRow(parent, text, TextDim);

    private void AddInfoRow(Slot parent, string text, color textColor)
    {
        var row = BeginRow(parent, "Info");
        var label = AddFillLabel(row, text, 14f, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void AddSectionRow(Slot parent, string title)
    {
        var row = BeginRow(parent, "Section");
        var label = AddFillLabel(row, title, 15f, SectionTitleColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    // A fixed-width, non-interactive pill cell (for member counts, visibility, role tags).
    private Slot AddPill(Slot row, string text, color fill, float width)
    {
        var cell = row.AddSlot("Pill");
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(cell, fill, RowBorder);
        AddFillLabel(cell, text, 12f, TextPrimary);
        return cell;
    }

    private static bool CanManage(string? myRole) => myRole is "Owner" or "Admin" or "Moderator";
    private static bool IsOwner(string? myRole) => myRole == "Owner";

    private static string Members(int n) => n == 1 ? "1 member" : $"{n} members";

    private static string FmtBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        return mb < 1024.0 ? $"{mb:0.#} MB" : $"{mb / 1024.0:0.##} GB";
    }

    private static readonly string[] AssignableRoles = { "Member", "Moderator", "Builder", "EventHost" };

    private static string NextRole(string current)
    {
        int i = Array.IndexOf(AssignableRoles, current);
        return AssignableRoles[(i + 1) % AssignableRoles.Length]; // unknown/Owner -> Member
    }

    private static string NextVisibility(string current) => current switch
    {
        "Public" => "Private",
        "Private" => "Hidden",
        _ => "Public",
    };

    private static string ShortDate(DateTime? when)
        => when.HasValue ? when.Value.ToLocalTime().ToString("yyyy-MM-dd") : "soon";

    private static color VisibilityColor(string visibility) => visibility switch
    {
        "Public" => PublicTag,
        "Private" => PrivateTag,
        _ => HiddenTag,
    };

    private static color StorageStatusColor(string status) => status switch
    {
        "Active" => PublicTag,
        "Grace" => new color(0.85f, 0.65f, 0.25f, 0.95f),
        _ => DangerFill, // Locked
    };

    private static color RoleColor(string role) => role switch
    {
        "Owner" => new color(0.85f, 0.65f, 0.25f, 0.95f),
        "Admin" => new color(0.80f, 0.40f, 0.40f, 0.95f),
        "Moderator" => new color(0.34f, 0.52f, 0.78f, 0.95f),
        "Builder" => new color(0.28f, 0.60f, 0.55f, 0.95f),
        "EventHost" => new color(0.55f, 0.42f, 0.74f, 0.95f),
        _ => new color(0.42f, 0.42f, 0.50f, 0.95f),
    };
}
