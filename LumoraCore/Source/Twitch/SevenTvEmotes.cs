// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lumora.Core.Twitch;

// Third-party emotes from the public 7TV API: the global set plus a channel's active set, as a map
// from emote name to image URL. No account involved. Fetches are cached for the process, a failed
// fetch caches as empty so a dead endpoint is asked once, not once per message. -xlinka
public static class SevenTvEmotes
{
    private const string GlobalSetUrl = "https://7tv.io/v3/emote-sets/global";
    private const string UserUrl = "https://7tv.io/v3/users/twitch/";

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, Task<IReadOnlyDictionary<string, string>>> Channels = new Dictionary<string, Task<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
    private static Task<IReadOnlyDictionary<string, string>>? _global;
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
                    _http.DefaultRequestHeaders.UserAgent.ParseAdd("LumoraVR/1.0");
                }
                return _http;
            }
        }
    }

    public static Task<IReadOnlyDictionary<string, string>> GetGlobalAsync()
    {
        lock (Sync)
        {
            _global ??= FetchAsync(GlobalSetUrl, false);
            return _global;
        }
    }

    public static Task<IReadOnlyDictionary<string, string>> GetChannelAsync(string twitchUserId)
    {
        if (string.IsNullOrEmpty(twitchUserId))
            return Task.FromResult(Empty);
        lock (Sync)
        {
            if (!Channels.TryGetValue(twitchUserId, out var task))
            {
                task = FetchAsync(UserUrl + twitchUserId, true);
                Channels[twitchUserId] = task;
            }
            return task;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> FetchAsync(string url, bool userDocument)
    {
        try
        {
            string json = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement emotes;
            if (userDocument)
            {
                if (!root.TryGetProperty("emote_set", out var set) || set.ValueKind != JsonValueKind.Object
                    || !set.TryGetProperty("emotes", out emotes))
                    return Empty;
            }
            else if (!root.TryGetProperty("emotes", out emotes))
            {
                return Empty;
            }
            return Parse(emotes);
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> Parse(JsonElement emotes)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (emotes.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var emote in emotes.EnumerateArray())
        {
            string? name = emote.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name))
                continue;
            if (!emote.TryGetProperty("data", out var data) || !data.TryGetProperty("host", out var host))
                continue;
            string? baseUrl = host.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(baseUrl) || !host.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                continue;

            // 2x is the sharpest size worth downloading for a chat row; WebP is what the image loader
            // reads, so it is the only format taken.
            string? pick = null;
            string? fallback = null;
            foreach (var file in files.EnumerateArray())
            {
                string? fileName = file.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fileName.StartsWith("2x", StringComparison.OrdinalIgnoreCase)) { pick = fileName; break; }
                if (fileName.StartsWith("1x", StringComparison.OrdinalIgnoreCase)) fallback = fileName;
                fallback ??= fileName;
            }
            pick ??= fallback;
            if (pick == null)
                continue;

            string prefix = baseUrl.StartsWith("//", StringComparison.Ordinal) ? "https:" : string.Empty;
            map[name] = prefix + baseUrl + "/" + pick;
        }
        return map;
    }
}
