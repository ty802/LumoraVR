// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Localization;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Web;

// THE HTTP CLIENT BEHIND A SCRIPT'S WEB REQUEST.
//
// A script can make this machine talk to the internet, so every request passes three gates before a
// byte leaves: the URL is http or https, this machine's user has allowed the host (WebAccess, which may
// prompt), and the socket only ever connects to a public address. That last check lives in the connect
// step itself, after this client's own name resolution, so a name that resolves somewhere public while
// the user is asked and somewhere local by the time the socket opens gets nothing. Redirects are
// followed by hand so every hop passes all three again.
//
// Everything is capped: sizes both ways, time, requests running per requester and per machine, starts
// per second. A request past a cap fails at once and says which one. Headers and bodies carry tokens,
// so neither is ever logged. -xlinka
public sealed class ScriptWebClient
{
    public const int MaxRequestBodyChars = 65536;
    public const int MaxRequestHeaderChars = 16384;
    public const int MaxResponseBodyChars = 1048576;
    public const int MaxResponseBodyBytes = 4 * 1024 * 1024;
    public const int MaxResponseHeaderChars = 65536;
    public const int MaxRedirects = 5;
    public const float DefaultTimeoutSeconds = 30f;
    public const float MinTimeoutSeconds = 1f;
    public const float MaxTimeoutSeconds = 60f;

    public static readonly string UserAgent = $"LumoraVR/{EngineVersion.VersionString} (script)";

    private const string DefaultContentType = "application/json; charset=utf-8";
    private const int ConnectionsPerServer = 8;
    // KiB, and above the header cap on purpose: the cap below decides, with its own message.
    private const int HandlerHeaderLimitKb = 128;
    private const int ReadChunk = 16 * 1024;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(3);

    // Carries the BOM as its preamble so a body that starts with one loses it; bad bytes become U+FFFD.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: false);

    // The stock replacement fallback writes "?", which reads as a real question mark. U+FFFD does not.
    private static readonly DecoderFallback BadBytes = new DecoderReplacementFallback("\uFFFD");

    private static readonly Lazy<ScriptWebClient> _shared = new(() =>
        new ScriptWebClient(WebAccess.Shared, WebAddressFilter.IsPublic, null));

    // A test hands a client with a looser filter to the script nodes here; nothing a script or a setting can
    // reach sets it.
    internal static ScriptWebClient? Replacement = null;

    public static ScriptWebClient Shared => Replacement ?? _shared.Value;

    private readonly WebAccess _access;
    private readonly Func<IPAddress, bool> _allowAddress;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly ScriptWebLimits _limits = new();
    private readonly HttpClient _http;

