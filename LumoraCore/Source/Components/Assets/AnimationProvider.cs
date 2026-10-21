// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

// the asset gathers and decodes itself through AssetManager; this only resolves and requests the URL.
// pointing at a local:// URI is what makes a clip travel: content-addressed in the local DB, replicated
// with the component, and gathered by hash on join
[ComponentCategory("Assets")]
public sealed class AnimationProvider : StaticAssetProvider<AnimationAsset>, ICustomInspectorUI, IInspectorActionContextHandler
{
    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorUI.FixedRow(ui.Root, "Curves", InspectorUI.RowHeight, out var row, ui.Root);
        InspectorUI.RelayButton(row, this, "curves", "Edit curves", 0f);
    }

    public void HandleInspectorAction(string argument, UIInteractionContext context)
    {
        if (argument == "curves")
            CurveStudioPanel.Open(this, null, context.WorldPoint);
    }
}
