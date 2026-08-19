// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Warden;

[Flags]
public enum PermissionAction : ulong
{
    None = 0,
    Read = 1UL << 0,
    Write = 1UL << 1,
    Create = 1UL << 2,
    Destroy = 1UL << 3,
    ReferenceWrite = 1UL << 4,
    CollectionEnumerate = 1UL << 5,
    CollectionAdd = 1UL << 6,
    CollectionInsert = 1UL << 7,
    CollectionSet = 1UL << 8,
    CollectionRemove = 1UL << 9,
    CollectionClear = 1UL << 10,
    CollectionResize = 1UL << 11,
    Replicate = 1UL << 12,
    Serialize = 1UL << 13,
    ConfigurePermissions = 1UL << 14,

    CollectionMutation = CollectionAdd | CollectionInsert | CollectionSet | CollectionRemove | CollectionClear | CollectionResize,
    Mutation = Write | Create | Destroy | ReferenceWrite | CollectionMutation,
    All = ulong.MaxValue
}

public enum PermissionSurface
{
    Unknown,
    Field,
    SyncElement,
    Array,
    List,
    Dictionary,
    Bag,
    ReplicatedDictionary,
    Worker,
    Slot,
    Component,
    User
}

public enum PermissionResult
{
    Abstain,
    Allow,
    Deny
}

// How a user relates to the world when they join - used to pick a default role.
public enum PermissionAccessClass
{
    Anonymous,
    Visitor,
    Contact,
    Host
}

// What an element is, for the ownership walk. The order the engine tests these in is load-bearing:
// slots and components carry the structural ownership signals, everything else inherits ownership
// from its parent, and a type that is several of these at once must be classified as the most
// specific one. -xlinka
public enum PermissionTargetKind
{
    Other,
    Slot,
    Component,
    SyncElement,
    Worker
}

// What a write against a grabbable surface is doing.
public enum GrabWriteKind
{
    // Not part of the grab protocol - falls through to the ownership/role check.
    None,
    // Grab bookkeeping other than the holder (e.g. where to put the object back).
    GrabState,
    // The holder reference itself, which is where a steal would show up.
    HolderRef,
    // Reparent or pose of the held object.
    Transform
}
