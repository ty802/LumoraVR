// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components.UI;

public sealed class LabelWidgetPreset : TextWidgetPreset
{
    public readonly Sync<string> LabelText;

    public LabelWidgetPreset()
    {
        LabelText = new Sync<string>(this, string.Empty);
    }

    protected override void SetupText(Text text)
    {
        text.Content.Value = LabelText.Value;
    }
}
