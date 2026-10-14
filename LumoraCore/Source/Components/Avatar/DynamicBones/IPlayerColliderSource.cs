// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Something that adds player-owned shapes to the world's dynamic bone gather each frame: a user's tracked body,
// or a collider an avatar carries that should push other people's bones the way a hand does.
public interface IPlayerColliderSource
{
    bool IsDestroyed { get; }

    void Contribute(DynamicBoneManager manager);
}
