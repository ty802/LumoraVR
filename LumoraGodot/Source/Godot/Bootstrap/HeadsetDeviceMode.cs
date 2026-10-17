// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Godot;

namespace Lumora.Source.Godot.Bootstrap;

// This describes the machine running Lumora, not a headset streaming from it. A Frame controller
// profile (or SteamVR runtime name) alone says nothing about where rendering takes place.
public static class HeadsetDeviceMode
{
    private static bool? _steamFrame;
    public static bool IsSteamFrame => _steamFrame ??= DetectSteamFrame();
    public static bool IsHeadset => AndroidDeviceMode.IsHeadset || IsSteamFrame;

    private static bool DetectSteamFrame()
    {
        if (!OS.HasFeature("linux")) return false;
        if (OS.HasFeature("steam_frame") || LaunchArgs.HasFlag("--steam-frame")) return true;
        if (IsFrameModel(OS.GetModelName())) return true;
        // Linux may report a generic model through Godot. These identify local hardware even when
        // an x86_64 build is running under FEX; process architecture is not a device identifier.
        foreach (var path in new[] { "/sys/firmware/devicetree/base/model", "/proc/device-tree/model", "/sys/class/dmi/id/product_name" })
        {
            try
            {
                using var reader = File.OpenText(path);
                char[] buffer = new char[256];
                if (IsFrameModel(new string(buffer, 0, reader.Read(buffer, 0, buffer.Length)))) return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    internal static bool IsFrameModel(string? model)
        => model?.Contains("Steam Frame", StringComparison.OrdinalIgnoreCase) == true
            || model?.Contains("SteamFrame", StringComparison.OrdinalIgnoreCase) == true;
}
