// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

[ComponentCategory("Rendering")]
public sealed class Mirror : CameraPortal
{
    public const int MinimumResolution = 128;
    public const int MaximumResolution = 8192;

    public readonly Sync<float2> Size;
    public readonly Sync<int> Resolution;
    public readonly SyncRef<QuadMesh> Surface;
    public readonly SyncRef<ReflectionMaterial> Material;
    public readonly SyncRef<RenderTextureProvider> Texture;

    public Mirror()
    {
        Size = new Sync<float2>(this, new float2(2.6f, 2.2f));
        Resolution = new Sync<int>(this, 4096);
        Surface = new SyncRef<QuadMesh>(this);
        Material = new SyncRef<ReflectionMaterial>(this);
        Texture = new SyncRef<RenderTextureProvider>(this);
    }

    public override void OnAttach()
    {
        base.OnAttach();
        // OnAttach runs only for a newly authored component. Saved references restore as-is.
        var glass = Slot.AddSlot("Mirror surface");
        glass.AttachComponent<RenderLayerOverride>().Layer.Value = RenderLayerOverride.MirrorLayer;
        Surface.Target = glass.AttachComponent<QuadMesh>();
        Texture.Target = glass.AttachComponent<RenderTextureProvider>();
        Material.Target = glass.AttachComponent<ReflectionMaterial>();
        Material.Target.ReflectionTexture.Target = Texture.Target;
        Material.Target.SurfaceUVSampling.Value = true;
        Material.Target.MirrorSampling.Value = false;
        Renderer.Target = glass.AttachComponent<MeshRenderer>();
        Renderer.Target.Mesh.Target = Surface.Target;
        Renderer.Target.Material.Target = Material.Target;
        Renderer.Target.ShadowCastMode.Value = ShadowCastMode.Off;
        ReflectionTexture.Target = Texture.Target;
        PlaneOffset.Value = 0f;
        OverrideFarClip.Value = 40f;
        SyncSurface();
    }

    public override void OnChanges()
    {
        SyncSurface();
        base.OnChanges();
    }

    private void SyncSurface()
    {
        var size = new float2(System.Math.Clamp(Size.Value.x, 0.05f, 100f),
            System.Math.Clamp(Size.Value.y, 0.05f, 100f));
        SurfaceSize.Value = size;
        if (Surface.Target != null) Surface.Target.Size.Value = size;
        if (Texture.Target != null)
        {
            int resolution = System.Math.Clamp(Resolution.Value, MinimumResolution, MaximumResolution);
            Texture.Target.Width.Value = resolution;
            Texture.Target.Height.Value = resolution;
        }
    }
}
