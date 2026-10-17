// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Helio.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

[ComponentCategory("Assets/Import")]
public sealed class ModelImportDialog : ImportDialog
{
    public readonly Sync<float> Scale;
    public readonly Sync<bool> AutoScale;
    public readonly Sync<ModelMaterialType> Material;
    public readonly Sync<bool> Colliders;
    public readonly Sync<bool> SolidToPeople;
    public readonly Sync<bool> AllowGrabbable;
    public readonly Sync<bool> Scalable;
    public readonly Sync<bool> ImportAtOrigin;
    public readonly Sync<bool> CalculateNormals;
    public readonly Sync<bool> CalculateTangents;
    public readonly Sync<bool> ImportVertexColors;
    public readonly Sync<bool> ImportAlbedoColor;
    public readonly Sync<bool> ImportEmissive;
    public readonly Sync<bool> MakeDualSided;
    public readonly Sync<bool> MakeFlatShaded;
    public readonly Sync<int> MaxTextureSize;
    public readonly Sync<bool> ForceNoMipMaps;
    public readonly Sync<bool> ImportAnimations;

    protected override string TitleText => "Model Import";
    protected override float2 CanvasSize => new float2(460f, 640f);

    public ModelImportDialog()
    {
        Scale = new Sync<float>(this, 1f);
        AutoScale = new Sync<bool>(this, false);
        Material = new Sync<ModelMaterialType>(this, ModelMaterialType.Lit);
        Colliders = new Sync<bool>(this, true);
        SolidToPeople = new Sync<bool>(this, false);
        AllowGrabbable = new Sync<bool>(this, true);
        Scalable = new Sync<bool>(this, true);
        ImportAtOrigin = new Sync<bool>(this, false);
        CalculateNormals = new Sync<bool>(this, true);
        CalculateTangents = new Sync<bool>(this, true);
        ImportVertexColors = new Sync<bool>(this, false);
        ImportAlbedoColor = new Sync<bool>(this, true);
        ImportEmissive = new Sync<bool>(this, true);
        MakeDualSided = new Sync<bool>(this, false);
        MakeFlatShaded = new Sync<bool>(this, false);
        MaxTextureSize = new Sync<int>(this, -1);
        ForceNoMipMaps = new Sync<bool>(this, false);
        ImportAnimations = new Sync<bool>(this, true);
    }

