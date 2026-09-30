// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Shading;

namespace Lumora.Core.Components.Scripting;

// A shader node's preview: a square just in front of its card's preview well, wearing its own material that
// shows what the node makes. On a local slot beside the card, not in its canvas, so the card can be rebuilt
// without the preview's shader being made again. -xlinka
public sealed class ScriptNodePreview
{
    private const float Inset = 8f;

    private readonly Slot _slot;
    private readonly QuadMesh _quad;
    private readonly ShaderPreviewMaterial _material;
    private float _placedWidth = -1f;
    private float _placedTop = -1f;

    public ShaderPreviewMaterial Material => _material;

    private ScriptNodePreview(Slot slot, QuadMesh quad, ShaderPreviewMaterial material)
    {
        _slot = slot;
        _quad = quad;
        _material = material;
    }

    public static ScriptNodePreview Create(ScriptGraphNode node)
    {
        var slot = node.Slot.AddLocalSlot("Card Preview");
        var quad = slot.AttachComponent<QuadMesh>();
        // A quad faces away from a card's reader until turned: seen from behind, every preview came out
        // mirrored, a UV's U running right to left.
        quad.Rotation.Value = floatQ.AxisAngleRad(float3.Up, MathF.PI);
        quad.DualSided.Value = true;
        var material = slot.AttachComponent<ShaderPreviewMaterial>();
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = quad;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return new ScriptNodePreview(slot, quad, material);
    }

    // Square in the preview well of a card laid out like this, a hair in front of it.
    public void Place(ScriptCardLayout layout)
    {
        if (_slot.IsDestroyed || (layout.Width == _placedWidth && layout.PreviewTop == _placedTop))
            return;
        _placedWidth = layout.Width;
        _placedTop = layout.PreviewTop;
        float scale = ScriptEditorStyle.CardScale;
        float side = ScriptCardLayout.PreviewSize - Inset * 2f;
        _slot.LocalPosition.Value = new float3(layout.Width * 0.5f * scale, -(layout.PreviewTop + ScriptCardLayout.PreviewSize * 0.5f) * scale, 1f * scale);
        _quad.Size.Value = new float2(side * scale, side * scale);
    }

    public void Show(ShaderProgram? program, ShaderGraphSource? textures)
    {
        if (!_slot.IsDestroyed)
            _material.Show(program, textures);
    }

    public void Destroy()
    {
        if (!_slot.IsDestroyed)
            _slot.Destroy();
    }
}
