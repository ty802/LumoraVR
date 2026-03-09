// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Management;

/// <summary>
/// Interface for WorldManager hooks (platform-specific world container).
/// Platform hook interface for world management.
/// </summary>
public interface IWorldManagerHook
{
    WorldManager Owner { get; }

    void Initialize(WorldManager owner, object sceneRoot);
    void Destroy();
}
