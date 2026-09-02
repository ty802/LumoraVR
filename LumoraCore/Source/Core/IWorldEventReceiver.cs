// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// Interface for components that want to receive world-level events.
/// </summary>
public interface IWorldEventReceiver
{
    /// <summary>
    /// Check if this component handles a specific world event type.
    /// </summary>
    bool HasEventHandler(World.WorldEvent eventType);

    /// <summary>
    /// Called when a user joins the world.
    /// </summary>
    void OnUserJoined(User user);

    /// <summary>
    /// Called when a user leaves the world.
    /// </summary>
    void OnUserLeft(User user);

    /// <summary>
    /// Called when the world focus changes.
    /// </summary>
    void OnFocusChanged(World.WorldFocus focus);

    /// <summary>
    /// Called when the world is being destroyed.
    /// </summary>
    void OnWorldDestroy();

    // Fires once a joined user's root has appeared with a head node, i.e. their body is really there.
    // OnUserJoined only means the user record exists.
    void OnUserSpawn(User user);

    // Fires after a world save that actually reached disk, with the path it was written to.
    void OnWorldSaved(string path);
}
