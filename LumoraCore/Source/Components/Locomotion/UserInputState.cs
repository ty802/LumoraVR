// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components;

// Per-local-user input state. Holds the four flags the desktop/VR camera
// and locomotion subsystems used to push around as statics on
// LocomotionController. Attached to the local user's root slot when
// LocomotionController initializes; never spawned on remote users.
//
// Desktop-side scene nodes (drivers, camera controllers) read via the
// `ForFocusedLocalUser` accessor; engine-side components that have a Slot
// should cache the instance from their owning user's root.
// - xlinka
[ComponentCategory("Users")]
public class UserInputState : Component
{
    // Mouse capture for desktop look. The desktop locomotion module raises
    // this when active, and tools or freecam can flip it back to false to
    // release the cursor for UI.
    public bool MouseCaptureRequested { get; private set; }

    public void SetMouseCaptureRequested(bool value)
    {
        MouseCaptureRequested = value;
    }

    // F6 free-cam. Locks the character in place while the camera flies free.
    public bool FreeCamActive { get; private set; }

    public void SetFreeCamActive(bool value)
    {
        FreeCamActive = value;
    }

    // F5 third-person. Character keeps moving but mouse drives orbit
    // instead of head look.
    public bool MouseLookSuppressed { get; private set; }

    public void SetMouseLookSuppressed(bool value)
    {
        MouseLookSuppressed = value;
    }

    // Multi-requester suppression. Tools, dialogs, etc add themselves while
    // they need exclusive mouse/keyboard control and remove themselves when
    // done. As long as the set is non-empty, desktop look/movement is gated.
    private readonly object _suppressionLock = new();
    private readonly HashSet<object> _suppressionRequests = new();

    public bool DesktopInputSuppressed
    {
        get
        {
            lock (_suppressionLock)
                return _suppressionRequests.Count > 0;
        }
    }

    public void SetDesktopInputSuppressed(object requester, bool value)
    {
        if (requester == null)
            return;

        lock (_suppressionLock)
        {
            if (value)
                _suppressionRequests.Add(requester);
            else
                _suppressionRequests.Remove(requester);
        }
    }

    public override void OnDestroy()
    {
        lock (_suppressionLock)
            _suppressionRequests.Clear();

        MouseCaptureRequested = false;
        FreeCamActive = false;
        MouseLookSuppressed = false;

        base.OnDestroy();
    }

    // === Convenience accessors for code that doesn't hold a Slot ===

    public static UserInputState ForFocusedLocalUser
    {
        get
        {
            var root = Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root;
            return (root?.Slot?.GetComponent<UserInputState>()) ?? null!;
        }
    }

    public static bool FocusedMouseCaptureRequested => ForFocusedLocalUser?.MouseCaptureRequested ?? false;
    public static bool FocusedFreeCamActive => ForFocusedLocalUser?.FreeCamActive ?? false;
    public static bool FocusedMouseLookSuppressed => ForFocusedLocalUser?.MouseLookSuppressed ?? false;
    public static bool FocusedDesktopInputSuppressed => ForFocusedLocalUser?.DesktopInputSuppressed ?? false;
}
