// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Components.UI;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Web;

// WHICH SITES SCRIPTS MAY REACH, ANSWERED BY THIS MACHINE'S OWN USER.
//
// The machine that makes a request is the one that asks, and it asks the person sitting at it. A
// shared script runs on the session host, so the host's user decides for it; a local script runs on
// every machine and each machine's user decides for that machine. Nothing here replicates.
//
// A host nobody has decided yet goes to the user as a prompt. Asks for the same host share one prompt,
// one prompt is on screen at a time, and a machine with nobody to ask (a server, a headless run) says
// no without remembering it. Allow and Deny are kept in a small file in the data folder and edited from
// Settings. A prompt that is dismissed or left alone for two minutes is not kept, so the next request
// asks again. -xlinka
public sealed class WebAccess
{
    public const string FileName = "web_access.json";

    // Distinct hosts that may wait for an answer at once, the one on screen included. A new host past
    // this is refused on the spot, so a script cannot stack up a wall of prompts.
    public const int MaxWaitingHosts = 5;

    public static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

    private const int FileVersion = 1;
    private const int MaxPromptText = 200;

    private static readonly Lazy<WebAccess> _shared = new(() =>
        new WebAccess(PathResolver.Resolve(FileName), new WebAccessDashboardPrompter(), PromptTimeout));

    public static WebAccess Shared => _shared.Value;

    private readonly object _lock = new();
    private readonly string _path;
    private readonly IWebAccessPrompter? _prompter;
    private readonly TimeSpan _promptTimeout;
    private Dictionary<string, bool>? _decisions;
    // Hosts waiting for a turn on screen, oldest first. The one on screen is _showing and not in here.
    private readonly List<Pending> _queue = new();
    private Pending? _showing;
    private int _version;

    // Shared is the only instance the engine makes. A test builds one on a scratch file with a fake
    // prompter and a short timeout.
    internal WebAccess(string path, IWebAccessPrompter? prompter, TimeSpan promptTimeout)
    {
        _path = path;
        _prompter = prompter;
        _promptTimeout = promptTimeout;
    }

    // Bumped on every change to the saved decisions. Settings polls it to know when to rebuild its rows.
    public int Version => Volatile.Read(ref _version);

    // HOST KEYS

    // What a decision is stored under: the host alone, lowercased, an international name in punycode.
    // Scheme and port are not part of it. Null when there is no usable host.
    public static string? HostKey(Uri? url)
    {
        if (url == null || !url.IsAbsoluteUri)
            return null;
        try
        {
            return NormalizeHost(url.IdnHost);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;
        string text = host.Trim();
        if (text.Length > 2 && text[0] == '[' && text[^1] == ']')
            text = text.Substring(1, text.Length - 2);
        // "example.com." is the same site as "example.com".
        if (text.EndsWith('.'))
            text = text.Substring(0, text.Length - 1);
        if (text.Length == 0)
            return null;
        if (!Ascii.IsValid(text))
        {
            try
            {
                text = new IdnMapping().GetAscii(text);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        text = text.ToLowerInvariant();
        if (text.Length > 253)
            return null;
        foreach (char c in text)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_' or ':'))
                return null;
        }
        return text;
    }

    // DECISIONS

    public bool IsAllowed(string host)
    {
        var key = NormalizeHost(host);
        if (key == null)
            return false;
        lock (_lock)
        {
            EnsureLoaded();
            return _decisions!.TryGetValue(key, out bool allowed) && allowed;
        }
    }

    // False for a host nobody has decided.
    public bool TryGetDecision(string host, out bool allowed)
    {
        allowed = false;
        var key = NormalizeHost(host);
        if (key == null)
            return false;
        lock (_lock)
        {
            EnsureLoaded();
            return _decisions!.TryGetValue(key, out allowed);
        }
    }

    // Every decided host, sorted by name.
    public IReadOnlyList<WebAccessEntry> Entries()
    {
        lock (_lock)
        {
            EnsureLoaded();
            var list = new List<WebAccessEntry>(_decisions!.Count);
            foreach (var pair in _decisions)
                list.Add(new WebAccessEntry(pair.Key, pair.Value));
            list.Sort((a, b) => string.CompareOrdinal(a.Host, b.Host));
            return list;
        }
    }

    // Saved exactly as if the user had answered a prompt with it, and anyone already waiting on that
    // host gets the answer now.
    public bool Set(string host, bool allowed)
    {
        var key = NormalizeHost(host);
        if (key == null)
            return false;

        WebAccessQuestion? withdrawn = null;
        Pending? next = null;
        lock (_lock)
        {
            EnsureLoaded();
            if (!_decisions!.TryGetValue(key, out bool current) || current != allowed)
            {
                _decisions[key] = allowed;
                SaveLocked();
                _version++;
            }

            var answer = allowed ? WebAccessAnswer.Allowed : WebAccessAnswer.Denied;
            if (_showing != null && _showing.Host == key)
            {
                var shown = _showing;
                _showing = null;
                shown.StopTimer();
                ResolveLocked(shown, answer);
                withdrawn = shown.Question;
                next = TakeNextLocked();
            }
            else
            {
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i].Host != key)
                        continue;
                    ResolveLocked(_queue[i], answer);
                    _queue.RemoveAt(i);
                    break;
                }
            }
        }

