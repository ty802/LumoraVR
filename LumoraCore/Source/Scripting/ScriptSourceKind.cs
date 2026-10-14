// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What a relay on an object passes on to the scripts listening to it: a panel button on it pressed, a
// beam coming onto it or going off it, a beam clicking it; typing into a text field on it starting,
// changing it, stopping and submitting; a drop surface on it taking something let go of; a scripted tool
// on it pressed and let go of, primary and secondary; a press on a panel button on it landing, held each
// frame, and ending. -xlinka
public enum ScriptSourceKind : byte
{
    Press,
    HoverEnter,
    HoverExit,
    Click,
    EditStart,
    EditChange,
    EditEnd,
    EditSubmit,
    Received,
    ToolPrimaryDown,
    ToolPrimaryUp,
    ToolSecondaryDown,
    ToolSecondaryUp,
    PressBegin,
    PressHold,
    PressEnd
}
