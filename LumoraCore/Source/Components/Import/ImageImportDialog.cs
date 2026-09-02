// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading.Tasks;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Import;

// Confirmation dialog for image imports. Only exposes options the image pipeline
// actually supports today: a flat-quad import, an equirectangular panorama turned
// into the world's sky, or a raw-file passthrough. Stereo layouts, LUT, screenshot
// metadata, etc. are not implemented and intentionally not shown - add buttons here
// when those land. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class ImageImportDialog : ImportDialog
{
    protected override string TitleText => "Image Import";

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "How should this image be imported?", backButton: false);
        SetupGrid(body);
        GridButton(body, "Regular", RunImport);
        GridButton(body, "As Skybox", AsSkybox);
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

    // Treat the image as an equirectangular panorama and make it the world's sky.
    //
    // Only the first file is used. A world has one sky, and importing four panoramas at once would
    // either build four skyboxes that fight over it or silently drop three - neither is what anyone
    // dropping a folder of them meant, so take the first and say so in the log. - xlinka
    public void AsSkybox()
    {
        if (!CanInteract) return;
        if (Paths.Count == 0) { Slot.Destroy(); return; }

        var file = Paths[0];
        if (Paths.Count > 1)
            Logger.Log($"ImageImportDialog: a world has one sky; using {Path.GetFileName(file)} and ignoring {Paths.Count - 1} other file(s)");

        var target = ResolveTargetWorld();
        var slot = target.RootSlot.AddSlot(Path.GetFileNameWithoutExtension(file) ?? "Skybox");

        var cubemap = slot.AttachComponent<StaticCubemap>();
        var skybox = slot.AttachComponent<Skybox>();
        skybox.Cubemap.Target = cubemap;
        // Outbid whatever sky the world already has: someone who just dropped a panorama in wants to
        // see it, not to find out they also have to go turn the old one off.
        skybox.MakeActive();

        AssignPanorama(cubemap, file);
        Slot.Destroy();
    }

    // The copy into local storage is I/O, so it cannot happen on the world thread; the URL write has
    // to go back onto it. Everything after that is the ordinary asset path - the cubemap gathers,
    // projects and uploads itself, and the sky hook picks it up when it reports loaded.
    //
    // The cubemap owns the task, so it needs no destroyed check of its own: if someone deletes the
    // skybox while the file is still copying, the hop back to the world cancels the task instead of
    // resuming into it. This dialog is NOT the owner - it destroys itself on the next line. -xlinka
    private static void AssignPanorama(StaticCubemap cubemap, string file)
    {
        cubemap.StartTask(async () =>
        {
            await WorldContext.ToBackground();

            string uri = file;
            var db = Engine.Current?.LocalDB;
            if (db != null)
            {
                var imported = await db.ImportLocalAssetAsync(file, LocalDB.ImportLocation.Copy);
                if (!string.IsNullOrEmpty(imported))
                    uri = imported;
            }

            await WorldContext.ToWorld();
            cubemap.URL.Value = new Uri(uri);
        });
    }
}
