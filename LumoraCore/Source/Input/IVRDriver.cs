// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input;

/// <summary>
/// Interface for VR input drivers.
/// </summary>
public interface IVRDriver
{
    /// <summary>
    /// Check if VR is available and active.
    /// </summary>
    bool IsVRActive { get; }

    /// <summary>
    /// Update VR devices with latest tracking data.
    /// </summary>
    void UpdateVRDevices(VRController leftController, VRController rightController, HeadDevice headDevice);

    /// <summary>
    /// Get the VR system name (e.g., "OpenXR", "Oculus", "SteamVR").
    /// </summary>
    string VRSystemName { get; }
}