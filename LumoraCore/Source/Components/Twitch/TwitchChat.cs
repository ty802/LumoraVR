// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumora.Core.Twitch;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Twitch;

// One channel's live chat, read as a guest. Every peer opens its own connection: chat is public, needs
// no account, and relaying it through the session would only add a hop and a host that can drop it.
// The channel name is the only thing that replicates. Everything the relay says (messages, subs,
// raids, gifts, announcements, timeouts, deletions, room mode) arrives on the world thread as a typed
// event so other components can build on it without touching the socket. -xlinka
[ComponentCategory("Media/Twitch")]
public sealed class TwitchChat : Component
{
    public readonly Sync<string> Channel;

    // Messages kept for a panel that opens after the stream has been running a while.
    public readonly Sync<int> HistoryLength;

    private TwitchChatClient? _client;
    private string? _connectedChannel;
    private readonly List<TwitchChatMessage> _history = new List<TwitchChatMessage>();
    private readonly List<TwitchChatEvent> _events = new List<TwitchChatEvent>();

    public IReadOnlyList<TwitchChatMessage> History => _history;
    public IReadOnlyList<TwitchChatEvent> RecentEvents => _events;

    // Name to image URL for the channel's third-party emotes (global set plus the channel's own),
    // filled once the relay has told us the room id. Empty until then.
    public IReadOnlyDictionary<string, string> ThirdPartyEmotes => _thirdPartyEmotes;
    private IReadOnlyDictionary<string, string> _thirdPartyEmotes = new Dictionary<string, string>();
    private string? _emotesRoomId;
    public event Action<TwitchChat>? EmotesChanged;

    // "set/version" to image URL for the channel: its own art where it has some, the stock art
    // elsewhere. Empty until the fetch lands, and the stock table covers that gap.
    private IReadOnlyDictionary<string, string> _badges = new Dictionary<string, string>();
    public event Action<TwitchChat>? BadgesChanged;

    public string? BadgeUrl(string name, string version)
    {
        if (_badges.Count > 0 && _badges.TryGetValue(name + "/" + version, out string? url))
            return url;
        return TwitchBadgeArt.BadgeUrl(name, version);
    }

    public bool IsConnected => _client?.IsConnected ?? false;
    public string Status { get; private set; } = "Idle";
    public string ConnectedChannel => _connectedChannel ?? string.Empty;
    public TwitchRoomState RoomState { get; private set; }

    public event Action<TwitchChat, TwitchChatMessage>? MessageReceived;
    public event Action<TwitchChat, TwitchChatEvent>? EventReceived;
    public event Action<TwitchChat, string>? MessageDeleted;
    public event Action<TwitchChat, TwitchChatClear>? ChatCleared;
    public event Action<TwitchChat, TwitchRoomState>? RoomStateChanged;
    public event Action<TwitchChat>? StatusChanged;
    // The history was thrown away: a channel switch or a disable.
    public event Action<TwitchChat>? HistoryReset;

    public TwitchChat()
    {
        Channel = new Sync<string>(this, string.Empty);
        HistoryLength = new Sync<int>(this, 200);
    }

    public override void OnStart()
    {
        base.OnStart();
        Reconcile();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Reconcile();
    }

    public override void OnDestroy()
    {
        Teardown();
        base.OnDestroy();
    }

