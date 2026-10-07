// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Source.Godot.Rendering;

// Godot builds a material's pipelines ahead of its first draw for one multisample count: the one in the
// project setting, which was left at none, while the window and the headset both draw at 4x. Every pipeline
// built ahead was for a framebuffer nothing draws into, so each new material compiled its real ones inside
// the frame that first showed it: two per shader graph preview, a 200 ms frame when a graph opened. The
// setting follows the viewport that draws the world; Godot reads it every frame and rebuilds what it keeps
// ahead in the background when it changes. -xlinka
public static class PipelineMsaa
{
    private const string Setting = "rendering/anti_aliasing/quality/msaa_3d";
    private static int _synced = -1;

    public static void Follow(Viewport? world)
    {
        if (world == null || !GodotObject.IsInstanceValid(world))
            return;
        int msaa = (int)world.Msaa3D;
        if (msaa == _synced)
            return;
        _synced = msaa;
        ProjectSettings.SetSetting(Setting, msaa);
    }
}
