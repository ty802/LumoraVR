// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI;

// What a text field does with what was typed once typing stops.
public enum TextFinishHandling
{
    LeaveAsIs,

    // Nothing typed becomes no text at all, which the shown text answers with its NullContent hint.
    NullOnEmpty,

    // Nothing but spaces typed becomes no text at all.
    NullOnWhitespace,
}
