// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Nexus.Diagnostics;

namespace Lumora.Nexus.Assets;

public static class BuiltinAssetHelper
{
    private const string BuiltinSchema = "builtin://";
    private static readonly List<string> BuiltinAssets = new[]
    {
        "Textures/Dot.png",
        "Assets/Models/headset.gltf",

        // Default avatar models
        "Assets/Prefabs/defaultavatar.prefab",
        "Assets/Prefabs/defaultavatarhumanoid.prefab",
        "Assets/Models/defaultavatar.glb",
        "Assets/Models/defaultavatarhumanoid.glb",
        "Assets/Models/defaultavatar.meshfile",
        "Assets/Models/defaultavatarhumanoid.meshfile",
    }.Select(i => $"builtin://{i}").ToList();

    // Set by the platform layer (e.g. LumoraEngineRunner). Takes the path minus "builtin://",
    // returns the raw bytes.
    public static Func<string, byte[]> PlatformLoader { get; set; } = null!;

    public static bool ValidPath(string path)
    {
        if (!path.StartsWith(BuiltinSchema)) return false;
        if (!BuiltinAssets.Contains(path)) return false;
        return true;
    }

    public static byte[] GetBuiltinAssetData(string path)
    {
        if (!ValidPath(path))
            return null!;

        if (PlatformLoader == null)
        {
            NexusLog.Warn($"BuiltinAssetHelper: No PlatformLoader registered, cannot load '{path}'");
            return null!;
        }

        string relativePath = path.Substring(BuiltinSchema.Length);
        return PlatformLoader(relativePath);
    }
}
