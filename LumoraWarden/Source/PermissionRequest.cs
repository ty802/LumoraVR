// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Warden;

// One access the gate is asked about. A struct passed by in: this is built on every field write and
// every collection touch, so it must not allocate.
public readonly struct PermissionRequest
{
    public readonly IPermissionWorldFacts? World;
    public readonly IPermissionActor? Actor;
    public readonly IPermissionTarget? Target;
    public readonly IPermissionTarget? Parent;
    public readonly IPermissionTarget? Member;
    public readonly PermissionSurface Surface;
    public readonly PermissionAction Action;
    public readonly bool IsNetwork;
    public readonly bool IsFullState;
    public readonly int? Index;
    public readonly object? Key;

    public PermissionRequest(
        IPermissionWorldFacts? world,
        IPermissionActor? actor,
        IPermissionTarget? target,
        IPermissionTarget? parent,
        IPermissionTarget? member,
        PermissionSurface surface,
        PermissionAction action,
        bool isNetwork,
        bool isFullState = false,
        int? index = null,
        object? key = null)
    {
        World = world;
        Actor = actor;
        Target = target;
        Parent = parent;
        Member = member;
        Surface = surface;
        Action = action;
        IsNetwork = isNetwork;
        IsFullState = isFullState;
        Index = index;
        Key = key;
    }
}
