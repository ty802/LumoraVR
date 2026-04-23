// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// The semantic meaning of a persistent avatar reference point.
/// </summary>
public enum AvatarReferenceKind
{
    None = 0,
    View = 1,
    LeftHandGrip = 2,
    RightHandGrip = 3,
    LeftFoot = 4,
    RightFoot = 5,
    Pelvis = 6
}

/// <summary>
/// Tags a slot as a persistent reference point authored by the avatar creator flow.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class AvatarReferencePoint : Component
{
    public readonly Sync<AvatarReferenceKind> Kind = new();

    public override void OnInit()
    {
        base.OnInit();
        Kind.Value = AvatarReferenceKind.None;
    }
}
