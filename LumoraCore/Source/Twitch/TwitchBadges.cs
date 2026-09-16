// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lumora.Core.Twitch;

// Badge art for a channel: the global set plus the channel's own (custom subscriber tiers, bits
// tiers, gifter ranks), as a map from "set/version" to image URL. The public site endpoint answers
// this with a client id and no account, the same id the video resolver's downloader already sends
// for stream lookups. Cached per channel for the process; a failure caches as empty so the stock
// badge table takes over instead of the request repeating. -xlinka
public static class TwitchBadges
{
    private const string Endpoint = "https://gql.twitch.tv/gql";
    private const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string Query = "query($login:String!){badges{setID version imageURL(size:DOUBLE)}user(login:$login){broadcastBadges{setID version imageURL(size:DOUBLE)}}}";

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, Task<IReadOnlyDictionary<string, string>>> Channels = new Dictionary<string, Task<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
    private static HttpClient? _http;

    private static HttpClient Http
    {
        get
        {
            lock (Sync)
            {
                if (_http == null)
                {
                    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                    _http.DefaultRequestHeaders.Add("Client-Id", ClientId);
                    _http.DefaultRequestHeaders.UserAgent.ParseAdd("LumoraVR/1.0");
                }
                return _http;
            }
        }
    }

    public static Task<IReadOnlyDictionary<string, string>> GetAsync(string channelLogin)
    {
        if (string.IsNullOrEmpty(channelLogin))
            return Task.FromResult(Empty);
        lock (Sync)
        {
            if (!Channels.TryGetValue(channelLogin, out var task))
            {
                task = FetchAsync(channelLogin);
                Channels[channelLogin] = task;
            }
            return task;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(string channelLogin)
    {
        try
        {
            string body = JsonSerializer.Serialize(new { query = Query, variables = new { login = channelLogin } });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Endpoint, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Empty;
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data))
                return Empty;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (data.TryGetProperty("badges", out var global))
                Read(global, map);
            // The channel's art replaces the stock art for the same set and version.
            if (data.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("broadcastBadges", out var channel))
                Read(channel, map);
            return map;
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    private static void Read(JsonElement badges, Dictionary<string, string> map)
    {
        if (badges.ValueKind != JsonValueKind.Array)
            return;
        foreach (var badge in badges.EnumerateArray())
        {
            string? set = badge.TryGetProperty("setID", out var s) ? s.GetString() : null;
            string? version = badge.TryGetProperty("version", out var v) ? v.GetString() : null;
            string? url = badge.TryGetProperty("imageURL", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(set) || version == null || string.IsNullOrEmpty(url))
                continue;
            map[set + "/" + version] = url;
        }
    }
}
