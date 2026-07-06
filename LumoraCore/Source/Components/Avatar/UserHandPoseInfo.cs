// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Per-user record of the current finger pose source. Lives on the user root so
/// any equipped hand can find it via the user-root registry and read whatever
/// source is currently publishing finger data for that user.
/// </summary>
[ComponentCategory("Users/Avatar/Hands")]
public class UserHandPoseInfo : UserRootComponent
{
    /// <summary>
    /// The source currently providing this user's finger data (null when none -
    /// e.g. a desktop user, who then renders relaxed hands).
    /// </summary>
    [OldName("FingerPoseSource")]
    public readonly SyncRef<IHandPoseSourceComponent> HandPoseSource = null!;
}
