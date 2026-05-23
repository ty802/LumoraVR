// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

public class WidgetGridScreen : DashboardScreen
{
    private WidgetGrid? _grid;

    public WidgetGrid? Grid => _grid;

    protected override void OnContentReady(Slot contentSlot)
    {
        _grid = contentSlot.GetComponent<WidgetGrid>() ?? contentSlot.AttachComponent<WidgetGrid>();
    }
}
