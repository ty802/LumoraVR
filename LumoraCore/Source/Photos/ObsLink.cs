// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Core.Photos;

// Remote control for an OBS on this machine or the LAN, over obs-websocket v5 (OBS 28 and later). One
// socket and one receive loop; requests are matched to their answers by id. Streaming and Recording follow
// OBS's own output events, not the answer to a start: OBS accepts a start before the output is up, and the
// output can still fail after that.
//
// Threading: StateChanged fires on whichever thread the socket wakes, usually a thread pool thread, and the
// properties change on that thread too. A handler that touches world state hands the work to the world
// thread itself. A handler that throws is swallowed, so a bad listener cannot kill the link or a caller.
//
// Every public method is safe while disconnected: it does nothing and answers false or empty, and LastError
// keeps the reason the link went down. -xlinka
public sealed class ObsLink : IDisposable, IAsyncDisposable
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    private const string SubProtocol = "obswebsocket.json";
    private const int RpcVersion = 1;
    private const int SubscribeGeneral = 1 << 0;
    private const int SubscribeOutputs = 1 << 6;

    private const int OpHello = 0;
    private const int OpIdentify = 1;
    private const int OpIdentified = 2;
    private const int OpEvent = 5;
    private const int OpRequest = 6;
    private const int OpRequestResponse = 7;

    private const int ChunkBytes = 16 * 1024;
    private const int MaxMessageBytes = 8 * 1024 * 1024;

    // Pings catch an OBS that vanished without closing (crash, sleep, pulled cable) within two intervals.
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _state = new();
    private Session? _session;
    private volatile bool _disposed;
    private TimeSpan _requestTimeout = DefaultRequestTimeout;

    private volatile string? _lastError;
    private volatile bool _streaming;
    private volatile bool _recording;
    private volatile string? _streamState;
    private volatile string? _recordState;
    private volatile string? _recordingPath;
    private volatile string? _currentScene;
    private TimeSpan? _streamDuration;
    private TimeSpan? _recordDuration;

    public event Action? StateChanged;

    public bool IsConnected => Volatile.Read(ref _session) != null;

    public string? LastError => _lastError;

    public bool Streaming => _streaming;

    public bool Recording => _recording;

    // OBS_WEBSOCKET_OUTPUT_STARTING, _STARTED, _STOPPING, _STOPPED, _RECONNECTING and so on, as OBS last said.
    public string? StreamState => _streamState;

    public string? RecordState => _recordState;

    // Where OBS wrote the last finished recording. Kept across disconnects, the file is still there.
    public string? LastRecordingPath => _recordingPath;

    // Only as fresh as the last GetScenesAsync or SetSceneAsync: scene events are not subscribed.
    public string? CurrentScene => _currentScene;

    // Zero from the moment the output starts, then whatever the last RefreshStatusAsync read.
    public TimeSpan? StreamDuration
    {
        get { lock (_state) return _streamDuration; }
    }

    public TimeSpan? RecordDuration
    {
        get { lock (_state) return _recordDuration; }
    }

    // Covers the whole handshake on connect, and each request from send to answer.
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        set => _requestTimeout = value > TimeSpan.Zero ? value : DefaultRequestTimeout;
    }

    public async Task<bool> ConnectAsync(string host, int port, string? password, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return Refuse("This OBS link has been disposed");
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return Refuse($"Not an OBS address: {host}:{port}");

        try
        {
            await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Refuse("Connecting to OBS was cancelled");
        }

        try
        {
            if (Volatile.Read(ref _session) is { } old)
                await CloseAsync(old).ConfigureAwait(false);
            if (_disposed)
                return Refuse("This OBS link has been disposed");
            return await OpenAsync(host.Trim(), port, password, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _session) is { } session)
                await CloseAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task<bool> StartStreamAsync() => CommandAsync("StartStream");

    public Task<bool> StopStreamAsync() => CommandAsync("StopStream");

    public Task<bool> ToggleStreamAsync() => CommandAsync("ToggleStream");

    public Task<bool> StartRecordAsync() => CommandAsync("StartRecord");

    public async Task<bool> StopRecordAsync()
    {
        var reply = await RequestAsync("StopRecord").ConfigureAwait(false);
        if (!reply.Ok)
            return Fail(reply);
        if (Str(reply.Data, "outputPath") is { Length: > 0 } path)
            _recordingPath = path;
        return true;
    }

    public Task<bool> ToggleRecordAsync() => CommandAsync("ToggleRecord");

    public async Task RefreshStatusAsync()
    {
        if (await RefreshCoreAsync().ConfigureAwait(false))
            RaiseStateChanged();
    }

    // In the order OBS's scene dock shows them, top first.
    public async Task<IReadOnlyList<string>> GetScenesAsync()
    {
        var reply = await RequestAsync("GetSceneList").ConfigureAwait(false);
        if (!reply.Ok)
        {
            Fail(reply);
            return Array.Empty<string>();
        }

        var scenes = new List<(double Index, string Name)>();
        var indexed = true;
        if (reply.Data.ValueKind == JsonValueKind.Object
            && reply.Data.TryGetProperty("scenes", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var scene in list.EnumerateArray())
            {
                if (Str(scene, "sceneName") is not { } name)
                    continue;
                var index = Number(scene, "sceneIndex");
                indexed &= index.HasValue;
                scenes.Add((index ?? 0, name));
            }
        }

        // The array comes bottom of the dock first, with sceneIndex counting up from the bottom.
        if (indexed)
            scenes.Sort((a, b) => b.Index.CompareTo(a.Index));

        var names = new string[scenes.Count];
        for (var i = 0; i < names.Length; i++)
            names[i] = scenes[i].Name;

        if (Str(reply.Data, "currentProgramSceneName") is { } current && SetCurrentScene(current))
            RaiseStateChanged();
        return names;
    }

    public async Task<bool> SetSceneAsync(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        var reply = await RequestAsync("SetCurrentProgramScene", w => w.WriteString("sceneName", name)).ConfigureAwait(false);
        if (!reply.Ok)
            return Fail(reply);
        if (SetCurrentScene(name))
            RaiseStateChanged();
        return true;
    }

    // Drops the socket without a close frame; DisposeAsync says goodbye first.
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Volatile.Read(ref _session) is { } session)
        {
            session.Closing = true;
            End(session, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    private async Task<bool> OpenAsync(string host, int port, string? password, CancellationToken cancellationToken)
    {
        var where = $"{host}:{port}";
        Uri address;
        try
        {
            address = new UriBuilder("ws", host, port).Uri;
        }
        catch (UriFormatException)
        {
            return Refuse($"Not an OBS address: {where}");
        }

        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(SubProtocol);
        socket.Options.KeepAliveInterval = KeepAlive;
        socket.Options.KeepAliveTimeout = KeepAlive;
        var chunk = new byte[ChunkBytes];
        var assembly = new MemoryStream();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            await socket.ConnectAsync(address, deadline.Token).ConfigureAwait(false);

            var hello = await ReceiveMessageAsync(socket, assembly, chunk, deadline.Token).ConfigureAwait(false);
            if (hello is not { } helloBytes)
                return Abandon(socket, DescribeClose(socket));

            string? challenge = null;
            string? salt = null;
            using (var doc = JsonDocument.Parse(helloBytes))
            {
                if (OpOf(doc.RootElement) != OpHello || !TryData(doc.RootElement, out var data))
                    return Abandon(socket, $"{where} answered, but not with an obs-websocket v5 hello");
                if (data.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
                {
                    challenge = Str(auth, "challenge");
                    salt = Str(auth, "salt");
                    if (challenge == null || salt == null)
                        return Abandon(socket, "OBS asked for authentication without a challenge and salt");
                }
            }

            if (challenge != null && string.IsNullOrEmpty(password))
                return Abandon(socket, "OBS needs a password: authentication is on in its WebSocket Server Settings");

            var identify = Encode(OpIdentify, w =>
            {
                w.WriteNumber("rpcVersion", RpcVersion);
                if (challenge != null)
                    w.WriteString("authentication", AuthResponse(password!, salt!, challenge));
                w.WriteNumber("eventSubscriptions", SubscribeGeneral | SubscribeOutputs);
            });
            await socket.SendAsync(identify, WebSocketMessageType.Text, true, deadline.Token).ConfigureAwait(false);

            var identified = await ReceiveMessageAsync(socket, assembly, chunk, deadline.Token).ConfigureAwait(false);
            if (identified is not { } identifiedBytes)
                return Abandon(socket, DescribeClose(socket));
            using (var doc = JsonDocument.Parse(identifiedBytes))
            {
                if (OpOf(doc.RootElement) != OpIdentified)
                    return Abandon(socket, "OBS did not confirm the identify");
            }
        }
        catch (OperationCanceledException)
        {
            return Abandon(socket, cancellationToken.IsCancellationRequested
                ? "Connecting to OBS was cancelled"
                : $"OBS at {where} did not answer within {RequestTimeout.TotalSeconds:0.#} s");
        }
        catch (WebSocketException e)
        {
            return Abandon(socket, $"Could not reach OBS at {where}: {Innermost(e)} Is the WebSocket server on in OBS (Tools, WebSocket Server Settings)?");
        }
        catch (JsonException)
        {
            return Abandon(socket, $"OBS at {where} sent something that is not JSON");
        }
        catch (Exception e)
        {
            return Abandon(socket, $"Could not connect to OBS at {where}: {Innermost(e)}");
        }

        var session = new Session(socket, chunk, assembly);
        lock (_state)
        {
            if (_disposed)
            {
                socket.Dispose();
                _lastError = "This OBS link has been disposed";
                return false;
            }
            ClearOutputs();
            _lastError = null;
            Volatile.Write(ref _session, session);
        }
        session.Receiving = Task.Run(() => ReceiveLoopAsync(session));

        // OBS may already be live when we join, so the first state is read, not assumed.
        await RefreshCoreAsync().ConfigureAwait(false);
        RaiseStateChanged();
        return ReferenceEquals(Volatile.Read(ref _session), session);
    }

    private async Task ReceiveLoopAsync(Session session)
    {
        string? reason;
        try
        {
            while (true)
            {
                var message = await ReceiveMessageAsync(session.Socket, session.Assembly, session.Chunk, session.Stop.Token).ConfigureAwait(false);
                if (message is not { } bytes)
                {
                    reason = DescribeClose(session.Socket);
                    if (!session.Closing)
                        await AnswerCloseAsync(session.Socket).ConfigureAwait(false);
                    break;
                }
                Dispatch(session, bytes);
            }
        }
        catch (Exception e)
        {
            reason = "Lost the connection to OBS: " + Innermost(e);
        }

        if (session.ObsExiting)
            reason = "OBS was closed";
        End(session, session.Closing ? null : reason);
    }

    private void Dispatch(Session session, ReadOnlyMemory<byte> bytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            if (!TryData(doc.RootElement, out var data))
                return;
            switch (OpOf(doc.RootElement))
            {
                case OpRequestResponse:
                    Answer(session, data);
                    break;
                case OpEvent:
                    Apply(session, data);
                    break;
            }
        }
    }

    private static void Answer(Session session, JsonElement data)
    {
        // A late answer to a request that already timed out has nobody waiting and is dropped here.
        if (Str(data, "requestId") is not { } id || !session.Pending.TryRemove(id, out var waiter))
            return;

        var ok = false;
        var code = 0;
        string? comment = null;
        if (data.TryGetProperty("requestStatus", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            ok = Bool(status, "result") ?? false;
            code = (int)(Number(status, "code") ?? 0);
            comment = Str(status, "comment");
        }

        var result = data.TryGetProperty("responseData", out var response) && response.ValueKind == JsonValueKind.Object
            ? response.Clone()
            : default;
        var requestType = Str(data, "requestType") ?? "the request";
        var error = ok ? null : $"OBS refused {requestType}: {comment ?? CodeText(code)} (code {code})";
        waiter.TrySetResult(new Reply(ok, code, error, result));
    }

    private void Apply(Session session, JsonElement data)
    {
        var payload = data.TryGetProperty("eventData", out var found) ? found : default;
        switch (Str(data, "eventType"))
        {
            case "StreamStateChanged":
            {
                var active = Bool(payload, "outputActive") ?? false;
                lock (_state)
                {
                    if (!ReferenceEquals(_session, session))
                        return;
                    _streaming = active;
                    _streamState = Str(payload, "outputState");
                    _streamDuration = active ? _streamDuration ?? TimeSpan.Zero : null;
                }
                RaiseStateChanged();
                break;
            }
            case "RecordStateChanged":
            {
                var active = Bool(payload, "outputActive") ?? false;
                lock (_state)
                {
                    if (!ReferenceEquals(_session, session))
                        return;
                    _recording = active;
                    _recordState = Str(payload, "outputState");
                    _recordDuration = active ? _recordDuration ?? TimeSpan.Zero : null;
                    if (Str(payload, "outputPath") is { Length: > 0 } path)
                        _recordingPath = path;
                }
                RaiseStateChanged();
                break;
            }
            case "ExitStarted":
                session.ObsExiting = true;
                break;
        }
    }

    private async Task<bool> RefreshCoreAsync()
    {
        var streamTask = RequestAsync("GetStreamStatus");
        var recordTask = RequestAsync("GetRecordStatus");
        var stream = await streamTask.ConfigureAwait(false);
        var record = await recordTask.ConfigureAwait(false);

        var changed = false;
        lock (_state)
        {
            if (Volatile.Read(ref _session) == null)
                return false;
            if (stream.Ok)
            {
                var active = Bool(stream.Data, "outputActive") ?? false;
                TimeSpan? duration = active ? TimeSpan.FromMilliseconds(Number(stream.Data, "outputDuration") ?? 0) : null;
                changed |= _streaming != active || _streamDuration != duration;
                _streaming = active;
                _streamDuration = duration;
            }
            if (record.Ok)
            {
                var active = Bool(record.Data, "outputActive") ?? false;
                TimeSpan? duration = active ? TimeSpan.FromMilliseconds(Number(record.Data, "outputDuration") ?? 0) : null;
                changed |= _recording != active || _recordDuration != duration;
                _recording = active;
                _recordDuration = duration;
            }
        }

        if (!stream.Ok)
            Fail(stream);
        if (!record.Ok)
            Fail(record);
        return changed;
    }

    private async Task<bool> CommandAsync(string requestType)
    {
        var reply = await RequestAsync(requestType).ConfigureAwait(false);
        return reply.Ok || Fail(reply);
    }

    private async Task<Reply> RequestAsync(string requestType, Action<Utf8JsonWriter>? writeData = null)
    {
        var session = Volatile.Read(ref _session);
        if (session == null)
            return Reply.Offline;

        var id = Guid.NewGuid().ToString();
        var answer = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Pending[id] = answer;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(session.Stop.Token);
        limit.CancelAfter(RequestTimeout);
        try
        {
            // End drains Pending after marking the session, so a request that slipped in late is caught here.
            if (Volatile.Read(ref session.Ended) != 0)
                return Reply.Lost;

            var payload = Encode(OpRequest, w =>
            {
                w.WriteString("requestType", requestType);
                w.WriteString("requestId", id);
                if (writeData == null)
                    return;
                w.WritePropertyName("requestData");
                w.WriteStartObject();
                writeData(w);
                w.WriteEndObject();
            });

            await session.SendLock.WaitAsync(limit.Token).ConfigureAwait(false);
            try
            {
                await session.Socket.SendAsync(payload, WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
            }
            finally
            {
                session.SendLock.Release();
            }

            return await answer.Task.WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!session.Stop.IsCancellationRequested)
        {
            return new Reply(false, 0, $"OBS did not answer {requestType} within {RequestTimeout.TotalSeconds:0.#} s", default);
        }
        catch (Exception)
        {
            return Reply.Lost;
        }
        finally
        {
            session.Pending.TryRemove(id, out _);
        }
    }

    private async Task CloseAsync(Session session)
    {
        session.Closing = true;
        using (var limit = new CancellationTokenSource(CloseWait))
        {
            try
            {
                await session.SendLock.WaitAsync(limit.Token).ConfigureAwait(false);
                try
                {
                    await session.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lumora disconnected", limit.Token).ConfigureAwait(false);
                }
                finally
                {
                    session.SendLock.Release();
                }
            }
            catch (Exception)
            {
                // A peer that is gone or slow to answer the close gets the socket aborted under it by End.
            }
        }

        End(session, null);
        try
        {
            await session.Receiving.WaitAsync(CloseWait).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void End(Session session, string? reason)
    {
        if (Interlocked.Exchange(ref session.Ended, 1) != 0)
            return;

        bool current;
        lock (_state)
        {
            current = ReferenceEquals(_session, session);
            if (current)
            {
                Volatile.Write(ref _session, null);
                ClearOutputs();
                if (reason != null)
                    _lastError = reason;
            }
        }

        try
        {
            session.Stop.Cancel();
        }
        catch (AggregateException)
        {
        }
        foreach (var id in session.Pending.Keys)
        {
            if (session.Pending.TryRemove(id, out var waiter))
                waiter.TrySetResult(Reply.Lost);
        }
        session.Socket.Dispose();

        if (current)
            RaiseStateChanged();
    }

    private void ClearOutputs()
    {
        _streaming = false;
        _recording = false;
        _streamState = null;
        _recordState = null;
        _streamDuration = null;
        _recordDuration = null;
        _currentScene = null;
    }

    private bool SetCurrentScene(string name)
    {
        lock (_state)
        {
            if (Volatile.Read(ref _session) == null || _currentScene == name)
                return false;
            _currentScene = name;
            return true;
        }
    }

    // A dropped link already left its reason in LastError; a failed request on top of it would only bury it.
    private bool Fail(Reply reply)
    {
        if (reply.Code != 0 || IsConnected || _lastError == null)
            _lastError = reply.Error;
        return false;
    }

    private bool Refuse(string error)
    {
        _lastError = error;
        RaiseStateChanged();
        return false;
    }

    private bool Abandon(ClientWebSocket socket, string error)
    {
        socket.Dispose();
        return Refuse(error);
    }

    private void RaiseStateChanged()
    {
        if (StateChanged is not { } handlers)
            return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception)
            {
                // Swallowed on purpose: see the threading note on the class.
            }
        }
    }

    private static async Task AnswerCloseAsync(ClientWebSocket socket)
    {
        try
        {
            using var limit = new CancellationTokenSource(CloseWait);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, limit.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    // One whole message, or null once the peer has closed. The bytes are only good until the next call.
    private static async Task<ReadOnlyMemory<byte>?> ReceiveMessageAsync(ClientWebSocket socket, MemoryStream assembly, byte[] chunk, CancellationToken token)
    {
        assembly.SetLength(0);
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk.AsMemory(), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.EndOfMessage && assembly.Length == 0)
                return new ReadOnlyMemory<byte>(chunk, 0, result.Count);
            assembly.Write(chunk, 0, result.Count);
            if (assembly.Length > MaxMessageBytes)
                throw new InvalidDataException($"OBS sent a message over {MaxMessageBytes / (1024 * 1024)} MB");
            if (result.EndOfMessage)
                return new ReadOnlyMemory<byte>(assembly.GetBuffer(), 0, (int)assembly.Length);
        }
    }

    private static ReadOnlyMemory<byte> Encode(int op, Action<Utf8JsonWriter> writeData)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("op", op);
            writer.WritePropertyName("d");
            writer.WriteStartObject();
            writeData(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.WrittenMemory;
    }

    private static string AuthResponse(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static string DescribeClose(ClientWebSocket socket)
    {
        var code = (int?)socket.CloseStatus;
        var text = code switch
        {
            4009 => "OBS rejected the password (authentication failed)",
            4010 => "OBS does not speak this RPC version",
            4011 => "OBS ended the session",
            4012 => "OBS does not support a feature this link asked for",
            null or 1000 or 1001 => "OBS closed the connection",
            _ => $"OBS closed the connection (code {code})",
        };
        var detail = socket.CloseStatusDescription;
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text}: {detail.Trim()}";
    }

    private static string CodeText(int code) => code switch
    {
        204 => "unknown request type",
        207 => "OBS is not ready yet",
        500 => "the output is already running",
        501 => "the output is not running",
        600 => "not found",
        _ => "no reason given",
    };

    private static string Innermost(Exception e)
    {
        while (e.InnerException != null)
            e = e.InnerException;
        return e.Message;
    }

    private static int OpOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("op", out var op)
        && op.ValueKind == JsonValueKind.Number
        && op.TryGetInt32(out var value)
            ? value
            : -1;

    private static bool TryData(JsonElement root, out JsonElement data)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("d", out data)
            && data.ValueKind == JsonValueKind.Object)
            return true;
        data = default;
        return false;
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static double? Number(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private readonly record struct Reply(bool Ok, int Code, string? Error, JsonElement Data)
    {
        public static readonly Reply Offline = new(false, 0, "Not connected to OBS", default);
        public static readonly Reply Lost = new(false, 0, "The connection to OBS closed", default);
    }

    private sealed class Session
    {
        public readonly ClientWebSocket Socket;
        public readonly byte[] Chunk;
        public readonly MemoryStream Assembly;
        public readonly CancellationTokenSource Stop = new();
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public readonly ConcurrentDictionary<string, TaskCompletionSource<Reply>> Pending = new(StringComparer.Ordinal);
        public Task Receiving = Task.CompletedTask;
        public int Ended;
        public volatile bool Closing;
        public volatile bool ObsExiting;

        public Session(ClientWebSocket socket, byte[] chunk, MemoryStream assembly)
        {
            Socket = socket;
            Chunk = chunk;
            Assembly = assembly;
        }
    }
}
