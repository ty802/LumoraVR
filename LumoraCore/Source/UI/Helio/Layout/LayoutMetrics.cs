// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI.Layout;

/// <summary>
/// Triple of min / preferred / flexible sizes along a single axis.
/// Layout containers aggregate these to size their children.
/// </summary>
public struct LayoutMetrics
{
    public float Min;
    public float Preferred;
    public float Flexible;
}