        if (withdrawn != null)
            WithdrawSafe(withdrawn);
        Present(next);
        return true;
    }

    // Forgets a decision, so the next request for that host asks again.
    public bool Remove(string host)
    {
        var key = NormalizeHost(host);
        if (key == null)
            return false;
        lock (_lock)
        {
            EnsureLoaded();
            if (!_decisions!.Remove(key))
                return false;
            SaveLocked();
            _version++;
            return true;
        }
    }

    // ASKING

    // A decided host answers at once. Otherwise the local user is asked and the task completes with
    // what they said: Denied at once when nobody can be asked or too many hosts are already waiting,
    // Ignored when the prompt is dismissed or times out. Cancelling takes this caller out of the wait
    // (its task ends cancelled) and leaves the prompt up while anyone else still waits on the host.
    public Task<WebAccessAnswer> AskAsync(string host, string askerName, string worldName, string reason,
        CancellationToken cancel)
    {
        var key = NormalizeHost(host);
        if (key == null)
            return Task.FromResult(WebAccessAnswer.Denied);
        if (cancel.IsCancellationRequested)
            return Task.FromCanceled<WebAccessAnswer>(cancel);

        Pending pending;
        var waiter = new Waiter();
        Pending? next = null;
        lock (_lock)
        {
            EnsureLoaded();
            if (_decisions!.TryGetValue(key, out bool allowed))
                return Task.FromResult(allowed ? WebAccessAnswer.Allowed : WebAccessAnswer.Denied);

            var existing = FindLocked(key);
            if (existing == null)
            {
                if (WaitingCountLocked() >= MaxWaitingHosts || !CanAskLocked())
                    return Task.FromResult(WebAccessAnswer.Denied);
                existing = new Pending(key, new WebAccessQuestion(this, key,
                    Clean(askerName), Clean(worldName), Clean(reason)));
                _queue.Add(existing);
            }
            pending = existing;
            pending.Waiters.Add(waiter);
            if (_showing == null)
                next = TakeNextLocked();
        }

        Present(next);

        // Registered outside the lock: a token cancelled in the meantime runs the callback right here,
        // and the callback takes the lock itself.
        if (cancel.CanBeCanceled)
        {
            var registration = cancel.Register(() => CancelWaiter(pending, waiter, cancel));
            lock (_lock)
            {
                if (waiter.Source.Task.IsCompleted)
                    registration.Unregister();
                else
                    waiter.Registration = registration;
            }
        }
        return waiter.Source.Task;
    }

    internal bool IsShowing(WebAccessQuestion question)
    {
        lock (_lock)
            return _showing != null && ReferenceEquals(_showing.Question, question);
    }

    internal void Answer(WebAccessQuestion question, WebAccessAnswer answer, bool remember)
    {
        if (answer is not (WebAccessAnswer.Allowed or WebAccessAnswer.Denied))
            answer = WebAccessAnswer.Ignored;

        Pending? next;
        lock (_lock)
        {
            var shown = _showing;
            if (shown == null || !ReferenceEquals(shown.Question, question))
                return;
            _showing = null;
            shown.StopTimer();
            if (remember && answer != WebAccessAnswer.Ignored)
            {
                EnsureLoaded();
                _decisions![shown.Host] = answer == WebAccessAnswer.Allowed;
                SaveLocked();
                _version++;
            }
            ResolveLocked(shown, answer);
            next = TakeNextLocked();
        }
        Present(next);
    }

    private void Expire(Pending pending)
    {
        Pending? next;
        lock (_lock)
        {
            if (!ReferenceEquals(_showing, pending))
                return;
            _showing = null;
            pending.StopTimer();
            ResolveLocked(pending, WebAccessAnswer.Ignored);
            next = TakeNextLocked();
        }
        WithdrawSafe(pending.Question);
        Present(next);
    }

    private void CancelWaiter(Pending pending, Waiter waiter, CancellationToken cancel)
    {
        WebAccessQuestion? withdrawn = null;
        Pending? next = null;
        lock (_lock)
        {
            if (!pending.Waiters.Remove(waiter))
                return;
            waiter.Source.TrySetCanceled(cancel);
            if (pending.Waiters.Count > 0)
                return;
            if (ReferenceEquals(_showing, pending))
            {
                _showing = null;
                pending.StopTimer();
                withdrawn = pending.Question;
                next = TakeNextLocked();
            }
            else
            {
                _queue.Remove(pending);
            }
        }
        if (withdrawn != null)
            WithdrawSafe(withdrawn);
        Present(next);
    }

    // Puts the oldest waiting host on screen. Decisions are read again first: a host decided while it
    // waited is answered from that and never shown.
    private Pending? TakeNextLocked()
    {
        EnsureLoaded();
        while (_showing == null && _queue.Count > 0)
        {
            var next = _queue[0];
            _queue.RemoveAt(0);
            if (next.Waiters.Count == 0)
                continue;
            if (_decisions!.TryGetValue(next.Host, out bool allowed))
            {
                ResolveLocked(next, allowed ? WebAccessAnswer.Allowed : WebAccessAnswer.Denied);
                continue;
            }
            _showing = next;
            next.StartTimer(_promptTimeout, () => Expire(next));
            return next;
        }
        return null;
    }

    // Hands the question now on screen to the prompter, outside the lock, because a prompter may
    // answer straight back into this class. One it cannot show at all is a no that is not remembered,
    // and the next host gets its turn.
    private void Present(Pending? pending)
    {
        while (pending != null)
        {
            bool shown;
            try
            {
                shown = _prompter != null && _prompter.Show(pending.Question);
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"WebAccess: the prompt failed to open: {ex.Message}");
                shown = false;
            }
            if (shown)
                return;

            lock (_lock)
            {
                if (!ReferenceEquals(_showing, pending))
                    return;
                _showing = null;
                pending.StopTimer();
                ResolveLocked(pending, WebAccessAnswer.Denied);
                pending = TakeNextLocked();
            }
        }
    }

    private void WithdrawSafe(WebAccessQuestion question)
    {
        try
        {
            _prompter?.Withdraw(question);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"WebAccess: the prompt failed to close: {ex.Message}");
        }
    }

    private bool CanAskLocked()
    {
        if (_prompter == null)
            return false;
        try
        {
            return _prompter.CanAsk;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Pending? FindLocked(string key)
    {
        if (_showing != null && _showing.Host == key)
            return _showing;
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].Host == key)
                return _queue[i];
        }
        return null;
    }

    private int WaitingCountLocked() => _queue.Count + (_showing != null ? 1 : 0);

    private static void ResolveLocked(Pending pending, WebAccessAnswer answer)
    {
        foreach (var waiter in pending.Waiters)
        {
            waiter.Registration.Unregister();
            waiter.Source.TrySetResult(answer);
        }
        pending.Waiters.Clear();
    }

    // Names end up on a prompt. A line break in one could forge a line of that prompt, so control
    // characters become spaces, and the length is capped.
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var builder = new StringBuilder(System.Math.Min(text.Length, MaxPromptText));
        foreach (char c in text)
        {
            if (builder.Length >= MaxPromptText)
                break;
            builder.Append(char.IsControl(c) ? ' ' : c);
        }
        return builder.ToString().Trim();
    }

    // FILE
    //
    // {"version":1,"hosts":{"example.com":"allow","other.example":"deny"}}. Read once, on first use;
    // written whole through a temp file and a move, so a crash mid-write leaves the old file intact. A
    // file that will not parse costs its decisions and nothing else. -xlinka

    private void EnsureLoaded()
    {
        if (_decisions != null)
            return;
        _decisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_path))
                return;
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("hosts", out var hosts)
                || hosts.ValueKind != JsonValueKind.Object)
                return;
            foreach (var entry in hosts.EnumerateObject())
            {
                var key = NormalizeHost(entry.Name);
                if (key == null || entry.Value.ValueKind != JsonValueKind.String)
                    continue;
                switch (entry.Value.GetString())
                {
                    case "allow":
                        _decisions[key] = true;
                        break;
                    case "deny":
                        _decisions[key] = false;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _decisions.Clear();
            LumoraLogger.Warn($"WebAccess: could not read {FileName}, no sites are decided: {ex.Message}");
        }
    }

    private void SaveLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var hosts = new List<string>(_decisions!.Keys);
            hosts.Sort(StringComparer.Ordinal);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", FileVersion);
                writer.WriteStartObject("hosts");
                foreach (var host in hosts)
                    writer.WriteString(host, _decisions[host] ? "allow" : "deny");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, buffer.ToArray());
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            // The decision still stands for this run.
            LumoraLogger.Warn($"WebAccess: could not save {FileName}: {ex.Message}");
        }
    }

    private sealed class Waiter
    {
        public readonly TaskCompletionSource<WebAccessAnswer> Source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration;
    }

    private sealed class Pending
    {
        public readonly string Host;
        public readonly WebAccessQuestion Question;
        public readonly List<Waiter> Waiters = new();
        private Timer? _timer;

        public Pending(string host, WebAccessQuestion question)
        {
            Host = host;
            Question = question;
        }

        public void StartTimer(TimeSpan after, Action expire)
        {
            if (after <= TimeSpan.Zero)
                return;
            _timer = new Timer(static state => ((Action)state!)(), expire, after, Timeout.InfiniteTimeSpan);
        }

        public void StopTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
