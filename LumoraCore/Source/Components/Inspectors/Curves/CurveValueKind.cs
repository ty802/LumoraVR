// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

public enum CurveValueKind
{
    Scalar,
    Whole,
    Flag,
    Rotation,
    // No channels at all: text keys can be listed but not drawn.
    Text,
}
