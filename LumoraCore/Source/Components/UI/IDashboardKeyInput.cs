// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

/// <summary>
/// A dashboard screen that wants raw keystrokes while it's the current screen (e.g. an inline name field).
/// The dashboard routes typed chars / backspace / enter / escape to the current screen if it implements this;
/// each method returns true if it consumed the key (so the dash doesn't also treat it as search). -xlinka
/// </summary>
public interface IDashboardKeyInput
{
    bool ConsumeChar(char c);
    bool ConsumeBackspace();
    bool ConsumeEnter();
    bool ConsumeEscape();
}
