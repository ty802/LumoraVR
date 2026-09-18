// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// Draw order of everything this viewer sees over the session world while the dash is up, as the
// absolute Godot sorting_offset each renderer ends up with (material RenderQueue + SortingOrder on the
// normal path, MeshRenderer.SetOverlayOrder on the lifted one). World UI, lasers and menus sit at
// 4000..11000; the dash surface is 4002 + 20000; the keyboard canvas is a band above the dash. While
// the dash is open the local user's hands and lasers lift above all of that and drop back on close.
// Cursor over beam over hands, so a reticle is never hidden by the line leading to it. -xlinka
public static class UserspaceRenderBands
{
    public const int DashSurfaceSortingOrder = 20000;
    public const int DashSurface = 4002 + DashSurfaceSortingOrder;
    public const int Hands = 24500;
    public const int DirectLine = 24800;
    public const int DirectCursor = 24900;
    public const int Beam = 24950;
    public const int Cursor = 25005;
}
