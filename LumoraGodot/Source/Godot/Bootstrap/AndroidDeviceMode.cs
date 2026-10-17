// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Source.Godot.Bootstrap;

// The phone APK has no XR loader. Headset presets retain their vendor-specific OpenXR runtime.
public static class AndroidDeviceMode
{
    public static bool IsPhone => OS.HasFeature("android") && OS.HasFeature("android_phone");
    public static bool IsHeadset => OS.HasFeature("android") && !OS.HasFeature("android_phone");
}
