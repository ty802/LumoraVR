// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Nexus.Cloud.Cdn;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// The host's copy of the group's member list and ban list, for a world hosted for a group.
//
// It exists because the door and the roles need an answer that no client can author. The joining peer
// knows perfectly well which groups it is in and its word for that is worth nothing here; the only
// membership this world honours is the one the HOST read off the service with the host's own token. So
// this component fetches, and pushes what it fetched into the permission gate, and everything downstream
// asks the gate.
//
// Attached by the host at create time when the world is hosted for a group, and it is deliberately not
// replicated in any useful sense: it carries no sync members, so a guest that ends up with the component
// gets an object that fetches nothing (the routes are member-only and it is not the host's token) and
// pushes nothing (every path is authority-gated). Nothing here is a secret, but nothing here is a client's
// to decide either.
//
// FAIL-CLOSED, in order: before the first fetch lands the roster is empty, so a members-only door refuses
// everyone and nobody is a group moderator. A fetch that fails leaves the last good roster exactly where
// it was rather than blanking a room full of people because one request timed out. -xlinka
[ComponentCategory("Hidden")]
public sealed class GroupHostRoster : Component
{
    // The service allows 30 calls a minute per address. Two calls every five minutes is nothing, and a
    // group's membership does not move fast enough to want more. -xlinka
    public const float RefreshSeconds = 300f;

    private readonly Dictionary<string, string> _members = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bans = new(StringComparer.Ordinal);

    private float _countdown;
    private bool _fetching;

    public IReadOnlyDictionary<string, string> Members => _members;

    public IReadOnlyCollection<string> Bans => _bans;

    // False until a fetch has actually come back. The door does not read this - an empty roster already
    // refuses - but the Permissions tab can say "still reading the roster" instead of "nobody is a member".
    public bool HasRoster { get; private set; }

    public DateTime LastFetchUtc { get; private set; }

    public string GroupId => World?.Configuration?.HostGroupId.Value ?? string.Empty;

    public override void OnStart()
    {
        base.OnStart();
        if (World == null || !World.IsAuthority)
            return;

        // Push the empty roster before anything else so the gate starts from a roster this component owns
        // rather than from whatever a previously hosted world in this process left behind.
        Push();
        _countdown = RefreshSeconds;
        Fetch();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (World == null || !World.IsAuthority)
            return;

        _countdown -= World.Time.Delta;
        if (_countdown > 0f)
            return;
        _countdown = RefreshSeconds;
        Fetch();
    }

    // Ask now rather than waiting out the interval. Used by the create path so the first roster lands as
    // close to the world starting as the round trip allows.
    public void RequestRefresh()
    {
        if (World == null || !World.IsAuthority)
            return;
        _countdown = RefreshSeconds;
        Fetch();
    }

    private void Fetch()
    {
        if (_fetching || IsDestroyed)
            return;

        string groupId = GroupId;
        var client = Engine.Current?.CDNClient;
        if (string.IsNullOrEmpty(groupId) || client == null || !client.IsAuthenticated)
            return;

        _fetching = true;
        StartTask(async () =>
        {
            List<GroupMemberInfo>? members = null;
            List<GroupBanInfo>? bans = null;
            string? failure = null;

            try
            {
                await WorldContext.ToBackground();
                var membersTask = client.GetGroupMembers(groupId);
                var bansTask = client.GetGroupBans(groupId);
                var memberResult = await membersTask;
                var banResult = await bansTask;

                if (memberResult is { Success: true, Data: not null })
                    members = memberResult.Data;
                else
                    failure = memberResult?.Message;

                // Bans are Admin-only. A host who runs an event for a group they only build in gets a 403
                // here and that is not a failure of the fetch: the members list is what the door needs, and
                // a ban list we are not entitled to read stays empty rather than blanking the members.
                if (banResult is { Success: true, Data: not null })
                    bans = banResult.Data;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            await WorldContext.ToWorld();
            _fetching = false;
            if (IsDestroyed || World == null || !World.IsAuthority)
                return;

            if (members == null)
            {
                // The previous roster stands. Say so once per failure rather than silently carrying on:
                // a host whose token expired mid-event needs to know why nobody new can get in. -xlinka
                LumoraLogger.Warn($"GroupHostRoster: could not read group '{groupId}' " +
                    $"({failure ?? "no answer"}); keeping the roster we already had.");
                return;
            }

            _members.Clear();
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member != null && !string.IsNullOrEmpty(member.UserId))
                    _members[member.UserId] = member.Role ?? string.Empty;
            }

            if (bans != null)
            {
                _bans.Clear();
                for (int i = 0; i < bans.Count; i++)
                {
                    var ban = bans[i];
                    if (ban != null && !string.IsNullOrEmpty(ban.UserId))
                        _bans.Add(ban.UserId);
                }
            }

            HasRoster = true;
            LastFetchUtc = DateTime.UtcNow;
            Push();
        });
    }

    private void Push()
    {
        var permissions = World?.DataModelPermissions;
        if (permissions == null || World?.IsAuthority != true)
            return;
        permissions.SetGroupRoster(_members, _bans);
    }
}
