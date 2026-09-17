// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Applies a slot's per-view transform override while the matching context is the one rendering.
//
// Only the local client runs it, so the override is local by construction: everyone else holds their
// own copy of the same slot and IsLocalUser() is false there. Position, rotation and scale go through
// SlotHook.SetLocalTransformOverride so the slot stays the single writer of its own transform.
//
// A scale override of zero is the one case that does NOT go through the transform: it means "hide this
// from myself". A zero scale would take the shadow with it, and ShadowsOnly (what this used to do) takes
// the object out of every local render including mirrors and in-world cameras - you end up headless in
// your own reflection. So the renderers move onto RenderHelper.LOCAL_HEAD_LAYER instead: the eye cameras
// are the only ones masking that bit off, lights carry no cull mask, so the head keeps its shadow, still
// shows up in mirrors and on camera, and only your own eyes skip it. This is what keeps the head out of
// the view now that the near plane is 2 cm. -xlinka
[ImplementableHook(typeof(LocalViewOverride))]
public class LocalViewOverrideHook : ComponentHook<LocalViewOverride>
{
    private const float HideScaleEpsilon = 0.0001f;
    // Frames we will keep coming back looking for a renderer to hide before accepting that this slot
    // simply has none. Bounded so a LocalViewOverride on a slot with no geometry cannot re-dirty forever.
    private const int HideRetryLimit = 240;

    private int _hideRetries = HideRetryLimit;
    private bool _hiddenFromView;
    private bool _transformOverridden;
    // What each instance's layer mask was before we moved it, so a restore puts back what was actually
    // there rather than a guessed default (a renderer on the private/hidden band, or under a
    // RenderLayerOverride, would otherwise come back wearing the public bit it never had).
    private readonly System.Collections.Generic.Dictionary<Node3D, int> _priorLayers = new();

    public static IHook<LocalViewOverride> Constructor() => new LocalViewOverrideHook();

    public override void Initialize()
    {
        base.Initialize();
        LumoraLogger.Log($"LocalViewOverrideHook: Initialized on '{Owner.Slot.SlotName.Value}'");
    }

    public override void ApplyChanges()
    {
        // UserView hiding only applies while the first-person view renders:
        // third-person/free-cam must show the full avatar.
        bool shouldApply = Owner.Enabled
            && Owner.Context.Value == ViewContext.UserView
            && IsLocalUser()
            && !UserInputState.FocusedExternalCameraActive;

        if (!shouldApply)
        {
            ClearAll();
            return;
        }

        bool hasScale = Owner.HasScaleOverride.Value;
        float3 scale = Owner.ScaleOverride.Value;
        bool hide = hasScale && IsEffectivelyZero(scale);

        SetHiddenFromView(hide);

        float3? position = Owner.HasPositionOverride.Value ? Owner.PositionOverride.Value : null;
        floatQ? rotation = Owner.HasRotationOverride.Value ? Owner.RotationOverride.Value : null;
        // A zero scale is served by the layer swap above, so it must not also reach the transform - the two
        // together would collapse the geometry as well as hide it, and take the shadow with it.
        float3? scaleOverride = hasScale && !hide ? scale : null;

        PushTransform(position, rotation, scaleOverride);
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld)
            ClearAll();

        base.Destroy(destroyingWorld);
    }

    // Helpers

    private void ClearAll()
    {
        SetHiddenFromView(false);
        PushTransform(null, null, null);
    }

    private void PushTransform(float3? position, floatQ? rotation, float3? scale)
    {
        bool wanted = position.HasValue || rotation.HasValue || scale.HasValue;
        if (!wanted && !_transformOverridden)
            return;

        if (Owner.Slot?.Hook is not SlotHook slotHook)
            return;

        slotHook.SetLocalTransformOverride(position, rotation, scale);
        _transformOverridden = wanted;
    }

    private void SetHiddenFromView(bool hidden)
    {
        // Re-applied every frame while active to catch MeshInstance3Ds created after us; the restore
        // only runs on the edge so a slot that never hid never pays for the walk.
        if (!hidden && !_hiddenFromView)
            return;

        var slotNode = (Owner.Slot?.Hook as SlotHook)?.GeneratedNode3D;
        if (slotNode == null || !GodotObject.IsInstanceValid(slotNode))
            return;

        if (hidden)
        {
            Helpers.RenderHelper.SetHierarchyLayer(slotNode, Helpers.RenderHelper.LOCAL_HEAD_LAYER, _priorLayers);

            // The renderer hook builds its MeshInstance3D on its own schedule, and on a fresh avatar it
            // usually lands AFTER this. Nothing re-fires us when it does, so a one-shot walk would leave
            // the head on the public layer and in your eyes - which the old 25 cm near plane used to cover
            // up. Come back until there is actually something here to move, then stop. -xlinka
            if (!HasVisual(slotNode) && _hideRetries-- > 0)
                Owner?.MarkChangeDirty();
            else
                _hideRetries = HideRetryLimit;
        }
        else
        {
            Helpers.RenderHelper.RestoreHierarchyLayer(slotNode, _priorLayers);
            _priorLayers.Clear();
            _hideRetries = HideRetryLimit;
        }

        _hiddenFromView = hidden;
    }

    private static bool HasVisual(Node3D root)
    {
        if (root is VisualInstance3D)
            return true;
        foreach (var child in root.GetChildren())
        {
            if (child is Node3D child3D && HasVisual(child3D))
                return true;
        }
        return false;
    }

    private static bool IsEffectivelyZero(float3 v)
    {
        return System.Math.Abs(v.x) < HideScaleEpsilon
            && System.Math.Abs(v.y) < HideScaleEpsilon
            && System.Math.Abs(v.z) < HideScaleEpsilon;
    }

    private bool IsLocalUser()
    {
        var slot = Owner.Slot;
        while (slot != null)
        {
            var userRoot = slot.GetComponent<UserRoot>();
            if (userRoot != null)
                return userRoot.ActiveUser?.IsLocal ?? false;
            slot = slot.Parent;
        }
        return false;
    }
}
