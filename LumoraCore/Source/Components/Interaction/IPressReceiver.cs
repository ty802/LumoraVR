// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Something on a pressable thing's slot that wants its presses, whatever the thing is: a panel button, a context
// menu entry, a physical control. Attach it beside one and it fires; it never needs to know which kind it landed
// on. The press arrives where the pressed thing reacts: a panel button on the presser's own machine, a physical
// control on the authority, run as that control's author. -xlinka
public interface IPressReceiver
{
    void OnPressed(in PressEvent press);
}
