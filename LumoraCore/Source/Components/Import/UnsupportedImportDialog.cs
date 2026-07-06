// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Import;

// Shown when a file's asset class has no import pipeline yet (audio, fonts,
// documents, animations, point clouds, etc.). It states plainly that the format
// isn't supported and offers a raw-file drop as the only honest action - it never
// fabricates a "successful" placeholder for content that didn't actually load.
// As real pipelines land, route those classes to their own dialog in
// UniversalImporter and they'll stop reaching here. - xlinka
[ComponentCategory("Assets/Import")]
public sealed class UnsupportedImportDialog : ImportDialog
{
    // The human-readable class name (e.g. "Audio") for the message. Set by
    // UniversalImporter before OnStart. - xlinka
    public string ClassName = "This file type";

    protected override string TitleText => "Import";

    protected override void OpenRoot(UIBuilder ui)
    {
        var body = SetupSection(ui, "Not Supported Yet", backButton: false);
        body.PushStyle().FlexibleHeight(1f);
        var msg = body.Text($"{ClassName} import isn't supported yet.\nYou can still drop it into the world as a raw file.",
            14f, ButtonText);
        msg.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        msg.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        msg.WordWrap.Value = true;
        body.PopStyle();

        SetupGrid(body);
        GridButton(body, "As Raw File", AsRawFile, BackColor);
        GridButton(body, "Close", () => Slot.Destroy());
    }
}
