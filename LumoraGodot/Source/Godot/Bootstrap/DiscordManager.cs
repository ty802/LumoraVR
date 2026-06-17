// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using DiscordRPC;
using LumoraLogger = Lumora.Core.Logging.Logger;
using LumoraWorld = Lumora.Core.World;

namespace Lumora.Source.Godot.Bootstrap;

/// <summary>
/// Discord Rich Presence: tells your friends which world you're in (name, mode, head-count) and lets
/// them Ask-to-Join public ones. Fails soft - no app ID or no Discord running and it just shrugs,
/// logs once, and gets on with its day.
/// </summary>
public static class DiscordManager
{
    // LumoraVR's Discord application (discord.com/developers). Empty string = presence is off.
    private const string AppId = "1330400493054464092";

    // These MUST match the asset keys uploaded to the app, or Discord shows a sad empty square
    // and quietly judges you.
    private const string LargeImageKey = "lumoravr";
    private const string PublicIconKey = "status_public";
    private const string PrivateIconKey = "status_private";

    /// <summary>Raised on the main thread (via <see cref="Poll"/>) with the session URI to connect to
    /// when a friend joins / their Ask-to-Join is accepted. The bootstrap layer wires this to the join path.</summary>
    public static Action<Uri>? JoinRequested;

    private static DiscordRpcClient? _client;
    private static DateTime _sessionStart;
    private static string _lastPresenceKey = string.Empty;

    public static bool Initialized => _client != null && !_client.IsDisposed;

