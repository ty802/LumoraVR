// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Core.Twitch;

// A guest in one channel's chat over the relay socket. No account, no sending, no library: the relay
// speaks a tagged line protocol that fits in a few hundred lines, and owning it means the events keep
// flowing when the next wrapper goes stale. Every event fires on the reader's thread. -xlinka
public sealed class TwitchChatClient : IDisposable
{
    private const string Endpoint = "wss://irc-ws.chat.twitch.tv:443";
    private const int ReceiveBufferBytes = 16 * 1024;
    private const int MaxBackoffSeconds = 30;

    private readonly object _sync = new object();
    private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
    private readonly string _nick = "justinfan" + Random.Shared.Next(10000, 99999).ToString(CultureInfo.InvariantCulture);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private string? _channel;
    private string? _joined;
    private bool _open;
    private bool _disposed;
    private int _backoffSeconds = 1;
    private TwitchRoomState _roomState = TwitchRoomState.Default;

    public string? Channel { get { lock (_sync) return _channel; } }

    public bool IsConnected
    {
        get { lock (_sync) return _open && _joined != null && string.Equals(_joined, _channel, StringComparison.Ordinal); }
    }

    public TwitchRoomState RoomState { get { lock (_sync) return _roomState; } }

    public event Action<TwitchChatMessage>? MessageReceived;
    public event Action<TwitchChatEvent>? EventReceived;
    public event Action<string>? MessageDeleted;
    public event Action<TwitchChatClear>? ChatCleared;
    public event Action<TwitchRoomState>? RoomStateChanged;
    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? Notice;
    public event Action<string>? Log;

