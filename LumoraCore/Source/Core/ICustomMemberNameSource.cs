// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// A worker whose collection elements have names of their own. The inspector labels each element
// row with the name instead of its index, and reference cards for the element carry it too. A
// skinned mesh names its blend shape weights after the mesh's shapes, which is what the row is
// for when there are eighty of them. -xlinka
public interface ICustomMemberNameSource
{
    // The name for one element of a collection this worker owns, or null to keep the default.
    string? GetMemberName(ISyncMember element);

    // True when this worker sizes and orders the collection itself (one weight per mesh shape), so
    // the inspector offers no add, remove or reorder on it: any of those breaks the index mapping.
    bool OwnsCollectionShape(ISyncMember collection) => false;
}