    private void DefaultPreset()
    {
        Material.Value = ModelMaterialType.Lit;
        Scale.Value = 1f;
        AutoScale.Value = false;
        Colliders.Value = true;
        SolidToPeople.Value = false;
        CalculateNormals.Value = true;
        CalculateTangents.Value = true;
        ImportAlbedoColor.Value = true;
        ImportEmissive.Value = true;
        AllowGrabbable.Value = true;
        Scalable.Value = true;
    }

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "How should this model be imported?", backButton: false);
        SetupGrid(body);
        GridButton(body, "Regular", PresetRegular);
        GridButton(body, "Vertex Color", PresetVertexColor);
        GridButton(body, "Advanced", () => OpenPage(MenuCustom));
        GridButton(body, "As Raw File", AsRawFile, BackColor);
    }

    public void PresetRegular()
    {
        DefaultPreset();
        OpenPage(MenuScale);
    }

    public void PresetVertexColor()
    {
        DefaultPreset();
        ImportVertexColors.Value = true;
        OpenPage(MenuScale);
    }

    private void MenuScale(UIBuilder ui)
    {
        var body = SetupSection(ui, "Units");
        SetupGrid(body);
        GridButton(body, "Auto", () => SetScaleAndOpenFinish(1f, true));
        GridButton(body, "Humanoid", () => SetScaleAndOpenFinish(1.8f, true));
        GridButton(body, "Meters", () => SetScaleAndOpenFinish(1f, false));
        GridButton(body, "Millimeters", () => SetScaleAndOpenFinish(0.001f, false));
        GridButton(body, "Centimeters", () => SetScaleAndOpenFinish(0.01f, false));
        GridButton(body, "Inches", () => SetScaleAndOpenFinish(0.0254f, false));
    }

    private void SetScaleAndOpenFinish(float scale, bool autoScale)
    {
        Scale.Value = scale;
        AutoScale.Value = autoScale;
        OpenPage(MenuFinish);
    }

    private void MenuFinish(UIBuilder ui)
    {
        var body = SetupSection(ui, "Finalize");
        SetupGrid(body);
        GridButton(body, "Import Model", RunImport, primary: true);
        GridButton(body, "Advanced", () => OpenPage(MenuCustom));
    }

    private static readonly (ModelMaterialType Value, string Name)[] MaterialChoices =
    {
        (ModelMaterialType.Lit, "PBS Metallic"),
        (ModelMaterialType.Specular, "PBS Specular"),
        (ModelMaterialType.DualSided, "PBS Dual Sided"),
        (ModelMaterialType.Triplanar, "PBS Triplanar"),
        (ModelMaterialType.Unlit, "Unlit"),
        (ModelMaterialType.Toon, "Toon"),
        (ModelMaterialType.ToonOutline, "Toon + Outline"),
        (ModelMaterialType.Wireframe, "Wireframe"),
    };

    private void MenuCustom(UIBuilder ui)
    {
        var body = SetupSection(ui, "Advanced Settings");
        var settings = ScrollContent(body);
        SetupChoice(settings, Material, "Material", MaterialChoices);
        SetupCheckbox(settings, AutoScale, "Auto Scale");
        SetupCheckbox(settings, CalculateNormals, "Calculate Normals");
        SetupCheckbox(settings, CalculateTangents, "Calculate Tangents");
        SetupCheckbox(settings, ImportVertexColors, "Import Vertex Colors");
        SetupCheckbox(settings, ImportAlbedoColor, "Import Albedo Color");
        SetupCheckbox(settings, ImportEmissive, "Import Emissive");
        SetupCheckbox(settings, Colliders, "Generate Colliders");
        // The second half of the question the first one only half answered: a collider is for lasers,
        // grabs and falling objects, and whether it is also a wall is a separate decision. Importing a
        // building wants this; importing a chair does not. -xlinka
        SetupCheckbox(settings, SolidToPeople, "Walk Into It");
        SetupCheckbox(settings, ImportAnimations, "Import Animations");
        SetupCheckbox(settings, MakeDualSided, "Dual Sided");
        SetupCheckbox(settings, MakeFlatShaded, "Flat Shaded");
        SetupCheckbox(settings, ForceNoMipMaps, "No Texture Mipmaps");
        SetupCheckbox(settings, AllowGrabbable, "Grabbable");
        SetupCheckbox(settings, Scalable, "Scalable");
        SetupCheckbox(settings, ImportAtOrigin, "Position At Origin");

        SetupGrid(body);
        GridButton(body, "Import Model", RunImport, primary: true);
    }

    public void RunImport()
    {
        if (!CanInteract || RefuseSpawn()) return;
        Logger.Debug($"ModelImportDialog: scale={Scale.Value} auto={AutoScale.Value} material={Material.Value} colliders={Colliders.Value}");

        int rowSize = (int)MathF.Max(1f, MathF.Ceiling(MathF.Sqrt(Paths.Count)));
        int index = 0;
        var basePos = Slot.GlobalPosition;
        var baseRot = Slot.GlobalRotation;
        var handler = ImportHandlers.Model;
        var request = BuildRequest();
        var target = ResolveTargetWorld();

        foreach (var file in Paths)
        {
            var s = target.AddSpawnSlot(target.RootSlot, Path.GetFileName(file) ?? file);
            if (!ImportAtOrigin.Value)
            {
                var offset = UniversalImporter.GridOffset(ref index, rowSize);
                s.GlobalPosition = basePos + baseRot * offset;
                s.GlobalRotation = baseRot;
            }
            s.GlobalScale = float3.One * Scale.Value;

            // Grabbable on the slot the model lands in, whoever builds the model: its colliders are children,
            // so a grab anywhere on it walks up to this. The handler path used to skip the option entirely and
            // an imported model came in that nothing could pick up. -xlinka
            if (AllowGrabbable.Value)
            {
                var g = s.AttachComponent<Grabbable>();
                g.AllowGrab.Value = true;
                g.Scalable.Value = Scalable.Value;
            }

            if (handler != null)
            {
                var pathCaptured = file;
                // Owned by the slot the model imports into, not dropped on the floor. The dialog destroys
                // itself on the next line, so it cannot be the owner; the target can, and that is the
                // thing whose disappearance should stop the import. A discarded Task also swallowed every
                // exception the import threw - this one is watched and logged. -xlinka
                s.StartTask(() => handler.ImportAsync(s, pathCaptured, request));
            }
            else
            {
                UniversalImporter.SpawnFileLabel(s, Path.GetFileName(file) ?? file);
            }
        }

        Slot.Destroy();
    }

    private ModelImportRequest BuildRequest()
    {
        return new ModelImportRequest
        {
            Scale = Scale.Value,
            AutoScale = AutoScale.Value,
            Material = Material.Value,
            Colliders = Colliders.Value,
            SolidToPeople = SolidToPeople.Value,
            Grabbable = AllowGrabbable.Value,
            Scalable = Scalable.Value,
            ImportAtOrigin = ImportAtOrigin.Value,
            CalculateNormals = CalculateNormals.Value,
            CalculateTangents = CalculateTangents.Value,
            ImportVertexColors = ImportVertexColors.Value,
            ImportAlbedoColor = ImportAlbedoColor.Value,
            ImportEmissive = ImportEmissive.Value,
            MakeDualSided = MakeDualSided.Value,
            MakeFlatShaded = MakeFlatShaded.Value,
            MaxTextureSize = MaxTextureSize.Value,
            ForceNoMipMaps = ForceNoMipMaps.Value,
            ImportAnimations = ImportAnimations.Value,
        };
    }
}
