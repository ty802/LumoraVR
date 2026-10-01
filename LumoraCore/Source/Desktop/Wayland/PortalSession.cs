// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Desktop;

// A screen-share session with the desktop portal: the compositor asks the user once which monitors to
// share, then hands over a PipeWire stream per monitor and takes input for them.
//
// Remote desktop (picture plus input) where the portal offers it; where that is missing or breaks, screen
// cast (picture only). A no from the user is a no to both. The approval is remembered: the portal gives a
// restore token with every session, kept in the settings, and the next session presents it instead of
// asking again.
//
// Every portal request answers twice: the call returns a request path at once, the result comes later as
// a Response signal on that path. The path is predictable from our bus name and a token we choose, so the
// signal is listened for before the call goes out and cannot be missed. Signals are only taken from the
// bus name that owns the portal: any program on the same bus could otherwise answer in its place. -xlinka
[SupportedOSPlatform("linux")]
internal sealed class PortalSession : IDisposable
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string RemoteDesktop = "org.freedesktop.portal.RemoteDesktop";
    private const string ScreenCast = "org.freedesktop.portal.ScreenCast";
    private const uint DeviceKeyboard = 1, DevicePointer = 2;
    private const uint SourceMonitor = 1;
    private const uint CursorEmbedded = 2;
    private const uint PersistUntilRevoked = 2;
    private const uint AnswerSuccess = 0, AnswerCancelled = 1, AnswerOther = 2;
    private static readonly TimeSpan ApprovalWait = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestWait = TimeSpan.FromSeconds(30);

    // Serial is the PipeWire object serial the portal names the stream by (0 where it gives only a node).
    public sealed record Stream(uint Node, ulong Serial, DesktopDisplay Display);

    private readonly DBusConnection _bus;
    private readonly string _requestPrefix;
    private string _owner = string.Empty;
    private int _tokens;
    private string _session = string.Empty;

    private PortalSession(DBusConnection bus)
    {
        _bus = bus;
        _requestPrefix = "/org/freedesktop/portal/desktop/request/" + bus.UniqueName.TrimStart(':').Replace('.', '_') + "/";
    }

    public bool CanPoint { get; private set; }
    public bool CanType { get; private set; }
    public IReadOnlyList<Stream> Streams { get; private set; } = Array.Empty<Stream>();
    public bool IsOpen { get; private set; }

    // Raised when the compositor or the user ends the session, on the bus's reader thread.
    public event Action? Closed;

    // Asks for the session and waits, on the calling thread, for the user to answer. Null with a reason
    // when there is no portal, no answer, or a no.
    public static PortalSession? Start(string restoreToken, Action<string> saveToken, out string? problem)
    {
        problem = null;
        foreach (bool remote in new[] { true, false })
        {
            var bus = DBusConnection.OpenSession();
            if (bus == null)
            {
                problem = "No session bus to ask the desktop for its screen.";
                return null;
            }
            var session = new PortalSession(bus);
            uint answer = AnswerOther;
            try
            {
                answer = session.Open(remote, restoreToken, saveToken, out problem);
                if (answer == AnswerSuccess)
                    return session;
            }
            catch (Exception ex)
            {
                problem = "The desktop portal failed: " + ex.Message;
                LumoraLogger.Warn($"PortalSession: {ex}");
            }
            session.Dispose();
            if (answer == AnswerCancelled)
                return null;
        }
        return null;
    }

    private uint Open(bool remote, string restoreToken, Action<string> saveToken, out string? problem)
    {
        problem = null;
        uint remoteVersion = VersionOf(RemoteDesktop), castVersion = VersionOf(ScreenCast);
        if (remote && remoteVersion < 1)
            return AnswerOther;
        if (castVersion < 1)
        {
            problem = "This desktop has no screen-share portal.";
            return AnswerOther;
        }
        var owner = _bus.TryCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner", "s", Service);
        _owner = owner.Type == DBusMessage.MethodReturn && owner.Body.Length > 0 ? owner.Body[0] as string ?? string.Empty : string.Empty;
        if (_owner.Length == 0)
        {
            problem = "The desktop portal is not running.";
            return AnswerOther;
        }

        string iface = remote ? RemoteDesktop : ScreenCast;
        bool persist = remote ? remoteVersion >= 2 : castVersion >= 4;

        var created = Request(iface, "CreateSession", "a{sv}", RequestWait, Options(("session_handle_token", new DBusVariant("s", NextToken()))));
        if (created.Code != AnswerSuccess)
            return Fail(created, "start a screen share", out problem);
        _session = created.Results.TryGetValue("session_handle", out var handle) ? handle.ToString() ?? string.Empty : string.Empty;
        if (_session.Length == 0)
        {
            problem = "The desktop portal gave no session.";
            return AnswerOther;
        }
        IsOpen = true;
        _bus.OnSignal(_session, "org.freedesktop.portal.Session", "Closed", _ =>
        {
            IsOpen = false;
            Closed?.Invoke();
        }, Service, _owner);

        var keep = new List<(string, DBusVariant)>();
        if (persist)
        {
            keep.Add(("persist_mode", new DBusVariant("u", PersistUntilRevoked)));
            if (!string.IsNullOrEmpty(restoreToken))
                keep.Add(("restore_token", new DBusVariant("s", restoreToken)));
        }

        if (remote)
        {
            var devices = new List<(string, DBusVariant)> { ("types", new DBusVariant("u", DeviceKeyboard | DevicePointer)) };
            devices.AddRange(keep);
            var selected = Request(RemoteDesktop, "SelectDevices", "oa{sv}", RequestWait, new DBusObjectPath(_session), Options(devices.ToArray()));
            if (selected.Code != AnswerSuccess)
                return Fail(selected, "choose input devices", out problem);
        }

        var sources = new List<(string, DBusVariant)>
        {
            ("types", new DBusVariant("u", SourceMonitor)),
            ("multiple", new DBusVariant("b", true)),
        };
        // A cursor mode the portal does not list is refused outright; without one the cursor is left out.
        if (castVersion >= 2 && (PropertyOf(ScreenCast, "AvailableCursorModes") & CursorEmbedded) != 0)
            sources.Add(("cursor_mode", new DBusVariant("u", CursorEmbedded)));
        if (!remote)
            sources.AddRange(keep);
        var chosen = Request(ScreenCast, "SelectSources", "oa{sv}", RequestWait, new DBusObjectPath(_session), Options(sources.ToArray()));
        if (chosen.Code != AnswerSuccess)
            return Fail(chosen, "choose monitors", out problem);

        var started = Request(iface, "Start", "osa{sv}", ApprovalWait, new DBusObjectPath(_session), "", Options());
        if (started.Code != AnswerSuccess)
            return Fail(started, "share the screen", out problem);
        if (started.Results.TryGetValue("restore_token", out var token) && token is string text && text.Length > 0)
            saveToken(text);
        uint granted = remote && started.Results.TryGetValue("devices", out var bits) && bits is uint value ? value : 0;
        CanPoint = (granted & DevicePointer) != 0;
        CanType = (granted & DeviceKeyboard) != 0;
        Streams = ReadStreams(started.Results);
        if (Streams.Count == 0)
        {
            problem = "No monitor was shared.";
            return AnswerOther;
        }
        return AnswerSuccess;
    }

    private static uint Fail(Answer answer, string what, out string? problem)
    {
        problem = answer.Code == AnswerCancelled
            ? $"Screen sharing was declined when asked to {what}."
            : $"The desktop could not {what}.";
        return answer.Code == AnswerCancelled ? AnswerCancelled : AnswerOther;
    }

    // The fd PipeWire is reached through, one per caller; the caller owns it.
    public int OpenPipeWire()
    {
        var reply = _bus.Call(Service, ObjectPath, ScreenCast, "OpenPipeWireRemote", "oa{sv}", new DBusObjectPath(_session), Options());
        return reply.Fds.Length > 0 ? reply.Fds[0] : -1;
    }

    // INPUT. Coordinates are the stream's own, in the compositor's logical pixels. The portal refuses
    // what was not granted, and with no reply asked for that refusal would go unheard, so ungranted
    // input is not sent at all.

    public void PointerTo(uint node, double x, double y)
    {
        if (CanPoint)
            Notify("NotifyPointerMotionAbsolute", "oa{sv}udd", node, x, y);
    }

    public void PointerButton(int evdevButton, bool down)
    {
        if (CanPoint)
            Notify("NotifyPointerButton", "oa{sv}iu", evdevButton, down ? 1u : 0u);
    }

    public void PointerAxis(bool horizontal, int steps)
    {
        if (CanPoint)
            Notify("NotifyPointerAxisDiscrete", "oa{sv}ui", horizontal ? 1u : 0u, steps);
    }

    public void Keysym(uint keysym, bool down)
    {
        if (CanType)
            Notify("NotifyKeyboardKeysym", "oa{sv}iu", (int)keysym, down ? 1u : 0u);
    }

    private void Notify(string member, string signature, params object[] rest)
    {
        if (!IsOpen)
            return;
        var args = new object[rest.Length + 2];
        args[0] = new DBusObjectPath(_session);
        args[1] = Options();
        rest.CopyTo(args, 2);
        _bus.CallNoReply(Service, ObjectPath, RemoteDesktop, member, signature, args);
    }

    // PLUMBING

    private readonly record struct Answer(uint Code, Dictionary<object, object> Results);

    // The options dictionary is always the last argument, and the request's handle token is added to it.
    private Answer Request(string iface, string member, string signature, TimeSpan wait, params object[] args)
    {
        string token = NextToken();
        ((Dictionary<string, DBusVariant>)args[^1])["handle_token"] = new DBusVariant("s", token);
        using var answered = new ManualResetEventSlim(false);
        Answer answer = new(AnswerOther, new Dictionary<object, object>());
        void OnResponse(DBusMessage message)
        {
            if (message.Body.Length >= 2 && message.Body[0] is uint code && message.Body[1] is Dictionary<object, object> results)
                answer = new Answer(code, results);
            answered.Set();
        }
        using (_bus.OnSignal(_requestPrefix + token, "org.freedesktop.portal.Request", "Response", OnResponse, Service, _owner))
        {
            var reply = _bus.Call(Service, ObjectPath, iface, member, signature, args);
            // An old portal can answer on a path of its own choosing.
            string path = reply.Body.Length > 0 ? reply.Body[0]?.ToString() ?? string.Empty : string.Empty;
            using var other = path.Length > 0 && path != _requestPrefix + token
                ? _bus.OnSignal(path, "org.freedesktop.portal.Request", "Response", OnResponse, Service, _owner)
                : null;
            // Woken by the answer, or by the bus going away, which means no answer is coming.
            if (WaitHandle.WaitAny(new[] { answered.WaitHandle, _bus.Gone }, wait) != 0)
                return new Answer(AnswerOther, new Dictionary<object, object>());
        }
        return answer;
    }

    private static Dictionary<string, DBusVariant> Options(params (string Key, DBusVariant Value)[] entries)
    {
        var options = new Dictionary<string, DBusVariant>();
        foreach (var (key, value) in entries)
            options[key] = value;
        return options;
    }

    private string NextToken() => "lumora" + Environment.ProcessId + "_" + Interlocked.Increment(ref _tokens);

    private uint VersionOf(string iface) => PropertyOf(iface, "version");

    private uint PropertyOf(string iface, string property)
    {
        var reply = _bus.TryCall(Service, ObjectPath, "org.freedesktop.DBus.Properties", "Get", "ss", iface, property);
        return reply.Type == DBusMessage.MethodReturn && reply.Body.Length > 0 && reply.Body[0] is uint value ? value : 0;
    }

    // Size and position are both optional in the answer. A stream without a size cannot be told apart
    // from a broken one; one without a position is laid out beside the others, so each still has a
    // place of its own for input to be sent to.
    private static IReadOnlyList<Stream> ReadStreams(Dictionary<object, object> results)
    {
        var streams = new List<Stream>();
        if (!results.TryGetValue("streams", out var raw) || raw is not List<object> list)
            return streams;
        int index = 0, beside = 0;
        foreach (var item in list)
        {
            if (item is not object[] entry || entry.Length < 2 || entry[0] is not uint node || entry[1] is not Dictionary<object, object> properties)
                continue;
            var (w, h) = Pair(properties, "size");
            if (w <= 0 || h <= 0)
                continue;
            bool placed = properties.ContainsKey("position");
            var (x, y) = placed ? Pair(properties, "position") : (beside, 0);
            beside = System.Math.Max(beside, x + w);
            ulong serial = properties.TryGetValue("pipewire-serial", out var s) && s is ulong value ? value : 0;
            streams.Add(new Stream(node, serial, new DesktopDisplay(0, index, "Monitor " + (index + 1), x, y, w, h, index == 0)));
            index++;
        }
        return streams;
    }

    private static (int, int) Pair(Dictionary<object, object> properties, string key)
        => properties.TryGetValue(key, out var value) && value is object[] pair && pair.Length == 2 && pair[0] is int a && pair[1] is int b ? (a, b) : (0, 0);

    public void Dispose()
    {
        if (IsOpen)
        {
            IsOpen = false;
            try
            {
                _bus.TryCall(Service, _session, "org.freedesktop.portal.Session", "Close", "");
            }
            catch (Exception)
            {
            }
        }
        _bus.Dispose();
    }
}
