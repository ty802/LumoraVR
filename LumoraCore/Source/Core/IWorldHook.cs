// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// Interface for World hooks (platform-specific world rendering).
/// </summary>
public interface IWorldHook
{
    World Owner { get; }

    void Initialize(World owner);
    void ChangeFocus(World.WorldFocus focus);
    void Destroy();
}
