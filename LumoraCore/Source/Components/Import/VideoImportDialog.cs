// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Helio.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// Confirmation dialog for video imports. Lumora has no video texture / player
// pipeline yet, so this dialog only shows what's actually supported: a raw-file
// passthrough that spawns the file as a labeled grabbable. Once VideoTexture-
// Provider + VideoPlayer land, add presets here for spheres/stereo/etc. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class VideoImportDialog : ImportDialog
{
    protected override string TitleText => "Video Import";

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "How should this video be imported?", backButton: false);
        SetupGrid(body);
        GridButton(body, "Regular", RunImport);
        GridButton(body, "As Raw File", AsRawFile, BackColor);
    }

    public void RunImport()
    {
        if (!CanInteract) return;
        Logger.Log($"VideoImportDialog: importing {Paths.Count} file(s)");

        var basePos = Slot.GlobalPosition;
        var baseRot = Slot.GlobalRotation;
        var offset = float3.Zero;
        var handler = ImportHandlers.Video;
        var target = ResolveTargetWorld();

        foreach (var file in Paths)
        {
            var s = target.RootSlot.AddSlot(Path.GetFileName(file) ?? file);
            s.GlobalPosition = basePos + offset;
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

            offset += baseRot * float3.Right;
        }

        Slot.Destroy();
    }
}
