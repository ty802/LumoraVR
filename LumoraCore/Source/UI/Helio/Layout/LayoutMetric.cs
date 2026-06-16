// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Helio.UI.Layout;

/// <summary>
/// Bit flags identifying which layout metrics of an element have changed and need recomputing.
/// </summary>
[Flags]
public enum LayoutMetric
{
    None = 0,
    MinWidth = 1 << 0,
    PreferredWidth = 1 << 1,
    FlexibleWidth = 1 << 2,
    MinHeight = 1 << 3,
    PreferredHeight = 1 << 4,
    FlexibleHeight = 1 << 5,
    Area = 1 << 6,

    AllWidth = MinWidth | PreferredWidth | FlexibleWidth,
    AllHeight = MinHeight | PreferredHeight | FlexibleHeight,
}
