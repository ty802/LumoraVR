// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Phos.Collections;

/// <summary>
/// Collection of triangles.
/// Used by PhosShape to track which triangles belong to a shape.
/// </summary>
public class PhosTriangleCollection : List<PhosTriangle>
{
    internal PhosTriangle[] _triangles => ToArray();

    public PhosTriangleCollection() : base() { }

    public PhosTriangleCollection(int capacity) : base(capacity) { }

    public PhosTriangleCollection(IEnumerable<PhosTriangle> collection) : base(collection) { }
}
