// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Hardcoded paths to built-in Lumora assets.
/// </summary>
public static class LumAssets
{
    /// <summary>
    /// Built-in UI scene paths.
    /// </summary>
    public static class UI
    {
        // Core
        public static string Bootstrap => "res://Scenes/UI/Core/Bootstrap.tscn";
        public static string LoadingScreen => "res://Scenes/LoadingScreen.tscn";

        // Debug console (--lumora-debug)
        public static string DebugWindow => "res://Scenes/UI/Debug/DebugWindow.tscn";
    }
}
