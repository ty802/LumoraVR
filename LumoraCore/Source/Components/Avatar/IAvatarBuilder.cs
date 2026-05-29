// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// Builds the per-user scene setup (body-node tracking slots, locomotion,
// collider, head output, nameplate, avatar manager) onto a freshly created
// UserRoot. One builder instance can serve every user that joins a world.
// - xlinka
public interface IAvatarBuilder
{
    void BuildAvatar(UserRoot user);
}
