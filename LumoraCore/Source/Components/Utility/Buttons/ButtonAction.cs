// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components.Utility;

// Shared plumbing for the components that do something when a button is pressed.
//
// Attach one to a button's slot and it fires on every press of that button: a panel button, a context
// menu entry, or a physical control. That is the whole contract; there is no wiring step and nothing to
// point at anything.
//
// It used to be considerably more than that. A button carries ONE press action - a SyncDelegate, so
// that an action survives duplication and a save - and several of these could not each hold it, so
// they elected a holder and the holder fanned the press back out to the others. The election had to be
// re-checked every frame on every button action in the world, because a holder can be destroyed and a
// delegate can be rebound and neither raises a change event; and it needed a rule about not stealing a
// delegate somebody else had deliberately wired. All of that existed to work around a button that
// could only talk to one thing. Now it talks to its slot, and none of it is needed. -xlinka
//
// A panel button's press runs on the peer whose user pressed it, and its writes are refused like any
// other edit when that user lacks permission. A physical control's press runs on the authority as the
// control's author, the way the control's own actions do. -xlinka
public abstract class ButtonAction : Component, IPressReceiver
{
    public void OnPressed(in PressEvent press)
        => Pressed(press.Actor);

    protected abstract void Pressed(User? actor);
}
