// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Tag component placed on the root slot of an avatar hierarchy.
/// Used to distinguish avatar roots from regular scene objects — grab detection,
/// avatar-specific logic, and tools can search for this component to identify avatars.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public class AvatarRoot : Component
{
    /// <summary>The user who owns this avatar.</summary>
    public readonly SyncRef<UserRoot> Owner = new();

    /// <summary>Whether this avatar is currently active/visible.</summary>
    public readonly Sync<bool> IsActive = new();

    public override void OnInit()
    {
        base.OnInit();
        // IsActive defaults to true (not C# default false)
        IsActive.Value = true;
    }
}