    public static void Initialize()
    {
        if (string.IsNullOrWhiteSpace(AppId))
        {
            LumoraLogger.Log("DiscordManager: no application ID set - rich presence disabled");
            return;
        }

        try
        {
            _sessionStart = DateTime.UtcNow;
            _client = new DiscordRpcClient(AppId);
            // Required before sending any presence that carries a join secret (Discord errors otherwise),
            // and it's what lets Discord relaunch the game for a cold-launch join. Registers a
            // discord-<appid>:// handler for the current executable (HKCU on Windows, no admin). - xlinka
            _client.RegisterUriScheme();
            _client.OnReady += (_, e) =>
            {
                LumoraLogger.Log($"DiscordManager: connected as {e.User.Username}");
                // Tell Discord we actually care about Join + Ask-to-Join events. Skip this and the
                // "Join" button is purely decorative. - xlinka
                _client?.SetSubscription(EventType.Join | EventType.JoinRequest);
            };
            _client.OnError += (_, e) => LumoraLogger.Warn($"DiscordManager: error {e.Code}: {e.Message}");
            _client.OnJoin += HandleJoin;
            _client.OnJoinRequested += HandleJoinRequest;
            _client.Initialize();
            LumoraLogger.Log("DiscordManager: rich presence initialized");
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"DiscordManager: init exception: {ex.Message}");
            _client = null;
        }
    }

    /// <summary>Pump the client's event queue (ready/error/join callbacks). Call from the update loop.</summary>
    public static void Poll()
    {
        if (_client != null && !_client.IsDisposed)
            _client.Invoke();
    }

    // A friend hit Join (or got their ask-to-join accepted). The "secret" is the session URI we
    // stashed in the presence - hand it to the bootstrap layer to actually go there.
    private static void HandleJoin(object sender, DiscordRPC.Message.JoinMessage args)
    {
        try
        {
            var uri = new Uri(args.Secret);
            LumoraLogger.Log($"DiscordManager: join -> {uri}");
            JoinRequested?.Invoke(uri);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"DiscordManager: ignoring bad join secret '{args.Secret}': {ex.Message}");
        }
    }

    // Someone politely knocked (Ask to Join). We only ever attach a join secret to PUBLIC worlds, so
    // if they got this far it's open house - wave them in. (Per-request prompts can come later.)
    private static void HandleJoinRequest(object sender, DiscordRPC.Message.JoinRequestMessage args)
    {
        LumoraLogger.Log($"DiscordManager: {args.User.Username} asked to join - opening the door");
        _client?.Respond(args, true);
    }

    /// <summary>
    /// Push presence for the focused world. De-duped: only re-sends when the resulting state actually
    /// changes, so it's cheap to call every frame/tick.
    /// </summary>
    public static void UpdatePresence(LumoraWorld? world)
    {
        if (_client == null || _client.IsDisposed)
            return;

        try
        {
            var presence = BuildPresence(world, out var key);
            if (key == _lastPresenceKey)
                return;
            _lastPresenceKey = key;
            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"DiscordManager: presence update failed: {ex.Message}");
        }
    }

    private static RichPresence BuildPresence(LumoraWorld? world, out string key)
    {
        string version = Lumora.Core.EngineVersion.VersionString;

        if (world == null)
        {
            key = "idle";
            return new RichPresence
            {
                Details = "In Userspace",
                State = "Idle",
                Timestamps = new Timestamps(_sessionStart),
                Assets = new Assets
                {
                    LargeImageKey = LargeImageKey,
                    LargeImageText = $"LumoraVR {version}",
                },
            };
        }

        string name = Trim(world.WorldName?.Value ?? world.Name, 120);
        var mode = world.Mode;
        string modeLabel = mode switch
        {
            Lumora.Core.WorldMode.Social => "Social",
            Lumora.Core.WorldMode.Event => "Event",
            _ => "Builder",
        };
        int users = world.UserCount;
        int max = world.Configuration?.MaxUsers?.Value ?? 0;
        bool isPublic = world.Configuration?.IsPublic?.Value ?? false;

        var presence = new RichPresence
        {
            Timestamps = new Timestamps(_sessionStart),
            Assets = new Assets
            {
                LargeImageKey = LargeImageKey,
                LargeImageText = $"LumoraVR {version}",
                SmallImageKey = isPublic ? PublicIconKey : PrivateIconKey,
                SmallImageText = isPublic ? "Public world" : "Private world",
            },
        };

        // Public worlds show their name; private (invite-only) worlds don't leak it.
        presence.Details = isPublic ? Trim(name, 120) : "In a private world";
        presence.State = $"{modeLabel} world";

        // Any hosted session with a reachable URL is INVITABLE (the Discord "+ Invite" works) - that's
        // the whole point of invite-only. Privacy decides who can ALSO ask-to-join: public = anyone,
        // private = invite recipients only (no Ask-to-Join button shown to strangers). - xlinka
        var sessionId = world.SessionID?.Value;
        var joinUri = BestJoinUri(world);
        bool joinable = !string.IsNullOrEmpty(sessionId) && joinUri != null;

        if (joinable)
        {
            presence.Party = new Party
            {
                ID = sessionId!,
                Size = Math.Max(1, users),
                Max = max > 0 ? max : Math.Max(2, users + 1),
                Privacy = isPublic ? Party.PrivacySetting.Public : Party.PrivacySetting.Private,
            };
            presence.Secrets = new Secrets { JoinSecret = Trim(joinUri!.ToString(), 120) };
        }

        key = $"{(isPublic ? "pub" : "priv")}|{(isPublic ? name : "")}|{modeLabel}|{users}/{max}|{(joinable ? "j" : "-")}";
        return presence;
    }

    private static string Trim(string? s, int max)
    {
        s ??= string.Empty;
        return s.Length <= max ? s : s.Substring(0, max);
    }

    // Pick a connectable session URL for the join secret. Skip 0.0.0.0/:: (bind-any addresses that
    // mean "everything" to the host and "nothing" to a joiner).
    private static Uri? BestJoinUri(LumoraWorld world)
    {
        var urls = world.SessionURLs;
        if (urls == null || urls.Count == 0)
            return null;
        foreach (var u in urls)
        {
            if (u != null && u.Host != "0.0.0.0" && u.Host != "::")
                return u;
        }
        return urls[0];
    }

    public static void Shutdown()
    {
        if (_client == null)
            return;
        try
        {
            if (!_client.IsDisposed)
            {
                _client.ClearPresence();
                _client.Dispose();
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"DiscordManager: shutdown exception: {ex.Message}");
        }
        _client = null;
        _lastPresenceKey = string.Empty;
        LumoraLogger.Log("DiscordManager: shutdown");
    }
}
