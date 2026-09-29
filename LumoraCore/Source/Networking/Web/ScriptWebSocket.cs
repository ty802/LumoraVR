// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Localization;

namespace Lumora.Core.Networking.Web;

// A LIVE CONNECTION A SCRIPT OPENED: text messages both ways until one side closes it.
//
// It opens the way a web request is sent. ws or wss only; this machine's user allows the host first,
// through the same decisions HTTP Request asks; and the socket only ever connects to a public address,
// through the web client's own connect step, so a name that resolves somewhere local gets nothing. The
// handshake carries the script's headers minus the ones that belong to the connection, and says what it
// is in its User-Agent. No proxy, no cookies, no credentials of this machine's.
//
// Open sockets count against their own per-requester and per-machine limits, the web request ones'
// numbers, and a socket holds its place until it closes. What a script sends is capped like a request
// body and what comes in like a response body: a message past that closes the socket, so a server
// cannot stream a script out of memory. Binary messages are not text and are dropped.
//
// Messages and the close arrive on a reader thread, through the callbacks given at connect. Closing
// sends a close frame and waits a little for the server's; the lifetime token closes it the same way,
// which is how a script that stops takes its sockets with it. -xlinka
public sealed class ScriptWebSocket
{
    public const int MaxSendChars = ScriptWebClient.MaxRequestBodyChars;
    public const int MaxMessageChars = ScriptWebClient.MaxResponseBodyChars;
    public const int MaxMessageBytes = ScriptWebClient.MaxResponseBodyBytes;

    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
    private const int ReadChunk = 16 * 1024;
    private const int MaxReasonChars = 120;

    // Bad bytes become U+FFFD rather than failing the message.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private static readonly ScriptWebLimits Limits = new("websockets");

    private readonly ClientWebSocket _socket = new();
    private readonly HttpMessageInvoker _invoker;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly object _queueLock = new();
    private Task _queue = Task.CompletedTask;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _requester;
    private readonly Action<ScriptWebSocket, string> _received;
    private readonly Action<ScriptWebSocket, string> _closed;
    private CancellationTokenRegistration _lifetime;
    private string? _closeReason;
    private int _closing;
    private int _finished;

    public Uri Url { get; }

    public bool IsOpen
        => Volatile.Read(ref _finished) == 0 && Volatile.Read(ref _closing) == 0 && _socket.State == WebSocketState.Open;

    // Why it closed, once it has; empty while it is open.
    public string ClosedBecause { get; private set; } = string.Empty;

    // Sockets scripts hold open on this machine.
    internal static int OpenCount => Limits.Running;

