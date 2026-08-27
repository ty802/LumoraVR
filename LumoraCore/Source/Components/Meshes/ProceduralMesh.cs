// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

public abstract class ProceduralMesh : ImplementableComponent, ICustomInspectorUI
{
    protected PhosMesh? phosMesh { get; private set; }
    protected MeshUploadHint uploadHint;

    private bool _isDirty = false;

    // Public Properties (for hook access)

    public PhosMesh? PhosMesh => phosMesh;

    public MeshUploadHint UploadHint => uploadHint;

    public bool IsDirty => _isDirty;

    // Sync Fields

    public readonly Sync<bool> OverrideBoundingBox;

    public readonly Sync<BoundingBox> OverridenBoundingBox;

    // Bake triangle-local barycentric coordinates into the vertex color channel, which is what the
    // wireframe material reads its edges from. This unwelds the mesh and overwrites any vertex
    // colors, so it is off unless asked for, and it is re-applied after every regeneration because a
    // one-shot bake would be wiped the moment any other field changed. -xlinka
    public readonly Sync<bool> WireframeBarycentrics;

    private bool _barycentricsBaked;

    // Constructor

    protected ProceduralMesh()
    {
        OverrideBoundingBox = new Sync<bool>(this, false);
        OverridenBoundingBox = new Sync<BoundingBox>(this, new BoundingBox());
        WireframeBarycentrics = new Sync<bool>(this, false);
    }

    // Lifecycle Hooks

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(WireframeBarycentrics);
    }

    public override void OnStart()
    {
        base.OnStart();
        // Generate initial mesh
        RegenerateMesh();
    }

    // Abstract Methods

    // copy sync field values to local variables for thread safety
    protected abstract void PrepareAssetUpdateData();

    protected abstract void UpdateMeshData(PhosMesh mesh);

    // called when disabled/destroyed
    protected abstract void ClearMeshData();

    // Mesh Generation

    private void PrepareMeshUpdate()
    {
        if (phosMesh == null)
        {
            phosMesh = new PhosMesh();
        }
        uploadHint.SetAll();
    }

    // exposed as an inspector action row via [SyncMethod]
    [SyncMethod]
    public void RegenerateMesh()
    {
        PrepareMeshUpdate();

        if (_barycentricsBaked)
        {
            // The last pass rewrote the buffers into an unwelded copy, so whatever vertex range the
            // generator cached points at geometry that is gone. Start from an empty mesh instead of
            // appending a second copy onto the bake.
            phosMesh!.Clear();
            ClearMeshData();
            _barycentricsBaked = false;
        }

        PrepareAssetUpdateData();
        UpdateMeshData(phosMesh!);

        if (WireframeBarycentrics.Value)
        {
            _barycentricsBaked = PhosBarycentrics.Bake(phosMesh!);
            if (_barycentricsBaked)
                uploadHint.SetAll();
        }

        MarkDirty();
    }

    // snapshot of the current geometry at build time; nothing computed beyond what the mesh already holds
    public void BuildInspectorBody(UIBuilder ui)
    {
        var mesh = phosMesh;
        if (mesh == null)
        {
            AddStatRow(ui, "Mesh statistics", "no mesh data");
            return;
        }

        int triangles = 0;
        foreach (var submesh in mesh.Submeshes)
            triangles += submesh.IndexCount / 3;

        AddStatRow(ui, "Vertices", mesh.VertexCount.ToString());
        AddStatRow(ui, "Triangles", triangles.ToString());
        AddStatRow(ui, "Submeshes", mesh.Submeshes.Count.ToString());
        if (mesh.BlendShapeCount > 0)
            AddStatRow(ui, "Blend shapes", mesh.BlendShapeCount.ToString());
        if (mesh.BoneCount > 0)
            AddStatRow(ui, "Bones", mesh.BoneCount.ToString());
    }

    private static void AddStatRow(UIBuilder ui, string label, string value)
    {
        // Theme from the hosting panel's UI tree, NOT this component's world slot: the mesh's slot
        // has no UITheme above it, and Helio text without a font renders nothing.
        InspectorUI.FixedRow(ui.Root, label, 24f, out var rowUi, ui.Root);
        rowUi.PushStyle();
        rowUi.MinWidth(150f);
        rowUi.PreferredWidth(220f);
        rowUi.FlexibleWidth(0.34f);
        var labelText = rowUi.Text(label, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(labelText.RectTransform!);
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
        rowUi.PushStyle();
        rowUi.FlexibleWidth(1f);
        var valueText = rowUi.Text(value, InspectorUI.FontSize - 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(valueText.RectTransform!);
        valueText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        valueText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
    }

    // triggers a hook update to apply changes to Godot
    protected void MarkDirty()
    {
        _isDirty = true;
        RunApplyChanges();
    }

    public void ClearDirty()
    {
        _isDirty = false;
    }

    public PhosMesh? GetPhosMesh()
    {
        return phosMesh;
    }

    public MeshUploadHint GetUploadHint()
    {
        return uploadHint;
    }

    public BoundingBox GetBoundingBox()
    {
        if (OverrideBoundingBox.Value)
        {
            return OverridenBoundingBox.Value;
        }

        if (phosMesh != null)
        {
            return phosMesh.CalculateBoundingBox();
        }

        return new BoundingBox();
    }

    // Cleanup

    public override void OnDestroy()
    {
        phosMesh?.Clear();
        phosMesh = null;
        ClearMeshData();
        base.OnDestroy();
    }

    // Helper: Subscribe to Property Changes

	protected void SubscribeToChanges<T>(SyncField<T> sync)
	{
		sync.OnChanged += (newVal) => RegenerateMesh();
	}
}
