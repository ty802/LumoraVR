// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Network;

namespace Lumora.Core.Components.UI.Worlds;

// Several people hosting the same place used to be several rows that looked unrelated. They are one
// card now, and the sessions behind it are the strip inside its detail view.
//
// There is NO world id anywhere in the session announcement: a session's Name is just the host's
// World.Name copied at start, so the ONLY thing two hosts of one place have in common is that name.
// The group key is therefore the normalized name (trimmed, whitespace collapsed, case folded) and
// nothing else. Two unrelated worlds that happen to share a name will collapse together; that is the
// honest consequence of the data we have, and inventing an id would not make it less wrong. -xlinka
internal sealed class WorldGroup
{
    public readonly string Key;
    public string DisplayName = string.Empty;
    public readonly List<SessionListEntry> Sessions = new();

    public WorldGroup(string key)
    {
        Key = key;
    }

    public int TotalUsers
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Sessions.Count; i++)
                total += Sessions[i].ActiveUsers;
            return total;
        }
    }

    // The session a group's primary button acts on when the user has not picked one in the strip:
    // the FULLEST one that still has room. Joining people is the point of the button, and an empty
    // session with a full one beside it is almost never the one you meant. Falls back to the first
    // entry when every session is full, so the button is still wired to something real (it will
    // refuse the join for the same reason the card says Full). -xlinka
    public SessionListEntry? Preferred
    {
        get
        {
            SessionListEntry? best = null;
            for (int i = 0; i < Sessions.Count; i++)
            {
                var entry = Sessions[i];
                if (!entry.HasSpace)
                    continue;
                if (best == null || entry.ActiveUsers > best.ActiveUsers)
                    best = entry;
            }
            return best ?? (Sessions.Count > 0 ? Sessions[0] : null);
        }
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "(unnamed)";
        var buffer = new System.Text.StringBuilder(name.Length);
        bool lastWasSpace = false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsWhiteSpace(c))
            {
                if (buffer.Length > 0)
                    lastWasSpace = true;
                continue;
            }
            if (lastWasSpace)
            {
                buffer.Append(' ');
                lastWasSpace = false;
            }
            buffer.Append(char.ToLowerInvariant(c));
        }
        return buffer.Length == 0 ? "(unnamed)" : buffer.ToString();
    }

    // WorldModePermissions.ParseMode answers Builder when there is no tag at all, which would put a
    // Builder chip on every session whose host never announced a mode. Sessions from the directory
    // carry whatever tags the host published and nothing is guaranteed, so ask the question that can
    // come back "don't know" and leave the chip off when it does. -xlinka
    public static bool TryReadMode(IEnumerable<string>? tags, out WorldMode mode)
    {
        mode = WorldMode.Builder;
        if (tags == null)
            return false;
        foreach (var tag in tags)
        {
            if (tag == null || !tag.StartsWith("mode:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Enum.TryParse(tag.Substring(5), ignoreCase: true, out WorldMode parsed))
            {
                mode = parsed;
                return true;
            }
        }
        return false;
    }
}