    private ScriptWebSocket(Uri url, string requester, ScriptWebClient client, Action<ScriptWebSocket, string> received,
        Action<ScriptWebSocket, string> closed)
    {
        Url = url;
        _requester = requester;
        _received = received;
        _closed = closed;
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = ConnectTimeout,
            // A proxy would open the real connection itself, out of the address check's sight.
            UseProxy = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            PreAuthenticate = false,
            ConnectCallback = client.ConnectFiltered,
        };
        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
    }

    // Never throws. The socket comes back open with its reader running, or not at all. requesterKey scopes
    // the limits; askerName and worldName are what the prompt shows.
    public static async Task<ScriptWebSocketResult> ConnectAsync(Uri? url, string? headers, string? protocols, float timeoutSeconds,
        string requesterKey, string askerName, string worldName, Action<ScriptWebSocket, string> received,
        Action<ScriptWebSocket, string> closed, CancellationToken lifetime)
    {
        string shown = url == null ? string.Empty : url.IsAbsoluteUri ? url.AbsoluteUri : url.OriginalString;
        if (url == null || !url.IsAbsoluteUri || !IsSocketScheme(url))
            return ScriptWebSocketResult.Failed(shown, "Only ws and wss URLs can be opened");
        if (headers != null && headers.Length > ScriptWebClient.MaxRequestHeaderChars)
            return ScriptWebSocketResult.Failed(shown, $"The request headers are longer than {ScriptWebClient.MaxRequestHeaderChars} characters");

        var client = ScriptWebClient.Shared;
        var host = WebAccess.HostKey(url);
        if (host == null)
            return ScriptWebSocketResult.Failed(shown, "The URL has no host");
        var local = client.Unreachable(host);
        if (local != null)
            return ScriptWebSocketResult.Failed(shown, local);

        string key = requesterKey ?? string.Empty;
        var refusal = Limits.TryEnter(key);
        if (refusal != null)
            return ScriptWebSocketResult.Failed(shown, refusal);

        ScriptWebSocket? socket = null;
        bool handedOver = false;
        try
        {
            string reason = "WebAccess.Reason.Socket".AsLocale("A script wants to open a live connection here.").Resolve();
            var answer = await client.Access.AskAsync(host, askerName ?? string.Empty, worldName ?? string.Empty, reason, lifetime)
                .ConfigureAwait(false);
            if (answer != WebAccessAnswer.Allowed)
            {
                return ScriptWebSocketResult.Denied(shown, answer == WebAccessAnswer.Denied
                    ? $"Web access to {host} was denied"
                    : $"Web access to {host} was not answered");
            }

            socket = new ScriptWebSocket(url, key, client, received, closed);
            var error = await socket.OpenAsync(headers, protocols, ScriptWebClient.ClampTimeout(timeoutSeconds), host, lifetime)
                .ConfigureAwait(false);
            if (error != null)
                return ScriptWebSocketResult.Failed(shown, error);

            handedOver = true;
            socket.Run(lifetime);
            return ScriptWebSocketResult.Opened(shown, socket);
        }
        catch (Exception)
        {
            return ScriptWebSocketResult.Failed(shown, lifetime.IsCancellationRequested ? "Cancelled" : "The websocket could not be opened");
        }
        finally
        {
            if (!handedOver)
            {
                socket?.Discard();
                Limits.Exit(key);
            }
        }
    }

    // Null once the text went out, else why it did not. Sends go out in the order they were asked for.
    public Task<string?> SendAsync(string? text, CancellationToken cancel)
    {
        lock (_queueLock)
        {
            var next = _queue.ContinueWith(_ => SendNowAsync(text, cancel), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            _queue = next;
            return next;
        }
    }

    private async Task<string?> SendNowAsync(string? text, CancellationToken cancel)
    {
        text ??= string.Empty;
        if (text.Length > MaxSendChars)
            return $"The message is longer than {MaxSendChars} characters";
        if (!IsOpen)
            return "The websocket is not open";

        var bytes = Utf8.GetBytes(text);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        attempt.CancelAfter(SendTimeout);
        bool entered = false;
        try
        {
            await _sending.WaitAsync(attempt.Token).ConfigureAwait(false);
            entered = true;
            if (!IsOpen)
                return "The websocket is not open";
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, attempt.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception)
        {
            if (cancel.IsCancellationRequested)
                return "Cancelled";
            if (attempt.IsCancellationRequested)
                return $"Timed out after {SendTimeout.TotalSeconds:0} s";
            return IsOpen ? "The message could not be sent" : "The websocket closed";
        }
        finally
        {
            if (entered)
                _sending.Release();
        }
    }

    // A close frame to the server, then the reader waits a little for the server's own and reports it closed.
    public void Close() => BeginClose("closed by the script");

    public static bool IsSocketScheme(Uri url) => url.Scheme == Uri.UriSchemeWs || url.Scheme == Uri.UriSchemeWss;

    private async Task<string?> OpenAsync(string? headers, string? protocols, float timeoutSeconds, string host, CancellationToken lifetime)
    {
        var options = _socket.Options;
        options.HttpVersion = HttpVersion.Version11;
        options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        options.KeepAliveInterval = KeepAlive;
        options.SetRequestHeader("User-Agent", ScriptWebClient.UserAgent);
        foreach (var (name, value) in ScriptWebHeaders.Parse(headers))
        {
            // The handshake's own headers belong to the client, like framing does for a request.
            if (name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                options.SetRequestHeader(name, value);
            }
            catch (ArgumentException)
            {
            }
        }
        if (!string.IsNullOrWhiteSpace(protocols))
        {
            foreach (var protocol in protocols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    options.AddSubProtocol(protocol);
                }
                catch (ArgumentException)
                {
                    return $"'{protocol}' is not a usable protocol name";
                }
            }
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        attempt.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await _socket.ConnectAsync(Url, _invoker, attempt.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            if (lifetime.IsCancellationRequested)
                return "Cancelled";
            if (Find<WebAddressBlockedException>(ex) is { } blocked)
                return blocked.Message;
            if (attempt.IsCancellationRequested)
                return $"Timed out after {timeoutSeconds:0.#} s";
            if (Find<SocketException>(ex) is { } socket)
            {
                return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                    ? $"Could not find {host}"
                    : $"Could not connect to {host}";
            }
            if (Find<AuthenticationException>(ex) != null)
                return $"Could not make a secure connection to {host}";
            if (Find<WebSocketException>(ex) is { WebSocketErrorCode: WebSocketError.NotAWebSocket })
                return $"{host} answered without opening a websocket";
            return $"Could not open a websocket to {host}";
        }
    }

    private void Run(CancellationToken lifetime)
    {
        _lifetime = lifetime.Register(static state => ((ScriptWebSocket)state!).BeginClose("the script stopped"), this);
        _ = Task.Run(ReadAsync);
    }

    private async Task ReadAsync()
    {
        var chunk = new byte[ReadChunk];
        var message = new MemoryStream();
        string? reason = null;
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(chunk.AsMemory(), _stop.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    reason = _closeReason ?? ClosedByServer();
                    if (_socket.State == WebSocketState.CloseReceived)
                        await SendCloseAsync(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
                    break;
                }
                if (message.Length + result.Count > MaxMessageBytes)
                {
                    reason = $"a message was larger than {MaxMessageBytes / (1024 * 1024)} MiB";
                    await SendCloseAsync(WebSocketCloseStatus.MessageTooBig).ConfigureAwait(false);
                    break;
                }
                message.Write(chunk, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Utf8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    if (text.Length > MaxMessageChars)
                    {
                        reason = $"a message was longer than {MaxMessageChars} characters";
                        await SendCloseAsync(WebSocketCloseStatus.MessageTooBig).ConfigureAwait(false);
                        break;
                    }
                    // Once the script asked to close, what still comes in is not for it.
                    if (Volatile.Read(ref _closing) == 0)
                        _received(this, text);
                }
                message.SetLength(0);
            }
        }
        catch (Exception)
        {
            reason ??= _closeReason ?? "the connection was lost";
        }
        Finish(reason ?? _closeReason ?? "the connection closed");
    }

    private void BeginClose(string reason)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;
        _closeReason = reason;
        _ = Task.Run(async () =>
        {
            await SendCloseAsync(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
            try
            {
                _stop.CancelAfter(CloseTimeout);
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }

    // One send at a time on a websocket, a close frame included.
    private async Task SendCloseAsync(WebSocketCloseStatus status)
    {
        Interlocked.Exchange(ref _closing, 1);
        bool entered = false;
        try
        {
            entered = await _sending.WaitAsync(CloseTimeout).ConfigureAwait(false);
            if (entered && _socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var wait = new CancellationTokenSource(CloseTimeout);
                await _socket.CloseOutputAsync(status, string.Empty, wait.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            if (entered)
                _sending.Release();
        }
    }

    private string ClosedByServer()
    {
        int code = (int?)_socket.CloseStatus ?? 0;
        var said = new StringBuilder();
        foreach (char c in _socket.CloseStatusDescription ?? string.Empty)
        {
            if (said.Length >= MaxReasonChars)
                break;
            said.Append(char.IsControl(c) ? ' ' : c);
        }
        var text = said.ToString().Trim();
        return text.Length > 0 ? $"closed by the server ({code}: {text})" : $"closed by the server ({code})";
    }

    private void Finish(string reason)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
            return;
        Interlocked.Exchange(ref _closing, 1);
        ClosedBecause = reason;
        _lifetime.Unregister();
        Limits.Exit(_requester);
        try
        {
            _socket.Dispose();
            _invoker.Dispose();
        }
        catch (Exception)
        {
        }
        try
        {
            _closed(this, reason);
        }
        catch (Exception)
        {
        }
    }

    // A socket that never opened: nothing was handed out and nobody hears of it.
    private void Discard()
    {
        Interlocked.Exchange(ref _finished, 1);
        try
        {
            _socket.Dispose();
            _invoker.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is T match)
                return match;
        }
        return null;
    }
}
