// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

// Something on a button's slot that wants to know when it was pressed.
//
// A button carries ONE press action - a SyncDelegate naming a world element and a method, which is
// what makes an action survive duplication and a save. One is the right number for the thing that
// built the button. It is the wrong number for a slot somebody has assembled out of parts, where
// three separate components each want to do something on the same press, and working around that is
// what produced the machinery this replaces: the actions elected a holder for the single delegate,
// re-checked the election every frame in case the holder had been destroyed or the delegate stolen,
// and fanned the press back out themselves.
//
// A button broadcasting to whatever is on its slot needs none of that. Attach, and it fires. Nothing
// is elected, nothing can be stolen, and nothing has to run every frame to check. -xlinka
public interface IButtonPressReceiver
{
    void OnButtonPressed(Button button, in UIInteractionContext context);
}
