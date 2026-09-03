// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core;

// What a host is hosting a world FOR. It lives exactly as long as the create call: once the world exists
// the answer is WorldSettings.HostGroupId plus the pair of session tags, which is what every other peer,
// the browser and the door actually read. MembersOnly is the difference between GroupMembers and
// GroupPublic, nothing more. -xlinka
public sealed record GroupHosting(string GroupId, string GroupTag, bool MembersOnly);

// The group half of a session announcement, shaped exactly like the mode tag beside it: one prefix per
// fact, a stamp that clears the previous pair before writing, and a read that can answer "no group"
// instead of guessing one. A directory entry carries whatever some host published, so nothing here may
// assume a tag is present, well formed, or cased the way we wrote it. -xlinka
public static class GroupSessionTags
{
    private const string IdPrefix = "group:";
    private const string TagPrefix = "grouptag:";

    public static string IdTag(string groupId) => IdPrefix + groupId;

    public static string ShortTag(string groupTag) => TagPrefix + groupTag;

    // Both tags go on together or neither does. An empty id clears the pair, which is how a world stops
    // being a group world without leaving a stale tag behind for the browser to filter on.
    public static void Stamp(List<string>? tags, string groupId, string groupTag)
    {
        if (tags == null)
            return;

        tags.RemoveAll(t => t != null
            && (t.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)
                || t.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)));

        if (string.IsNullOrWhiteSpace(groupId))
            return;

        tags.Add(IdTag(groupId.Trim()));
        if (!string.IsNullOrWhiteSpace(groupTag))
            tags.Add(ShortTag(groupTag.Trim().ToUpperInvariant()));
    }

    // True only when an id came back. A world can carry an id with no short tag (a group whose tag the
    // host never read), and the callers all cope with that; a short tag with no id is not a group world
    // and is reported as one.
    public static bool TryRead(IEnumerable<string>? tags, out string groupId, out string groupTag)
    {
        groupId = string.Empty;
        groupTag = string.Empty;
        if (tags == null)
            return false;

        foreach (var tag in tags)
        {
            if (tag == null)
                continue;
            if (groupId.Length == 0 && tag.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
                groupId = tag.Substring(IdPrefix.Length);
            else if (groupTag.Length == 0 && tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
                groupTag = tag.Substring(TagPrefix.Length);
        }

        return groupId.Length > 0;
    }

    // The short tag alone, for a chip that only has room for one thing.
    public static string ReadShortTag(IEnumerable<string>? tags)
    {
        TryRead(tags, out _, out var tag);
        return tag;
    }

    // What an OPEN world should call the group it is hosted for.
    //
    // The session tags are the host's own announcement and only the host has them; a peer that joined
    // carries its own join metadata, not the host's. So on a peer the only replicated place the short tag
    // exists is the nametag card of somebody in the room wearing that same group, and the last resort is
    // the id, which at least identifies the door. Never invents a tag. -xlinka
    public static string WorldShortTag(World? world)
    {
        if (world == null || world.IsDestroyed)
            return string.Empty;

        var settings = world.RootSlot?.GetComponent<WorldSettings>();
        string groupId = settings?.HostGroupId.Value ?? string.Empty;
        if (groupId.Length == 0)
            return string.Empty;

        var announced = ReadShortTag(world.Session?.Metadata?.Tags);
        if (announced.Length > 0)
            return announced;

        var users = world.GetAllUsers();
        for (int i = 0; i < users.Count; i++)
        {
            var user = users[i];
            if (user == null || user.IsDestroyed)
                continue;
            if (string.Equals(user.GroupId.Value, groupId, StringComparison.Ordinal)
                && user.GroupTag.Value.Length > 0)
                return user.GroupTag.Value;
        }

        return groupId;
    }
}
