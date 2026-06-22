// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI.Layout;

// Where a child sits on the layout's CROSS axis when it isn't force-expanded to fill it. Start/End are
// axis-relative (Start = left for a VerticalLayout's horizontal cross axis, top for a HorizontalLayout's
// vertical cross axis); Center is the middle (the historical hardcoded default). -xlinka
public enum LayoutAlignment
{
    Start,
    Center,
    End,
}