    public void Connect(string channel)
    {
        string login = NormalizeChannel(channel);
        if (login.Length == 0)
            throw new ArgumentException("A channel login is required.", nameof(channel));

        ClientWebSocket? socket = null;
        string? previous = null;
        bool startLoop = false;
        lock (_sync)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TwitchChatClient));
            if (string.Equals(_channel, login, StringComparison.Ordinal) && _loop != null)
                return;
            previous = _channel;
            _channel = login;
            _joined = null;
            _roomState = TwitchRoomState.Default;
            if (_loop == null)
            {
                _cancel = new CancellationTokenSource();
                var token = _cancel.Token;
                _loop = Task.Run(() => RunAsync(token));
                startLoop = true;
            }
            else if (_open)
            {
                socket = _socket;
            }
        }

        if (!startLoop && socket != null)
        {
            // Already on the relay: leave the old room and enter the new one on the same socket.
            _ = SwitchAsync(socket, previous, login);
        }
    }

    public void Disconnect()
    {
        Task? loop;
        CancellationTokenSource? cancel;
        ClientWebSocket? socket;
        lock (_sync)
        {
            loop = _loop;
            cancel = _cancel;
            socket = _socket;
            _loop = null;
            _cancel = null;
            _socket = null;
            _open = false;
            _joined = null;
            _channel = null;
        }

        try { cancel?.Cancel(); } catch (Exception) { }
        if (socket != null)
        {
            try { socket.Abort(); } catch (Exception) { }
        }
        if (loop != null)
        {
            try { loop.Wait(1000); } catch (Exception) { }
        }
        try { cancel?.Dispose(); } catch (Exception) { }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Disconnect();
        _sendGate.Dispose();
    }

    public static string NormalizeChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string text = value.Trim();
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            text = text.Substring(scheme + 3);
            int slash = text.IndexOf('/');
            text = slash >= 0 ? text.Substring(slash + 1) : string.Empty;
        }
        if (text.StartsWith("#", StringComparison.Ordinal))
            text = text.Substring(1);
        int cut = text.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (cut >= 0)
            text = text.Substring(0, cut);
        return text.ToLowerInvariant();
    }

    // CONNECTION

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? channel;
            lock (_sync) channel = _channel;
            if (channel == null)
            {
                try { await Task.Delay(200, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            var socket = new ClientWebSocket();
            string reason = "closed";
            bool serverReconnect = false;
            try
            {
                RaiseLog("connecting to " + Endpoint + " as " + _nick);
                await socket.ConnectAsync(new Uri(Endpoint), token).ConfigureAwait(false);
                lock (_sync)
                {
                    _socket = socket;
                    _open = true;
                }
                await SendAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands", token).ConfigureAwait(false);
                await SendAsync(socket, "PASS SCHMOOPIIE", token).ConfigureAwait(false);
                await SendAsync(socket, "NICK " + _nick, token).ConfigureAwait(false);
                await SendAsync(socket, "JOIN #" + channel, token).ConfigureAwait(false);
                serverReconnect = await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
                if (serverReconnect) reason = "relay asked for a reconnect";
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_socket, socket)) _socket = null;
                    _open = false;
                    _joined = null;
                }
                try { socket.Dispose(); } catch (Exception) { }
            }

            if (token.IsCancellationRequested)
                break;

            Raise(Disconnected, reason);

            int delay;
            lock (_sync)
            {
                if (serverReconnect) _backoffSeconds = 1;
                delay = _backoffSeconds;
                _backoffSeconds = System.Math.Min(MaxBackoffSeconds, _backoffSeconds * 2);
            }
            RaiseLog("reconnecting in " + delay + " s (" + reason + ")");
            try { await Task.Delay(delay * 1000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Returns true when the relay itself asked us to reconnect, false when the socket closed.
    private async Task<bool> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[ReceiveBufferBytes];
        var frame = new List<byte>(ReceiveBufferBytes);
        var pending = new StringBuilder();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                RaiseLog("receive failed: " + ex.Message);
                return false;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return false;

            frame.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
            if (!result.EndOfMessage)
                continue;

            pending.Append(Encoding.UTF8.GetString(frame.ToArray()));
            frame.Clear();

            string text = pending.ToString();
            int consumed = 0;
            while (true)
            {
                int end = text.IndexOf("\r\n", consumed, StringComparison.Ordinal);
                if (end < 0) break;
                string line = text.Substring(consumed, end - consumed);
                consumed = end + 2;
                if (line.Length == 0) continue;
                bool reconnect;
                try
                {
                    reconnect = await HandleLineAsync(socket, line, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RaiseLog("line failed: " + ex.Message + " :: " + line);
                    reconnect = false;
                }
                if (reconnect)
                    return true;
            }
            pending.Clear();
            if (consumed < text.Length)
                pending.Append(text, consumed, text.Length - consumed);
        }
        return false;
    }

    private async Task SwitchAsync(ClientWebSocket socket, string? previous, string next)
    {
        try
        {
            if (previous != null)
                await SendAsync(socket, "PART #" + previous, CancellationToken.None).ConfigureAwait(false);
            await SendAsync(socket, "JOIN #" + next, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseLog("channel switch failed: " + ex.Message);
        }
    }

    private async Task SendAsync(ClientWebSocket socket, string line, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _sendGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open) return;
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    // LINES

    private async Task<bool> HandleLineAsync(ClientWebSocket socket, string line, CancellationToken token)
    {
        ParseLine(line, out var tags, out string prefix, out string command, out var parameters, out string? trailing);

        switch (command)
        {
            case "PING":
                await SendAsync(socket, "PONG :" + (trailing ?? "tmi.twitch.tv"), token).ConfigureAwait(false);
                return false;

            case "RECONNECT":
                return true;

            case "001":
                RaiseLog("logged in as " + _nick);
                return false;

            case "JOIN":
                if (string.Equals(PrefixLogin(prefix), _nick, StringComparison.OrdinalIgnoreCase))
                    MarkJoined(ChannelParam(parameters, 0));
                return false;

            case "366":
                MarkJoined(ChannelParam(parameters, 1));
                return false;

            case "PRIVMSG":
                Raise(MessageReceived, BuildMessage(tags, prefix, ChannelParam(parameters, 0), trailing ?? string.Empty));
                return false;

            case "USERNOTICE":
                Raise(EventReceived, BuildEvent(tags, ChannelParam(parameters, 0), trailing));
                return false;

            case "CLEARCHAT":
                Raise(ChatCleared, new TwitchChatClear
                {
                    TargetUserId = Tag(tags, "target-user-id"),
                    TargetLogin = trailing,
                    BanDurationSeconds = ParseInt(Tag(tags, "ban-duration"), -1) is int seconds && seconds >= 0 ? seconds : null,
                    SentAt = SentAt(tags),
                });
                return false;

            case "CLEARMSG":
                {
                    string? id = Tag(tags, "target-msg-id");
                    if (!string.IsNullOrEmpty(id))
                        Raise(MessageDeleted, id);
                    return false;
                }

            case "ROOMSTATE":
                {
                    TwitchRoomState state;
                    lock (_sync)
                    {
                        var last = _roomState;
                        state = new TwitchRoomState(
                            ChannelParam(parameters, 0),
                            Tag(tags, "room-id") ?? last.RoomId,
                            TagFlag(tags, "emote-only", last.EmoteOnly),
                            ParseInt(Tag(tags, "followers-only"), last.FollowersOnlyMinutes),
                            ParseInt(Tag(tags, "slow"), last.SlowSeconds),
                            TagFlag(tags, "subs-only", last.SubscribersOnly),
                            TagFlag(tags, "r9k", last.UniqueChat));
                        _roomState = state;
                    }
                    Raise(RoomStateChanged, state);
                    return false;
                }

            case "NOTICE":
                {
                    string text = trailing ?? string.Empty;
                    string? id = Tag(tags, "msg-id");
                    Raise(Notice, string.IsNullOrEmpty(id) ? text : id + ": " + text);
                    return false;
                }

            default:
                return false;
        }
    }

    private void MarkJoined(string channel)
    {
        bool matches;
        lock (_sync)
        {
            // The join echo and the end-of-names reply both land for one join; the second is no news.
            if (string.Equals(_joined, channel, StringComparison.Ordinal))
                return;
            _joined = channel;
            matches = string.Equals(channel, _channel, StringComparison.Ordinal);
            if (matches) _backoffSeconds = 1;
        }
        RaiseLog("joined #" + channel);
        if (matches)
            Raise(Connected);
    }

    // MESSAGES

    private static TwitchChatMessage BuildMessage(Dictionary<string, string> tags, string prefix, string channel, string trailing)
    {
        string login = PrefixLogin(prefix);
        string text = trailing;
        bool action = false;
        if (text.StartsWith("ACTION ", StringComparison.Ordinal))
        {
            action = true;
            text = text.Substring(8);
            if (text.EndsWith("", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
        }

        var badges = ParseBadges(Tag(tags, "badges"));
        bool broadcaster = HasBadge(badges, "broadcaster");
        bool moderator = HasBadge(badges, "moderator") || Tag(tags, "mod") == "1";
        bool vip = HasBadge(badges, "vip") || Tag(tags, "vip") == "1";
        bool subscriber = HasBadge(badges, "subscriber") || HasBadge(badges, "founder") || Tag(tags, "subscriber") == "1";

        string display = Tag(tags, "display-name") ?? string.Empty;
        if (display.Length == 0) display = login;
        string color = Tag(tags, "color") ?? string.Empty;
        if (color.Length == 0) color = TwitchChatColors.ForLogin(login);

        return new TwitchChatMessage
        {
            Id = Tag(tags, "id") ?? string.Empty,
            ChannelLogin = channel,
            UserId = Tag(tags, "user-id") ?? string.Empty,
            Login = login,
            DisplayName = display,
            Color = color,
            Text = text,
            IsAction = action,
            Badges = badges,
            IsBroadcaster = broadcaster,
            IsModerator = moderator,
            IsVip = vip,
            IsSubscriber = subscriber,
            IsFirstMessage = Tag(tags, "first-msg") == "1",
            IsReturningChatter = Tag(tags, "returning-chatter") == "1",
            Bits = ParseInt(Tag(tags, "bits"), 0),
            Emotes = ParseEmotes(Tag(tags, "emotes"), text),
            ReplyParentMessageId = Tag(tags, "reply-parent-msg-id"),
            ReplyParentDisplayName = Tag(tags, "reply-parent-display-name"),
            SentAt = SentAt(tags),
        };
    }

    private static TwitchChatEvent BuildEvent(Dictionary<string, string> tags, string channel, string? trailing)
    {
        string raw = Tag(tags, "msg-id") ?? string.Empty;
        string login = Tag(tags, "login") ?? string.Empty;
        string display = Tag(tags, "display-name") ?? string.Empty;
        if (display.Length == 0) display = login;
        string color = Tag(tags, "color") ?? string.Empty;
        if (color.Length == 0) color = TwitchChatColors.ForLogin(login);

        TwitchChatEventKind kind = raw switch
        {
            "sub" => TwitchChatEventKind.Subscription,
            "resub" => TwitchChatEventKind.Resubscription,
            "subgift" => TwitchChatEventKind.SubGift,
            "anonsubgift" => TwitchChatEventKind.AnonSubGift,
            "submysterygift" => TwitchChatEventKind.MysteryGift,
            "giftpaidupgrade" => TwitchChatEventKind.GiftUpgrade,
            "anongiftpaidupgrade" => TwitchChatEventKind.GiftUpgrade,
            "raid" => TwitchChatEventKind.Raid,
            "unraid" => TwitchChatEventKind.Unraid,
            "announcement" => TwitchChatEventKind.Announcement,
            "bitsbadgetier" => TwitchChatEventKind.BitsBadge,
            "viewermilestone" => TwitchChatEventKind.ViewerMilestone,
            _ => TwitchChatEventKind.Unknown,
        };

        int giftCount = ParseInt(Tag(tags, "msg-param-mass-gift-count"), 0);
        if (giftCount == 0 && (kind == TwitchChatEventKind.SubGift || kind == TwitchChatEventKind.AnonSubGift))
            giftCount = 1;

        return new TwitchChatEvent
        {
            Kind = kind,
            RawKind = raw,
            SystemMessage = Tag(tags, "system-msg") ?? string.Empty,
            UserMessage = string.IsNullOrEmpty(trailing) ? null : trailing,
            Login = login,
            DisplayName = display,
            Color = color,
            SubPlan = Tag(tags, "msg-param-sub-plan"),
            CumulativeMonths = ParseInt(Tag(tags, "msg-param-cumulative-months"), 0),
            StreakMonths = ParseInt(Tag(tags, "msg-param-streak-months"), 0),
            GiftCount = giftCount,
            RecipientDisplayName = Tag(tags, "msg-param-recipient-display-name"),
            RaidViewerCount = ParseInt(Tag(tags, "msg-param-viewerCount"), 0),
            AnnouncementColor = Tag(tags, "msg-param-color"),
            SentAt = SentAt(tags),
            Id = Tag(tags, "id") ?? string.Empty,
        };
    }

    // PARSING

    private static void ParseLine(string line, out Dictionary<string, string> tags, out string prefix, out string command,
        out List<string> parameters, out string? trailing)
    {
        tags = new Dictionary<string, string>(StringComparer.Ordinal);
        prefix = string.Empty;
        command = string.Empty;
        parameters = new List<string>(4);
        trailing = null;

        int position = 0;
        if (line.Length > 0 && line[0] == '@')
        {
            int space = line.IndexOf(' ');
            if (space < 0) space = line.Length;
            ParseTags(line.AsSpan(1, space - 1), tags);
            position = space + 1;
        }

        while (position < line.Length && line[position] == ' ') position++;
        if (position < line.Length && line[position] == ':')
        {
            int space = line.IndexOf(' ', position);
            if (space < 0) space = line.Length;
            prefix = line.Substring(position + 1, space - position - 1);
            position = space + 1;
        }

        while (position < line.Length && line[position] == ' ') position++;
        {
            int space = line.IndexOf(' ', position);
            if (space < 0) space = line.Length;
            if (position < line.Length)
                command = line.Substring(position, space - position);
            position = space + 1;
        }

        while (position < line.Length)
        {
            while (position < line.Length && line[position] == ' ') position++;
            if (position >= line.Length) break;
            if (line[position] == ':')
            {
                trailing = line.Substring(position + 1);
                break;
            }
            int space = line.IndexOf(' ', position);
            if (space < 0) space = line.Length;
            parameters.Add(line.Substring(position, space - position));
            position = space + 1;
        }
    }

    private static void ParseTags(ReadOnlySpan<char> text, Dictionary<string, string> tags)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.Slice(start).IndexOf(';');
            ReadOnlySpan<char> pair = end < 0 ? text.Slice(start) : text.Slice(start, end);
            if (pair.Length > 0)
            {
                int equals = pair.IndexOf('=');
                if (equals < 0)
                    tags[pair.ToString()] = string.Empty;
                else
                    tags[pair.Slice(0, equals).ToString()] = UnescapeTag(pair.Slice(equals + 1));
            }
            if (end < 0) break;
            start += end + 1;
        }
    }

    private static string UnescapeTag(ReadOnlySpan<char> value)
    {
        if (value.IndexOf('\\') < 0)
            return value.ToString();
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i + 1 >= value.Length)
                break;
            char next = value[++i];
            switch (next)
            {
                case 's': sb.Append(' '); break;
                case ':': sb.Append(';'); break;
                case '\\': sb.Append('\\'); break;
                case 'r': sb.Append('\r'); break;
                case 'n': sb.Append('\n'); break;
                default: sb.Append(next); break;
            }
        }
        return sb.ToString();
    }

    private static string PrefixLogin(string prefix)
    {
        if (prefix.Length == 0) return string.Empty;
        int bang = prefix.IndexOf('!');
        if (bang >= 0) return prefix.Substring(0, bang);
        int dot = prefix.IndexOf('.');
        return dot >= 0 ? prefix.Substring(0, dot) : prefix;
    }

    private static string ChannelParam(List<string> parameters, int index)
    {
        if (index >= parameters.Count) return string.Empty;
        string value = parameters[index];
        return value.StartsWith("#", StringComparison.Ordinal) ? value.Substring(1) : value;
    }

    private static string? Tag(Dictionary<string, string> tags, string key)
        => tags.TryGetValue(key, out string? value) ? value : null;

    private static bool TagFlag(Dictionary<string, string> tags, string key, bool fallback)
    {
        string? value = Tag(tags, key);
        if (value == null) return fallback;
        return value == "1";
    }

    private static int ParseInt(string? value, int fallback)
        => value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    private static DateTimeOffset SentAt(Dictionary<string, string> tags)
    {
        string? value = Tag(tags, "tmi-sent-ts");
        if (value != null && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(ms); }
            catch (ArgumentOutOfRangeException) { }
        }
        return DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<TwitchBadge> ParseBadges(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<TwitchBadge>();
        var list = new List<TwitchBadge>(4);
        foreach (string entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int slash = entry.IndexOf('/');
            if (slash < 0) list.Add(new TwitchBadge(entry, string.Empty));
            else list.Add(new TwitchBadge(entry.Substring(0, slash), entry.Substring(slash + 1)));
        }
        return list;
    }

    private static bool HasBadge(IReadOnlyList<TwitchBadge> badges, string name)
    {
        for (int i = 0; i < badges.Count; i++)
        {
            if (string.Equals(badges[i].Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // The relay counts positions in code points; the text is indexed in UTF-16 units, so a message
    // with anything outside the basic plane needs the positions moved across.
    private static IReadOnlyList<TwitchEmote> ParseEmotes(string? value, string text)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<TwitchEmote>();

        int[]? map = null;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i]))
            {
                map = BuildCodePointMap(text);
                break;
            }
        }

        var list = new List<TwitchEmote>(4);
        foreach (string group in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = group.IndexOf(':');
            if (colon < 0) continue;
            string id = group.Substring(0, colon);
            foreach (string range in group.Substring(colon + 1).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int dash = range.IndexOf('-');
                if (dash < 0) continue;
                int start = ParseInt(range.Substring(0, dash), -1);
                int end = ParseInt(range.Substring(dash + 1), -1);
                if (start < 0 || end < start) continue;
                if (map != null)
                {
                    if (start >= map.Length || end >= map.Length) continue;
                    start = map[start];
                    end = map[end];
                }
                list.Add(new TwitchEmote(id, start, end));
            }
        }
        return list;
    }

    private static int[] BuildCodePointMap(string text)
    {
        var map = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            map.Add(i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
        }
        return map.ToArray();
    }

    // EVENTS

    private void Raise<T>(Action<T>? handler, T value)
    {
        if (handler == null) return;
        try { handler(value); }
        catch (Exception ex) { RaiseLog("handler failed: " + ex.Message); }
    }

    private void Raise(Action? handler)
    {
        if (handler == null) return;
        try { handler(); }
        catch (Exception ex) { RaiseLog("handler failed: " + ex.Message); }
    }

    private void RaiseLog(string line)
    {
        var handler = Log;
        if (handler == null) return;
        try { handler(line); } catch (Exception) { }
    }
}