    // The shared client passes the public-only filter and system resolution. A test builds a separate
    // client with a looser filter or a fake resolver, and only through here.
    internal ScriptWebClient(WebAccess access, Func<IPAddress, bool> allowAddress,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve)
    {
        _access = access;
        _allowAddress = allowAddress;
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));

        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = ConnectionLifetime,
            MaxConnectionsPerServer = ConnectionsPerServer,
            MaxResponseHeadersLength = HandlerHeaderLimitKb,
            // A proxy would open the real connection itself, out of the address check's sight.
            UseProxy = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            PreAuthenticate = false,
            ConnectCallback = ConnectAsync,
        };
        _http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    internal int Running => _limits.Running;

    // The approvals this client asks, for nodes that reach an address by other means and need the same
    // leave, and for nodes that read a decision or ask ahead of a request.
    internal WebAccess Access => _access;

    // Why a host can never be reached from here, or null when it might be.
    internal string? Unreachable(string host) => RefuseLocal(host);

    // The connect step every request takes, for a websocket's handshake: resolved here, public addresses only.
    internal ValueTask<Stream> ConnectFiltered(SocketsHttpConnectionContext context, CancellationToken token)
        => ConnectAsync(context, token);

    // Never throws. A cancelled request ends Failed with "Cancelled". requesterKey scopes the in-flight
    // and start-rate limits; askerName and worldName are what the prompt shows.
    public async Task<ScriptWebResult> SendAsync(ScriptWebRequest request, string requesterKey, string askerName,
        string worldName, CancellationToken cancel)
    {
        var url = request?.Url;
        string shown = url == null ? string.Empty : url.IsAbsoluteUri ? url.AbsoluteUri : url.OriginalString;
        if (request == null || url == null || !url.IsAbsoluteUri || !IsWebScheme(url))
            return ScriptWebResult.Failed(shown, "Only http and https URLs can be requested");
        if (request.Body != null && request.Body.Length > MaxRequestBodyChars)
            return ScriptWebResult.Failed(shown, $"The request body is longer than {MaxRequestBodyChars} characters");
        if (request.Headers != null && request.Headers.Length > MaxRequestHeaderChars)
            return ScriptWebResult.Failed(shown, $"The request headers are longer than {MaxRequestHeaderChars} characters");

        string key = requesterKey ?? string.Empty;
        var refusal = _limits.TryEnter(key);
        if (refusal != null)
            return ScriptWebResult.Failed(shown, refusal);

        ScriptWebResult result;
        try
        {
            result = await RunAsync(request, url, askerName ?? string.Empty, worldName ?? string.Empty, cancel)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            result = ScriptWebResult.Failed(shown, cancel.IsCancellationRequested ? "Cancelled" : "The request failed");
        }
        finally
        {
            _limits.Exit(key);
        }

        if (LumoraLogger.EnableDebug)
            LumoraLogger.Debug($"ScriptWebClient: {request.Method} {WebAccess.HostKey(url) ?? "?"} {result.Outcome}");
        return result;
    }

    internal static float ClampTimeout(float seconds)
    {
        if (seconds == 0f || !float.IsFinite(seconds))
            return DefaultTimeoutSeconds;
        return System.Math.Clamp(seconds, MinTimeoutSeconds, MaxTimeoutSeconds);
    }

    // One hop per pass. The timeout is a budget of network time across every hop; time spent waiting on
    // the user's answer is not charged to it.
    private async Task<ScriptWebResult> RunAsync(ScriptWebRequest request, Uri url, string askerName, string worldName,
        CancellationToken cancel)
    {
        var method = request.Method;
        var headers = ScriptWebHeaders.Parse(request.Headers);
        byte[]? body = CarriesBody(method) && !string.IsNullOrEmpty(request.Body) ? Encoding.UTF8.GetBytes(request.Body) : null;
        var timeout = TimeSpan.FromSeconds(ClampTimeout(request.TimeoutSeconds));
        var remaining = timeout;
        var current = url;
        string? cameFrom = null;

        for (int hop = 0; ; hop++)
        {
            string at = current.AbsoluteUri;
            var host = WebAccess.HostKey(current);
            if (host == null)
                return ScriptWebResult.Failed(at, "The URL has no host");
            var local = RefuseLocal(host);
            if (local != null)
                return ScriptWebResult.Failed(at, local);

            string reason = cameFrom == null
                ? "WebAccess.Reason.Request".AsLocale("A script wants to send a {0} request here.", method.ToString()).Resolve()
                : "WebAccess.Reason.Redirect".AsLocale("A redirect from {0} sent a script's request here.", cameFrom).Resolve();
            var answer = await _access.AskAsync(host, askerName, worldName, reason, cancel).ConfigureAwait(false);
            if (answer != WebAccessAnswer.Allowed)
            {
                return ScriptWebResult.Denied(at, answer == WebAccessAnswer.Denied
                    ? $"Web access to {host} was denied"
                    : $"Web access to {host} was not answered");
            }

            long started = Stopwatch.GetTimestamp();
            using var hopCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            hopCancel.CancelAfter(remaining);

            HttpResponseMessage response;
            try
            {
                using var message = BuildMessage(method, current, headers, body);
                response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, hopCancel.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Failure(ex, at, host, timeout, cancel, hopCancel.IsCancellationRequested);
            }

            using (response)
            {
                var location = response.Headers.Location;
                if (IsRedirect(response.StatusCode) && location != null)
                {
                    if (hop >= MaxRedirects)
                        return ScriptWebResult.Failed(at, $"Stopped after {MaxRedirects} redirects");
                    if (!Uri.TryCreate(current, location, out var next) || !IsWebScheme(next))
                        return ScriptWebResult.Failed(at, "A redirect pointed somewhere other than an http or https URL");

                    (method, body) = Redirected(response.StatusCode, method, body);
                    if (!SameOrigin(current, next))
                        DropCredentials(headers);
                    cameFrom = host;
                    current = next;
                    remaining -= Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero)
                        return ScriptWebResult.Failed(current.AbsoluteUri, TimedOut(timeout));
                    continue;
                }

                try
                {
                    return await ReadAsync(response, at, method, hopCancel.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Failure(ex, at, host, timeout, cancel, hopCancel.IsCancellationRequested);
                }
            }
        }
    }

    // An address written straight into the URL is checked before anyone is asked about it. The connect
    // step would refuse it anyway, but a prompt for a host that can only ever fail helps nobody.
    private string? RefuseLocal(string host)
    {
        if (IPAddress.TryParse(host, out var literal))
            return _allowAddress(literal) ? null : $"{host} is a local network address, which scripts cannot reach";
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return _allowAddress(IPAddress.Loopback) || _allowAddress(IPAddress.IPv6Loopback)
                ? null
                : $"{host} is this machine, which scripts cannot reach";
        }
        return null;
    }

    // Resolves the host here rather than letting the socket layer do it, so the filter sees every
    // address before a connection is attempted, and tries the allowed ones in the order they came back.
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var target = context.DnsEndPoint;
        string host = target.Host;
        if (host.Length > 2 && host[0] == '[' && host[^1] == ']')
            host = host.Substring(1, host.Length - 2);

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await _resolve(host, token).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        Exception? failure = null;
        bool anyAllowed = false;
        foreach (var candidate in addresses)
        {
            if (!_allowAddress(candidate))
                continue;
            anyAllowed = true;
            var address = candidate.IsIPv4MappedToIPv6 ? candidate.MapToIPv4() : candidate;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, target.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                if (token.IsCancellationRequested)
                    throw;
                failure = ex;
            }
        }

        if (!anyAllowed)
            throw new WebAddressBlockedException($"{host} only leads to local network addresses, which scripts cannot reach");
        throw failure ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private static HttpRequestMessage BuildMessage(WebMethod method, Uri url, List<KeyValuePair<string, string>> headers,
        byte[]? body)
    {
        var message = new HttpRequestMessage(ToHttpMethod(method), url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        // One content object per message, and every content header goes on that same object. Content
        // headers live on the content, so a second copy built after they were applied would drop them.
        var content = body != null ? new ByteArrayContent(body) : null;
        bool typed = false;
        foreach (var (name, value) in headers)
        {
            if (!ScriptWebHeaders.IsContentHeader(name))
            {
                message.Headers.TryAddWithoutValidation(name, value);
                continue;
            }
            if (content == null)
                continue;
            content.Headers.TryAddWithoutValidation(name, value);
            typed |= string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase);
        }

        if (content != null)
        {
            if (!typed)
                content.Headers.TryAddWithoutValidation("Content-Type", DefaultContentType);
            message.Content = content;
        }
        return message;
    }

    private static async Task<ScriptWebResult> ReadAsync(HttpResponseMessage response, string url, WebMethod method,
        CancellationToken token)
    {
        string headers = ScriptWebHeaders.Format(response, MaxResponseHeaderChars);
        if (headers.Length > MaxResponseHeaderChars)
            return ScriptWebResult.Failed(url, $"The response headers are longer than {MaxResponseHeaderChars} characters");

        string body = string.Empty;
        if (method != WebMethod.HEAD)
        {
            if (response.Content.Headers.ContentLength > MaxResponseBodyBytes)
                return ScriptWebResult.Failed(url, TooLarge());

            // Read against the byte cap and stop the moment it is crossed. With decompression on, this
            // counts decompressed bytes, which is the number that matters.
            var buffer = new MemoryStream();
            var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var chunk = new byte[ReadChunk];
                int read;
                while ((read = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > MaxResponseBodyBytes)
                        return ScriptWebResult.Failed(url, TooLarge());
                    buffer.Write(chunk, 0, read);
                }
            }

            body = Decode(buffer.GetBuffer(), (int)buffer.Length, response.Content.Headers.ContentType?.CharSet);
            if (body.Length > MaxResponseBodyChars)
                return ScriptWebResult.Failed(url, $"The response body is longer than {MaxResponseBodyChars} characters");
        }

        return new ScriptWebResult(WebOutcome.Response, (int)response.StatusCode, body, headers, url, string.Empty);
    }

    private static string Decode(byte[] bytes, int length, string? charset)
    {
        var encoding = EncodingFor(charset);
        var preamble = encoding.Preamble;
        int start = preamble.Length > 0 && length >= preamble.Length && bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble)
            ? preamble.Length
            : 0;
        return encoding.GetString(bytes, start, length - start);
    }

    // The charset the response names, UTF-8 when it names none or one this runtime does not know. Bytes
    // that do not decode become U+FFFD rather than failing the request.
    private static Encoding EncodingFor(string? charset)
    {
        var name = charset?.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(name))
            return Utf8;
        try
        {
            return Encoding.GetEncoding(name, EncoderFallback.ReplacementFallback, BadBytes);
        }
        catch (ArgumentException)
        {
        }
        try
        {
            var legacy = CodePagesEncodingProvider.Instance.GetEncoding(name, EncoderFallback.ReplacementFallback, BadBytes);
            if (legacy != null)
                return legacy;
        }
        catch (ArgumentException)
        {
        }
        return Utf8;
    }

    // Short, and never a stack trace or anything the request carried. hopExpired says the hop's own
    // budget ran out, which is a timeout whatever the stack reported it as: a body cut off mid-read can
    // surface as a plain IO error.
    private static ScriptWebResult Failure(Exception ex, string url, string host, TimeSpan timeout, CancellationToken cancel,
        bool hopExpired)
    {
        if (cancel.IsCancellationRequested)
            return ScriptWebResult.Failed(url, "Cancelled");
        var blocked = Find<WebAddressBlockedException>(ex);
        if (blocked != null)
            return ScriptWebResult.Failed(url, blocked.Message);
        if (hopExpired)
            return ScriptWebResult.Failed(url, TimedOut(timeout));
        if (ex is OperationCanceledException)
        {
            return ScriptWebResult.Failed(url, ex.InnerException is TimeoutException
                ? $"Could not connect to {host} in time"
                : TimedOut(timeout));
        }

        var socket = Find<SocketException>(ex);
        if (socket != null)
        {
            return ScriptWebResult.Failed(url, socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                ? $"Could not find {host}"
                : $"Could not connect to {host}");
        }
        if (Find<AuthenticationException>(ex) != null)
            return ScriptWebResult.Failed(url, $"Could not make a secure connection to {host}");

        if (ex is HttpRequestException http)
        {
            switch (http.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError:
                    return ScriptWebResult.Failed(url, $"Could not find {host}");
                case HttpRequestError.ConnectionError:
                    return ScriptWebResult.Failed(url, $"Could not connect to {host}");
                case HttpRequestError.SecureConnectionError:
                    return ScriptWebResult.Failed(url, $"Could not make a secure connection to {host}");
                case HttpRequestError.ConfigurationLimitExceeded:
                    return ScriptWebResult.Failed(url, $"The response headers are longer than {MaxResponseHeaderChars} characters");
                case HttpRequestError.ResponseEnded:
                    return ScriptWebResult.Failed(url, $"{host} closed the connection before the response was complete");
                case HttpRequestError.InvalidResponse:
                case HttpRequestError.HttpProtocolError:
                    return ScriptWebResult.Failed(url, $"{host} sent a response that is not valid HTTP");
            }
        }
        if (ex is IOException)
            return ScriptWebResult.Failed(url, $"The connection to {host} failed");
        return ScriptWebResult.Failed(url, $"The request to {host} failed");
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

    private static string TimedOut(TimeSpan timeout) => $"Timed out after {timeout.TotalSeconds:0.#} s";

    private static string TooLarge() => $"The response body is larger than {MaxResponseBodyBytes / (1024 * 1024)} MiB";

    private static bool IsWebScheme(Uri url)
        => url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps;

    private static bool CarriesBody(WebMethod method)
        => method is WebMethod.POST or WebMethod.PUT or WebMethod.PATCH or WebMethod.DELETE;

    private static bool IsRedirect(HttpStatusCode status)
        => (int)status is 301 or 302 or 303 or 307 or 308;

    // 301 and 302 turn a POST into a GET, and 303 turns anything but GET and HEAD into one, dropping the
    // body. 307 and 308 repeat the request as it was.
    private static (WebMethod Method, byte[]? Body) Redirected(HttpStatusCode status, WebMethod method, byte[]? body)
    {
        switch ((int)status)
        {
            case 301:
            case 302:
                return method == WebMethod.POST ? (WebMethod.GET, null) : (method, body);
            case 303:
                return method is WebMethod.GET or WebMethod.HEAD ? (method, null) : (WebMethod.GET, null);
            default:
                return (method, body);
        }
    }

    private static bool SameOrigin(Uri a, Uri b)
        => string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase)
           && a.Port == b.Port;

    // Credentials the script meant for one origin are not handed to whatever origin it redirects to.
    private static void DropCredentials(List<KeyValuePair<string, string>> headers)
    {
        headers.RemoveAll(header =>
            string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
            || string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpMethod ToHttpMethod(WebMethod method) => method switch
    {
        WebMethod.POST => HttpMethod.Post,
        WebMethod.PUT => HttpMethod.Put,
        WebMethod.PATCH => HttpMethod.Patch,
        WebMethod.DELETE => HttpMethod.Delete,
        WebMethod.HEAD => HttpMethod.Head,
        WebMethod.OPTIONS => HttpMethod.Options,
        _ => HttpMethod.Get,
    };
}
