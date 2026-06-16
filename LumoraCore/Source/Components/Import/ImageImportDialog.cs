// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Helio.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// Confirmation dialog for image imports. Only exposes options the image pipeline
// actually supports today: a flat-quad import or a raw-file passthrough. Sphere
// projections (360/180), stereo layouts, LUT, screenshot metadata, etc. are not
// implemented and intentionally not shown - add buttons here when those land. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class ImageImportDialog : ImportDialog
{
    protected override string TitleText => "Image Import";

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "How should this image be imported?", backButton: false);
        SetupGrid(body);
        GridButton(body, "Regular", RunImport);
        GridButton(body, "As Raw File", AsRawFile, BackColor);
    }

    public void RunImport()
    {
        if (!CanInteract) return;
        Logger.Log($"ImageImportDialog: importing {Paths.Count} file(s)");

        int rowSize = (int)MathF.Max(1f, MathF.Ceiling(MathF.Sqrt(Paths.Count)));
        int index = 0;
        var basePos = Slot.GlobalPosition;
        var baseRot = Slot.GlobalRotation;
        var handler = ImportHandlers.Image;
        var target = ResolveTargetWorld();

        foreach (var file in Paths)
        {
            var s = target.RootSlot.AddSlot(Path.GetFileName(file) ?? file);
            var offset = UniversalImporter.GridOffset(ref index, rowSize);
            s.GlobalPosition = basePos + baseRot * offset;
            s.GlobalRotation = baseRot;
            s.GlobalScale = float3.One;

            if (handler != null)
            {
                var pathCaptured = file;
                _ = handler.ImportAsync(s, pathCaptured);
            }
            else
            {
                var label = s.AttachComponent<TextRenderer>();
                label.Text.Value = Path.GetFileName(file) ?? file;
                label.Size.Value = 0.08f;
                s.AttachComponent<Grabbable>().AllowGrab.Value = true;
            }
        }

        Slot.Destroy();
    }
}
