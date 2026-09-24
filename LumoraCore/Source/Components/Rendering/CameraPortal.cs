// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A mirror, or a window into somewhere else.
//
// It owns no picture of its own. Every frame it takes the viewer's eye, reflects it across this slot's
// plane (Mirror) or carries it through to PortalTarget's space (Portal), renders the world from there
// into ReflectionTexture, and the ReflectionMaterial on Renderer draws that texture in screen
// projection. In VR that is two renders, one per eye, which is what makes a mirror have depth instead
// of looking like a photograph of one.
//
// The plane is this slot: PlaneNormal in local space, pushed out by PlaneOffset so the reflected camera
// never sits exactly on the surface it is reflecting and z-fights with it. Backward, because that is
// the way a QuadMesh faces, so a mirror is a quad with this on it and nothing to line up.
//
// Resolution is not here. It lives on the RenderTextureProvider this points at: the texture is the
// thing with a size. -xlinka
[ComponentCategory("Rendering")]
public class CameraPortal : ImplementableComponent
{
    public enum Mode
    {
        Mirror,
        Portal
    }

    // The surface this draws on. The renderer is what a viewer looks at; this is what fills it.
    public readonly SyncRef<MeshRenderer> Renderer;

    public readonly AssetRef<RenderTexture> ReflectionTexture;

    public readonly Sync<float> PlaneOffset;
    public readonly Sync<float3> PlaneNormal;

    public readonly Sync<Mode> RenderMode;

    // Portal only: the slot whose space the viewer is carried into. The picture is what you would see
    // standing at the target the way you are standing at this.
    public readonly SyncRef<Slot> PortalTarget;

    // A flag and a value rather than a nullable clear mode, because a nullable enum has no save path
    // here. Off means the world's own sky.
    public readonly Sync<bool> OverrideClear;
    public readonly Sync<ClearMode> Clear;
    public readonly Sync<color> ClearColor;

    // Kept for imports. The renderer has no per-viewport light switch, so this changes nothing here;
    // DisableShadows does.
    public readonly Sync<bool> DisablePerPixelLights;
    public readonly Sync<bool> DisableShadows;

    public readonly Sync<float?> OverrideFarClip;
    public readonly Sync<float?> OverrideNearClip;

    // A rectangular mirror can align its capture camera with the surface. This gives an exact
    // clipping plane even at grazing angles, and spends the texture on the glass instead of the screen.
    // Zero retains screen projection for arbitrary imported portal surfaces.
    public readonly Sync<float2> SurfaceSize;
    public readonly Sync<float> ActivationDistance;

    // Panel quality and power are per viewer. World authors still set the shared defaults above.
    public bool LocalRenderingEnabled { get; set; } = true;
    public int LocalResolution { get; set; }
    public float? LocalRenderDistance { get; set; }

    public CameraPortal()
    {
        Renderer = new SyncRef<MeshRenderer>(this);
        ReflectionTexture = new AssetRef<RenderTexture>(this);
        PlaneOffset = new Sync<float>(this, 0.001f);
        PlaneNormal = new Sync<float3>(this, float3.Backward);
        RenderMode = new Sync<Mode>(this, Mode.Mirror);
        PortalTarget = new SyncRef<Slot>(this);
        OverrideClear = new Sync<bool>(this, false);
        Clear = new Sync<ClearMode>(this, ClearMode.Skybox);
        ClearColor = new Sync<color>(this, new color(0f, 0f, 0f, 1f));
        DisablePerPixelLights = new Sync<bool>(this, false);
        DisableShadows = new Sync<bool>(this, false);
        OverrideFarClip = new Sync<float?>(this, null);
        OverrideNearClip = new Sync<float?>(this, 0.01f);
        SurfaceSize = new Sync<float2>(this, float2.Zero);
        ActivationDistance = new Sync<float>(this, 15f);
    }

    // World-space plane, for anything on this side that needs it (gizmos, the panel). The hook reads
    // the same thing off the node it owns.
    public void GetPlane(out float3 position, out float3 normal)
    {
        var n = PlaneNormal.Value;
        if (n.LengthSquared < 1e-8f)
            n = float3.Backward;
        normal = Slot.LocalDirectionToGlobal(n).Normalized;
        position = Slot.GlobalPosition + normal * PlaneOffset.Value;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        KeepMaterialSamplingInStep();
    }

    // A mirror's picture is rendered with a proper camera and un-mirrored on sampling; a portal's must
    // not be. The material is the only thing that can act on that, and it does not know our mode, so
    // this keeps it told. See ReflectionMaterial.MirrorSampling. -xlinka
    private void KeepMaterialSamplingInStep()
    {
        var renderer = Renderer.Target;
        if (renderer == null || renderer.IsDestroyed)
            return;

        bool mirror = RenderMode.Value == Mode.Mirror;
        for (int i = 0; i < renderer.Materials.Count; i++)
        {
            if (renderer.Materials.GetElement(i).Target is ReflectionMaterial material)
            {
                bool surface = mirror && SurfaceSize.Value.x > 0f && SurfaceSize.Value.y > 0f;
                if (material.MirrorSampling.Value != (mirror && !surface))
                    material.MirrorSampling.Value = mirror && !surface;
                if (material.SurfaceUVSampling.Value != surface)
                    material.SurfaceUVSampling.Value = surface;
            }
        }
    }
}
