// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI.Layout;

// Marker. When present on a slot, the parent layout skips this child during arrangement. - xlinka
public sealed class IgnoreLayout : UIComputeComponent
{
    public override void PrepareCompute() { }

    protected override void FlagChanges(RectTransform rect) { }
}