    // Login name from whatever was typed or pasted: "#name", "Name", or a channel URL.
    public static string NormalizeChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string text = value.Trim();
        if (text.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            string fromUrl = ChannelFromUrl(uri);
            if (!string.IsNullOrEmpty(fromUrl))
                return fromUrl;
        }
        if (text.StartsWith("#", StringComparison.Ordinal))
            text = text.Substring(1);
        int slash = text.IndexOf('/');
        if (slash >= 0)
            text = text.Substring(0, slash);
        return text.Trim().ToLowerInvariant();
    }

    // A channel page is the only twitch.tv URL with a live chat behind it. Recordings, clips, categories
    // and the site's own pages have none, so they return nothing rather than a bogus login.
    public static string ChannelFromUrl(Uri? uri)
    {
        if (uri == null || !uri.IsAbsoluteUri)
            return string.Empty;
        string host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);
        if (host.StartsWith("m.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(2);
        if (!host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;
        string first = parts[0].ToLowerInvariant();
        switch (first)
        {
            case "videos":
            case "directory":
            case "downloads":
            case "settings":
            case "p":
            case "search":
            case "subscriptions":
            case "inventory":
            case "wallet":
            case "drops":
            case "friends":
            case "moderator":
            case "popout":
            case "embed":
            case "collections":
            case "clip":
                return string.Empty;
        }
        if (parts.Length >= 2 && parts[1].Equals("clip", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        foreach (char c in first)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return string.Empty;
        }
        return first;
    }

    private void Reconcile()
    {
        string channel = NormalizeChannel(Channel.Value);
        bool wanted = Enabled.Value && channel.Length > 0;
        if (!wanted)
        {
            Teardown();
            return;
        }

        if (_client == null)
        {
            var client = new TwitchChatClient();
            client.MessageReceived += OnClientMessage;
            client.EventReceived += OnClientEvent;
            client.MessageDeleted += OnClientDeleted;
            client.ChatCleared += OnClientCleared;
            client.RoomStateChanged += OnClientRoomState;
            client.Connected += OnClientConnected;
            client.Disconnected += OnClientDisconnected;
            client.Notice += OnClientNotice;
            client.Log += OnClientLog;
            _client = client;
        }

        if (!string.Equals(_connectedChannel, channel, StringComparison.Ordinal))
        {
            _connectedChannel = channel;
            _history.Clear();
            _events.Clear();
            RoomState = default;
            _emotesRoomId = null;
            _thirdPartyEmotes = new Dictionary<string, string>();
            _badges = new Dictionary<string, string>();
            LoadBadges(channel);
            SetStatus("Connecting");
            HistoryReset?.Invoke(this);
            try { _client.Connect(channel); }
            catch (Exception ex)
            {
                SetStatus("Failed: " + ex.Message);
            }
        }
    }

    private void Teardown()
    {
        var client = _client;
        _client = null;
        bool hadChannel = _connectedChannel != null;
        _connectedChannel = null;
        if (client != null)
        {
            client.MessageReceived -= OnClientMessage;
            client.EventReceived -= OnClientEvent;
            client.MessageDeleted -= OnClientDeleted;
            client.ChatCleared -= OnClientCleared;
            client.RoomStateChanged -= OnClientRoomState;
            client.Connected -= OnClientConnected;
            client.Disconnected -= OnClientDisconnected;
            client.Notice -= OnClientNotice;
            client.Log -= OnClientLog;
            try { client.Dispose(); } catch (Exception) { }
        }
        if (hadChannel)
        {
            _history.Clear();
            _events.Clear();
            SetStatus("Idle");
            if (!IsDestroyed)
                HistoryReset?.Invoke(this);
        }
    }

    private void SetStatus(string status)
    {
        if (string.Equals(Status, status, StringComparison.Ordinal))
            return;
        Status = status;
        if (status.StartsWith("Failed", StringComparison.Ordinal))
            LumoraLogger.Warn($"TwitchChat '{_connectedChannel}': {status}");
        else
            LumoraLogger.Debug($"TwitchChat '{_connectedChannel}': {status}");
        try { StatusChanged?.Invoke(this); } catch (Exception) { }
    }

    // Every relay callback lands on the reader's thread; the world is the only place a component may
    // touch its state or raise an event, so each one is re-posted there and dropped if the component
    // went away in the meantime.
    private void Post(Action action)
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (IsDestroyed) return;
            try { action(); }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"TwitchChat: handler failed: {ex.Message}");
            }
        });
    }

    private void OnClientMessage(TwitchChatMessage message) => Post(() =>
    {
        _history.Add(message);
        int limit = System.Math.Max(1, HistoryLength.Value);
        if (_history.Count > limit)
            _history.RemoveRange(0, _history.Count - limit);
        MessageReceived?.Invoke(this, message);
    });

    private void OnClientEvent(TwitchChatEvent chatEvent) => Post(() =>
    {
        _events.Add(chatEvent);
        if (_events.Count > 64)
            _events.RemoveRange(0, _events.Count - 64);
        EventReceived?.Invoke(this, chatEvent);
    });

    private void OnClientDeleted(string messageId) => Post(() =>
    {
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_history[i].Id, messageId, StringComparison.Ordinal))
            {
                _history.RemoveAt(i);
                break;
            }
        }
        MessageDeleted?.Invoke(this, messageId);
    });

    private void OnClientCleared(TwitchChatClear clear) => Post(() =>
    {
        if (clear.TargetUserId == null)
        {
            _history.Clear();
        }
        else
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_history[i].UserId, clear.TargetUserId, StringComparison.Ordinal))
                    _history.RemoveAt(i);
            }
        }
        ChatCleared?.Invoke(this, clear);
    });

    private void OnClientRoomState(TwitchRoomState state) => Post(() =>
    {
        RoomState = state;
        RoomStateChanged?.Invoke(this, state);
        if (!string.IsNullOrEmpty(state.RoomId) && !string.Equals(state.RoomId, _emotesRoomId, StringComparison.Ordinal))
        {
            _emotesRoomId = state.RoomId;
            LoadThirdPartyEmotes(state.RoomId);
        }
    });

    private void LoadBadges(string channel)
    {
        _ = Task.Run(async () =>
        {
            var badges = await TwitchBadges.GetAsync(channel).ConfigureAwait(false);
            Post(() =>
            {
                if (!string.Equals(_connectedChannel, channel, StringComparison.Ordinal))
                    return;
                _badges = badges;
                LumoraLogger.Debug($"TwitchChat '{channel}': {badges.Count} badge pictures");
                BadgesChanged?.Invoke(this);
            });
        });
    }

    private void LoadThirdPartyEmotes(string roomId)
    {
        _ = Task.Run(async () =>
        {
            var global = await SevenTvEmotes.GetGlobalAsync().ConfigureAwait(false);
            var channel = await SevenTvEmotes.GetChannelAsync(roomId).ConfigureAwait(false);
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in global) merged[pair.Key] = pair.Value;
            // The channel's own set wins a name clash, which is how the site resolves it too.
            foreach (var pair in channel) merged[pair.Key] = pair.Value;
            Post(() =>
            {
                if (!string.Equals(_emotesRoomId, roomId, StringComparison.Ordinal))
                    return;
                _thirdPartyEmotes = merged;
                LumoraLogger.Debug($"TwitchChat '{_connectedChannel}': {global.Count} global + {channel.Count} channel third-party emotes");
                EmotesChanged?.Invoke(this);
            });
        });
    }

    private void OnClientConnected() => Post(() => SetStatus("Live"));

    private void OnClientDisconnected(string reason) => Post(() => SetStatus(string.IsNullOrEmpty(reason) ? "Reconnecting" : "Reconnecting: " + reason));

    private void OnClientNotice(string notice) => Post(() =>
    {
        LumoraLogger.Log($"TwitchChat '{_connectedChannel}': {notice}");
        if (notice.Contains("suspended", StringComparison.OrdinalIgnoreCase) || notice.Contains("not found", StringComparison.OrdinalIgnoreCase))
            SetStatus(notice);
    });

    private void OnClientLog(string line) => LumoraLogger.Debug($"TwitchChat '{_connectedChannel}': {line}");
}
