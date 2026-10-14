// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input;

// Lets callers pick per-platform defaults without string-matching runtime names.
public enum VRPlatform
{
    None,
    DesktopOpenXR,
    MetaQuestStandalone,
    PicoStandalone,
    ViveFocusStandalone,
    SteamFrameStandalone,
}

public interface IVRDriver
{
    bool IsVRActive { get; }

    void UpdateVRDevices(VRController leftController, VRController rightController, HeadDevice headDevice);

    // Stable id of the tracking backend.
    string VRSystemName { get; }

    // None when VR is not active.
    VRPlatform Platform { get; }

    // Empty when unavailable.
    string RuntimeName { get; }

    // Interaction profile paths; empty when no profile is bound or the controller is idle.
    string LeftInteractionProfile { get; }

    string RightInteractionProfile { get; }
}
