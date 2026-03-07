// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// Types of sync members for network synchronization.
/// </summary>
public enum SyncMemberType
{
    Field,
    List,
    Dictionary,
    Array,
    Dynamic,
    Bag,
    Object,
    Empty,
    ReplicatedDictionary
}