// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;

namespace Lumora.Core.Components.Avatar;

// Per-user record of the source publishing this user's finger data, found from any equipped hand
// through the user-root registry. Null for a user with nothing to publish.
[ComponentCategory("Users/Avatar/Hands")]
public class UserHandPoseInfo : UserRootComponent
{
    [OldName("FingerPoseSource")]
    public readonly SyncRef<IHandPoseSourceComponent> HandPoseSource = null!;
}
