// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components;

// appends extra UI to its inspector section, AFTER the reflected member and method rows
// (statistics blocks, previews). unlike ICustomInspector this does not replace the reflected rows.
// the builder is rooted at a fresh vertical container inside the section; add fixed-height rows via
// InspectorUI.FixedRow(ui.Root, ...).
public interface ICustomInspectorUI
{
    void BuildInspectorBody(UIBuilder ui);
}
