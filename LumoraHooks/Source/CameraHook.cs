// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Godot.Hooks;

// Camera component → Godot Camera3D. Clear mode and viewport rect are
// recognized but pass through Godot's defaults today; expand the helpers
// below once the corresponding camera features land. - xlinka
[ImplementableHook(typeof(Camera))]
public class CameraHook : NodeBackedComponentHook<Camera, Camera3D>
{
    public Camera3D GodotCamera => PlatformNode;

    protected override Camera3D CreatePlatformNode()
    {
        return new Camera3D
        {
            Name = "Camera",
            PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
            Environment = null,
            Attributes = null,
        };
    }

    protected override void SyncProperties()
    {
        var camera = PlatformNode;
        camera.Projection = Owner.Projection.Value == ProjectionType.Orthographic
            ? Camera3D.ProjectionType.Orthogonal
            : Camera3D.ProjectionType.Perspective;
        camera.Fov = Owner.FieldOfView.Value;
        camera.Size = Owner.OrthographicSize.Value;
        camera.Near = Owner.NearClip.Value;
        camera.Far = Owner.FarClip.Value;

        UpdateClearMode();
        UpdateViewport();

        camera.Current = Owner.Enabled.Value && Owner.Slot.IsActive;
    }

    private void UpdateClearMode()
    {
        switch (Owner.Clear.Value)
        {
            case ClearMode.Skybox:
            case ClearMode.Color:
            case ClearMode.DepthOnly:
            case ClearMode.Nothing:
                break;
        }
    }

    private void UpdateViewport()
    {
        float4 viewport = Owner.ViewportRect.Value;
    }
}
