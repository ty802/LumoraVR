// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Logging;

namespace Lumora.Core.Components.Import;

// Confirmation dialog for shader imports. Two options the pipeline actually supports:
// a grabbable material orb (source-validated, params auto-populated) or the raw-file
// passthrough every other dialog has. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class ShaderImportDialog : ImportDialog
{
    protected override string TitleText => "Shader Import";

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "How should this shader be imported?", backButton: false);
        SetupGrid(body);
        GridButton(body, "Material Orb", RunImport);
        GridButton(body, "As Raw File", AsRawFile, BackColor);
    }

    public void RunImport()
    {
        if (!CanInteract) return;
        Logger.Log($"ShaderImportDialog: importing {Paths.Count} shader file(s) as material orbs");
        UniversalImporter.SpawnShaderOrbs(Paths, ResolveTargetWorld(), Slot.GlobalPosition, Slot.GlobalRotation);
        Slot.Destroy();
    }
}
