// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
// Steam is a desktop-only transport. The Steamworks binding has no Android build, and a type that
// merely MENTIONS a Steamworks type cannot be loaded there: the whole engine init failed on a Pico 4
// with 'Could not load type of field SteamNetworkManager:_listenerMap', long before any runtime
// 'is this Android' check could run. Compile the transport out on mobile entirely. -xlinka
#if GODOT_PC

using System;
using Steamworks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Bootstrap;

public static class SteamManager
{
    private const uint AppId = 4237370;

    public static bool Initialized { get; private set; }

    // Always true now; false would mean the caller has to quit (kept for a relaunch step, should one return).
    //
    // The app id goes in through the environment, the way the Steam client hands it to a game it launches,
    // so a build started straight from its folder (the zips testers get) reaches Steam as well. A
    // steam_appid.txt is only read from the working directory, which an export does not control, and the
    // relaunch-through-Steam step that used to sit here closed every such build the moment it started. -xlinka
    public static bool Initialize()
    {
        try
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId")))
            {
                global::Godot.OS.SetEnvironment("SteamAppId", AppId.ToString());
                global::Godot.OS.SetEnvironment("SteamGameId", AppId.ToString());
            }

            Initialized = SteamAPI.Init();

            if (!Initialized)
                LumoraLogger.Warn("SteamManager: SteamAPI.Init() failed - Steam may not be running");
            else
            {
                LumoraLogger.Log($"SteamManager: Initialized (AppId={AppId}, User={SteamFriends.GetPersonaName()})");
                SteamPhotoLibrary.Initialize();
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"SteamManager: Init exception: {ex.Message}");
        }

        return true;
    }

    public static void RunCallbacks()
    {
        if (Initialized) SteamAPI.RunCallbacks();
    }

    public static void Shutdown()
    {
        if (!Initialized) return;
        SteamPhotoLibrary.Shutdown();
        SteamAPI.Shutdown();
        Initialized = false;
        LumoraLogger.Log("SteamManager: Shutdown");
    }
}
#endif
