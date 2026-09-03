// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Network;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// What a link points at. A session is a place someone is hosting right now, a saved world is a file
// on one machine.
public enum WorldLinkKind
{
    Session,
    SavedWorld,
}

// A destination, carried as an object in the world. The orb and the portal are the two shapes it
// takes; everything about WHERE the thing goes and whether you may go there lives here so both
// shapes answer the same way.
//
// The fields replicate because the object is shared: you hand somebody an orb, or drop a portal in a
// room, and their copy has to know the same address yours does. Opening it does NOT replicate. Open()
// runs on the local user alone, which is the whole point - a portal that took everyone standing near
// it somewhere would be a weapon, not a door.
//
// A saved world is the one destination that is not portable. The file exists on exactly one machine,
// so the link stamps the machine name at spawn and refuses anywhere else rather than handing a peer a
// path that resolves to nothing (or, worse, to a different file with the same name). -xlinka
public abstract class WorldLink : Component
{
    public readonly Sync<WorldLinkKind> Kind;

    public readonly Sync<string> DisplayName;

    // Session
    public readonly Sync<Uri> JoinUrl;
    public readonly Sync<string> SessionId;

    // SavedWorld
    public readonly Sync<string> SavedWorldPath;
    public readonly Sync<string> HostMachine;

    public readonly Sync<WorldMode> Mode;

    public readonly Sync<int> ActiveUsers;
    public readonly Sync<int> MaxUsers;

    // A local:// asset URL, saved out of whatever picture the announcement carried.
    public readonly Sync<Uri> ThumbnailUrl;

    // Why the last Open() refused, for the visual to show. Local, never replicated: the refusal is
    // about THIS machine (wrong machine for a saved file, a join already in flight), so a shared
    // reason would be a lie on every other peer.
    public string? LastRefusal { get; protected set; }

    private const float UserPollInterval = 2f;
    private float _userPollCountdown;
    private bool _thumbnailPending;

    protected WorldLink()
    {
        Kind = new Sync<WorldLinkKind>(this, WorldLinkKind.Session);
        DisplayName = new Sync<string>(this, string.Empty);
        JoinUrl = new Sync<Uri>(this, null!);
        SessionId = new Sync<string>(this, string.Empty);
        SavedWorldPath = new Sync<string>(this, string.Empty);
        HostMachine = new Sync<string>(this, string.Empty);
        Mode = new Sync<WorldMode>(this, WorldMode.Builder);
        ActiveUsers = new Sync<int>(this, 0);
        MaxUsers = new Sync<int>(this, 0);
        ThumbnailUrl = new Sync<Uri>(this, null!);
    }

    // GATES

    // Whether an item may be brought into this world at all. Same two questions the in-world
    // dispensers ask (GrabSpawnerBase.DescribeBlock): the mode ceiling first, then the role cap. Both
    // bind, and the mode answer stands even where the gate is switched off.
    public static bool CanSpawnIn(World? into, out string? reason)
    {
        if (into == null || into.IsDestroyed)
        {
            reason = "no world";
            return false;
        }
        if (!into.AllowsItemSpawning)
        {
            reason = "spawning is off in this world";
            return false;
        }
        if (into.DataModelPermissions?.AllowsDomain(into.LocalUser, DataModelPermissionDomain.Spawn) == false)
        {
            reason = "you may not spawn items in this world";
            return false;
        }
        reason = null;
        return true;
    }

    // Whether a link to this open world is worth handing to anybody else. A private session has no
    // door, and a session that has not published an address yet has nothing to write on the link.
    public static bool CanShareSession(World? target, out string? reason)
    {
        if (target == null || target.IsDestroyed)
        {
            reason = "no world";
            return false;
        }
        if (target.Configuration?.AccessLevel.Value == World.WorldAccessLevel.Private)
        {
            reason = "private session, nobody else can join it (change Who Can Join in Session settings)";
            return false;
        }
        if (target.SessionURLs.Count == 0)
        {
            reason = "no join address yet";
            return false;
        }
        reason = null;
        return true;
    }

