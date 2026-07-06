// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Helio.UI;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// Confirmation dialog for video imports. Lumora has no video texture / player
// pipeline, and no platform handler is registered, so "Regular" import isn't real
// yet - selecting it shows an honest "not supported" notice instead of spawning a
// labeled placeholder that pretends the video loaded. "As Raw File" still works: it
// drops the file into the world as a grabbable so you can hand it off / save it.
// Once a VideoTextureProvider + IVideoImportHandler land, replace the notice with
// real presets (flat/sphere/stereo). - xlinka
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

        // A registered video handler would make this real; until one exists, be honest.
        var handler = ImportHandlers.Video;
        if (handler == null)
        {
            Logger.Warn($"VideoImportDialog: no video pipeline - {Paths.Count} file(s) not imported.");
            ShowUnsupported("Video playback isn't supported yet.\nUse \"As Raw File\" to drop the file into the world.");
            return;
        }

        Logger.Log($"VideoImportDialog: importing {Paths.Count} file(s)");
        var basePos = Slot.GlobalPosition;
        var baseRot = Slot.GlobalRotation;
        var offset = float3.Zero;
        var target = ResolveTargetWorld();

        foreach (var file in Paths)
        {
            var s = target.RootSlot.AddSlot(Path.GetFileName(file) ?? file);
            s.GlobalPosition = basePos + offset;
            s.GlobalRotation = baseRot;
            s.GlobalScale = float3.One;

            var pathCaptured = file;
            _ = handler.ImportAsync(s, pathCaptured);

            offset += baseRot * float3.Right;
        }

        Slot.Destroy();
    }
}
