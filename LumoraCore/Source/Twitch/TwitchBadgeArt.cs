// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Twitch;

// Where the pictures live. Emotes come off the public CDN by id, in the static form so an animated
// one still decodes as a picture. Badges are trickier: the per-channel art needs an authenticated
// API, so only the global badges are known here, by the ids they have carried for years, and a
// channel's own subscriber art falls back to the stock star. -xlinka
public static class TwitchBadgeArt
{
    private const string BadgeBase = "https://static-cdn.jtvnw.net/badges/v1/";
    private const string EmoteBase = "https://static-cdn.jtvnw.net/emoticons/v2/";

    private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["broadcaster/1"] = "5527c58c-fb7d-422d-b71b-f309dcb85cc1",
        ["moderator/1"] = "3267646d-33f0-4b17-b3df-f923a41db1d0",
        ["vip/1"] = "b817aba4-fad8-49e2-b88a-7cc744dfa6ec",
        ["subscriber"] = "5d9f2208-5dd8-11e7-8513-2ff4adfae661",
        ["founder"] = "511b78a9-ab37-472f-9569-457753bbe7d3",
        ["premium/1"] = "bbbe0db0-a598-423e-86d0-f9fb98ca1933",
        ["turbo/1"] = "bd444ec6-8f34-4bf9-91f4-af1e3428d80f",
        ["partner/1"] = "d12a2e27-16f6-41d0-ab77-b780518f00a3",
        ["staff/1"] = "d97c37bd-a6f5-4c38-8f57-4e4bef88af34",
        ["admin/1"] = "9ef7e029-4cdf-4d4d-a0d5-e2b3fb2583fe",
        ["global_mod/1"] = "9384c43e-4ce7-4e94-b2a1-b93656896eba",
        ["bits/1"] = "73b5c3fb-24f9-4a82-a852-2f475b59411c",
        ["bits/100"] = "09d93036-e7ce-431c-9a9e-7f89e2d0f44d",
        ["bits/1000"] = "0d85a29e-79ad-4c63-a285-3acd2c66f2ba",
        ["bits/5000"] = "57cd97fc-3e9e-4c6d-9d41-60147137234e",
        ["bits/10000"] = "68af213b-a771-4124-b6e3-9bb6d05c7c65",
    };

    public static string? BadgeUrl(string name, string version)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (Known.TryGetValue(name + "/" + version, out string? id) || Known.TryGetValue(name, out id))
            return BadgeBase + id + "/2";
        return null;
    }

    // The default form is a still for a still emote and an animated file for an animated one.
    public static string EmoteUrl(string id) => EmoteBase + id + "/default/dark/2.0";
}