    // A browser row is already a published session, so the address it came with is the whole answer.
    public static bool CanShareSession(SessionListEntry? entry, out string? reason)
    {
        if (entry?.JoinUrl == null)
        {
            reason = "no join address yet";
            return false;
        }
        reason = null;
        return true;
    }

    // FILL

    public void FillFrom(World target)
    {
        if (target == null || target.IsDestroyed)
            return;

        Kind.Value = WorldLinkKind.Session;
        DisplayName.Value = string.IsNullOrEmpty(target.WorldName?.Value) ? target.Name : target.WorldName!.Value;
        SessionId.Value = target.SessionID?.Value ?? string.Empty;
        JoinUrl.Value = target.SessionURLs.Count > 0 ? target.SessionURLs[0] : null!;
        Mode.Value = target.Mode;
        ActiveUsers.Value = target.UserCount;
        MaxUsers.Value = target.Configuration?.MaxUsers.Value ?? 0;
        OfferThumbnail(target.Session?.Metadata?.ThumbnailBase64, null);
    }

    public void FillFrom(SessionListEntry entry)
    {
        if (entry == null)
            return;

        Kind.Value = WorldLinkKind.Session;
        DisplayName.Value = string.IsNullOrEmpty(entry.Name) ? (entry.JoinUrl?.Host ?? "Session") : entry.Name;
        SessionId.Value = entry.SessionId ?? string.Empty;
        // A row with no address is a session nobody can reach; the field carries that as null, which
        // is what its own default is.
        JoinUrl.Value = entry.JoinUrl!;
        ActiveUsers.Value = entry.ActiveUsers;
        MaxUsers.Value = entry.MaxUsers;
        if (UI.Worlds.WorldGroup.TryReadMode(entry.Tags, out var mode))
            Mode.Value = mode;
        OfferThumbnail(entry.ThumbnailBase64, entry.ThumbnailUrl);
    }

    // Take everything one link knows over to another: a portal dropped from an orb points at exactly
    // the same place, thumbnail included.
    public void CopyFrom(WorldLink other)
    {
        if (other == null || other.IsDestroyed)
            return;

        Kind.Value = other.Kind.Value;
        DisplayName.Value = other.DisplayName.Value;
        JoinUrl.Value = other.JoinUrl.Value;
        SessionId.Value = other.SessionId.Value;
        SavedWorldPath.Value = other.SavedWorldPath.Value;
        HostMachine.Value = other.HostMachine.Value;
        Mode.Value = other.Mode.Value;
        ActiveUsers.Value = other.ActiveUsers.Value;
        MaxUsers.Value = other.MaxUsers.Value;
        ThumbnailUrl.Value = other.ThumbnailUrl.Value;
    }

