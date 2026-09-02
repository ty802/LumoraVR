// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.UI;

// A dashboard screen that wants raw keystrokes while it is the current screen (an inline name field, a
// search well). The dash routes typed chars, backspace, enter and escape at the current screen when it
// implements this; each method returns true if it consumed the key, so the dash does not also treat it as
// search. -xlinka
public interface IDashboardKeyInput
{
    bool ConsumeChar(char c);
    bool ConsumeBackspace();
    bool ConsumeEnter();
    bool ConsumeEscape();
}
