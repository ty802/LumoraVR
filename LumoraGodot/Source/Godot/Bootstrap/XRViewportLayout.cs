// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Source.Godot.Bootstrap;

public static class XRViewportLayout
{
    // Bootstrap and the mode manager must choose the same viewport. Moving the camera after
    // OpenXR starts replaces the live eye render, including on a Linux standalone headset.
    public static bool UseRootViewport
        => LaunchArgs.HasFlag("--xr-root") || (HeadsetDeviceMode.IsHeadset && !LaunchArgs.HasFlag("--xr-subviewport"));

    public static Viewport Ensure(Node sceneRoot, Viewport rootViewport)
    {
        if (UseRootViewport)
            return rootViewport;

        var viewport = sceneRoot.GetNodeOrNull<SubViewport>("%XRViewport")
            ?? sceneRoot.GetNodeOrNull<SubViewport>("XRViewport");
        if (viewport == null)
        {
            viewport = new SubViewport
            {
                Name = "XRViewport",
                Size = new Vector2I(16, 16),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                PhysicsObjectPicking = false,
            };
            sceneRoot.AddChild(viewport);
            viewport.Owner = sceneRoot;
            viewport.UniqueNameInOwner = true;
        }

        viewport.World3D = rootViewport.World3D;
        var origin = sceneRoot.GetNodeOrNull<XROrigin3D>("%XROrigin3D");
        if (origin != null && origin.GetParent() != viewport)
            origin.Reparent(viewport, keepGlobalTransform: true);
        return viewport;
    }

    public static void ApplySessionState(Viewport rootViewport, Viewport xrViewport, bool sessionActive, bool vrActive)
    {
        bool separate = rootViewport != xrViewport;
        if (separate && rootViewport.UseXR)
            rootViewport.UseXR = false;

        // Keep stereo attachments across desktop mode; suspend drawing without converting to mono. -xlinka
        bool stereo = sessionActive && (separate || vrActive);
        if (xrViewport.UseXR != stereo)
            xrViewport.UseXR = stereo;
        if (xrViewport is SubViewport subViewport)
        {
            var updateMode = stereo && vrActive ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            if (subViewport.RenderTargetUpdateMode != updateMode)
                subViewport.RenderTargetUpdateMode = updateMode;
        }
    }
}