    public void FillSaved(string path)
    {
        Kind.Value = WorldLinkKind.SavedWorld;
        SavedWorldPath.Value = path ?? string.Empty;
        // The file is on THIS machine and nowhere else. Stamped once, at spawn.
        HostMachine.Value = Environment.MachineName;
        DisplayName.Value = string.IsNullOrEmpty(path)
            ? "Saved world"
            : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    // STATE

    // The open world this link points at, or null. Sessions match on id; a saved world is a file
    // rather than a place, so it has none.
    public World? OpenWorld
    {
        get
        {
            if (Kind.Value != WorldLinkKind.Session)
                return null;
            string id = SessionId.Value;
            if (string.IsNullOrEmpty(id))
                return null;
            var worlds = Engine.Current?.WorldManager?.Worlds;
            if (worlds == null)
                return null;
            for (int i = 0; i < worlds.Count; i++)
            {
                var world = worlds[i];
                if (world != null && !world.IsDestroyed
                    && string.Equals(world.SessionID?.Value, id, StringComparison.Ordinal))
                    return world;
            }
            return null;
        }
    }

    public bool IsLocalWorld => OpenWorld != null;

    public bool IsFocused
    {
        get
        {
            var open = OpenWorld;
            return open != null && ReferenceEquals(open, Engine.Current?.WorldManager?.FocusedWorld);
        }
    }

    // The saved file this link names exists and belongs to this machine.
    public bool SavedFileIsHere
    {
        get
        {
            if (Kind.Value != WorldLinkKind.SavedWorld)
                return false;
            if (!string.Equals(HostMachine.Value, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return false;
            string path = SavedWorldPath.Value;
            if (string.IsNullOrEmpty(path))
                return false;
            try
            {
                return System.IO.File.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }

    // OPEN

    // Takes the LOCAL user to the destination. Never replicated, never run for anybody else.
    public bool Open()
    {
        LastRefusal = null;
        switch (Kind.Value)
        {
            case WorldLinkKind.Session: return OpenSession();
            case WorldLinkKind.SavedWorld: return OpenSaved();
            default: return false;
        }
    }

    private bool OpenSession()
    {
        var manager = Engine.Current?.WorldManager;
        if (manager == null)
        {
            LastRefusal = "not ready";
            return false;
        }

        // Already open here: this is a focus change, not a join. Re-joining a session you are standing
        // in would build a second world for the same place.
        var open = OpenWorld;
        if (open != null)
        {
            manager.FocusWorld(open);
            return true;
        }

        var url = JoinUrl.Value;
        if (url == null)
        {
            LastRefusal = "no join address";
            return false;
        }

        // One at a time, same guard the dash browser uses: the loading service holds you where you are
        // until a join is ready, and a second join on top of a live one has nowhere to put the user.
        var loader = Engine.Current?.WorldLoadingService;
        if (loader != null && loader.IsLoading)
        {
            LastRefusal = "already joining a world";
            return false;
        }

        string name = string.IsNullOrEmpty(DisplayName.Value) ? url.Host : DisplayName.Value;
        if (loader != null)
        {
            loader.JoinSessionAsync(name, url, focusWhenReady: true);
            return true;
        }

        ushort port = url.Port > 0 ? (ushort)url.Port : (ushort)0;
        var world = manager.JoinSession(name, url.Host, port);
        if (world == null)
        {
            LastRefusal = "join failed";
            return false;
        }
        manager.SwitchToWorld(world);
        return true;
    }

    private bool OpenSaved()
    {
        if (!string.Equals(HostMachine.Value, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            LastRefusal = "saved on " + (string.IsNullOrEmpty(HostMachine.Value) ? "another machine" : HostMachine.Value);
            return false;
        }
        if (!SavedFileIsHere)
        {
            LastRefusal = "file is gone";
            return false;
        }

        var manager = Engine.Current?.WorldManager;
        var opened = manager?.OpenSavedWorld(SavedWorldPath.Value);
        if (opened == null)
        {
            LastRefusal = "could not open the file";
            return false;
        }
        return true;
    }

    // THUMBNAIL

    // A picture arrives either inline as base64 (a machine on the same network announced it) or as an
    // address (the directory published it). Bytes go to the local asset store off the world thread and
    // come back as a local:// URL an ImageProvider can load; an address is already a URL and needs
    // none of that.
    protected void OfferThumbnail(string? base64, string? url)
    {
        if (ThumbnailUrl.Value != null || _thumbnailPending)
            return;

        if (string.IsNullOrEmpty(base64))
        {
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var direct))
                ThumbnailUrl.Value = direct;
            return;
        }

        _thumbnailPending = true;
        string encoded = base64!;
        // Decode and the disk write go to the background; the field write comes back to the world
        // thread, because ThumbnailUrl is a datamodel member like any other.
        StartTask(async () =>
        {
            string? saved = null;
            try
            {
                await WorldContext.ToBackground();
                var bytes = Convert.FromBase64String(encoded);
                var db = Engine.Current?.LocalDB;
                if (bytes.Length > 0 && db != null)
                    saved = await db.SaveAssetAsync(bytes, ".jpg").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"WorldLink: couldn't prepare a thumbnail: {ex.Message}");
            }

            await WorldContext.ToWorld();
            _thumbnailPending = false;
            if (IsDestroyed || string.IsNullOrEmpty(saved))
                return;
            ThumbnailUrl.Value = new Uri(saved!);
        });
    }

    // LIVE COUNT

    // Sessions grow and empty while the link sits there, so the counts on it are refreshed from the
    // browser. Only the authority writes: these are replicated fields, and a guest writing them is
    // refused by the permission gate anyway - one writer, every peer receives the result.
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        if (Kind.Value != WorldLinkKind.Session || World?.IsAuthority != true)
            return;

        _userPollCountdown -= World.Time.Delta;
        if (_userPollCountdown > 0f)
            return;
        _userPollCountdown = UserPollInterval;

        string id = SessionId.Value;
        if (string.IsNullOrEmpty(id))
            return;

        // An open world is its own best source; nothing on the network knows it better than we do.
        var open = OpenWorld;
        if (open != null)
        {
            WriteCounts(open.UserCount, open.Configuration?.MaxUsers.Value ?? MaxUsers.Value);
            return;
        }

        var browser = FindBrowser();
        var sessions = browser?.GetSessions();
        if (sessions == null)
            return;
        for (int i = 0; i < sessions.Count; i++)
        {
            var entry = sessions[i];
            if (!string.Equals(entry.SessionId, id, StringComparison.Ordinal))
                continue;
            WriteCounts(entry.ActiveUsers, entry.MaxUsers);
            return;
        }
    }

    private void WriteCounts(int active, int max)
    {
        if (ActiveUsers.Value != active)
            ActiveUsers.Value = active;
        if (max > 0 && MaxUsers.Value != max)
            MaxUsers.Value = max;
    }

    // The browser lives on the userspace root, one per client. Read only from here: this runs inside a
    // session world's update, and attaching to another world from there is not this component's call.
    // The Worlds screen creates and starts it; until then the counts simply stay as they were.
    private static SessionBrowser? FindBrowser()
    {
        var root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;
        return root.GetComponent<SessionBrowser>();
    }

    // DESCRIPTION

    // The one line under the name. Sessions count heads, a saved world says where it lives when that
    // is not here.
    public string DescribeState()
    {
        switch (Kind.Value)
        {
            case WorldLinkKind.SavedWorld:
                return SavedFileIsHere
                    ? "saved world"
                    : "on " + (string.IsNullOrEmpty(HostMachine.Value) ? "another machine" : HostMachine.Value);
            default:
                int users = ActiveUsers.Value;
                return users == 1 ? "1 user" : users + " users";
        }
    }

    // The colour of the destination, for whatever is drawing it. Focused is where you are standing,
    // open is a world already running on this machine, a session with people in it reads warmer than
    // an empty one, and a saved world you cannot reach reads as a refusal.
    public colorHDR StateColor()
    {
        if (Kind.Value == WorldLinkKind.SavedWorld)
            return SavedFileIsHere ? Clear : Red;
        if (IsFocused)
            return Cyan;
        if (IsLocalWorld)
            return Orange;
        if (JoinUrl.Value == null)
            return Clear;
        return ActiveUsers.Value > 0 ? Pink : Purple;
    }

    public colorHDR ModeColor()
    {
        var tint = UI.DashTheme.ModeTint(Mode.Value);
        return new colorHDR(tint.r, tint.g, tint.b, 1f);
    }

    protected static readonly colorHDR Clear = new colorHDR(0f, 0f, 0f, 0f);
    protected static readonly colorHDR Cyan = new colorHDR(0f, 0.85f, 1f, 1f);
    protected static readonly colorHDR Orange = new colorHDR(1f, 0.55f, 0.1f, 1f);
    protected static readonly colorHDR Pink = new colorHDR(1f, 0f, 0.5f, 1f);
    protected static readonly colorHDR Purple = new colorHDR(0.5f, 0f, 1f, 1f);
    protected static readonly colorHDR Red = new colorHDR(1f, 0.15f, 0.15f, 1f);
    protected static readonly colorHDR Yellow = new colorHDR(1f, 0.9f, 0.2f, 1f);
}
