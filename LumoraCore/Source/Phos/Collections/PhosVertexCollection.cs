// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Phos.Collections;

/// <summary>
/// Collection of vertices.
/// Used by PhosShape to track which vertices belong to a shape.
/// </summary>
public class PhosVertexCollection : List<PhosVertex>
{
    public PhosVertexCollection() : base() { }

    public PhosVertexCollection(int capacity) : base(capacity) { }
}
