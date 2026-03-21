// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Steamworks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Bootstrap;

public static class SteamManager
{
    private const uint AppId = 4237370;

    public static bool Initialized { get; private set; }

    /// <summary>
    /// Initialize Steam. Returns false if Steam is relaunching the app — caller must quit immediately.
    /// </summary>
    public static bool Initialize()
    {
        try
        {
            if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
            {
                LumoraLogger.Log("SteamManager: Relaunching via Steam client...");
                return false;
            }

            Initialized = SteamAPI.Init();

            if (!Initialized)
                LumoraLogger.Warn("SteamManager: SteamAPI.Init() failed — Steam may not be running");
            else
                LumoraLogger.Log($"SteamManager: Initialized (AppId={AppId}, User={SteamFriends.GetPersonaName()})");
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
        SteamAPI.Shutdown();
        Initialized = false;
        LumoraLogger.Log("SteamManager: Shutdown");
    }
}
