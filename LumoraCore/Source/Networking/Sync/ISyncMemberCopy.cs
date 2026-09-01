// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking.Sync;

// Duplication copies a field through its Value property, a reference through the deferred remap, and
// an element list element-by-element. A member that is none of those - raw values in a flat buffer, a
// composite entry, a child whose TYPE is part of the state - has no Value property to set and no
// elements to walk, so the generic walk skips it and the clone comes out EMPTY with nothing logged.
// Implementing this is how such a member says how it copies.
//
// copyChild routes a child member back through the caller's own copy, so a nested reference still
// defers to the transfer phase instead of binding to the original's target. -xlinka
public interface ISyncMemberCopy
{
    void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild);
}
