// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

// Observer for DestroyPreservingAssets. The strip tears a subtree down in one
// pass and there is no way to reconstruct what it removed afterwards, so a caller that needs to
// reverse the operation gets told about each piece at the moment it still exists.
// Supplying a recorder also changes what happens to a slot the strip emptied: the recorder can take
// it instead of letting it be destroyed. Keeping the slot object alive is what makes an undo cheap
// and exact - the RefID, its members and every reference pointing at it stay valid, so nothing
// outside the subtree has to be re-bound when the slot comes back. Components have no equivalent
// escape (a component cannot move between slots), so those are serialized and re-attached. -xlinka
public interface ISlotStripRecorder
{
    void ComponentStripped(Component component);

    // Return true to claim it (the recorder must move it out of the doomed subtree); false destroys it as
    // usual.
    bool SlotEmptied(Slot slot);

    // Called before the move so the original parent and pose are still readable.
    void SlotRelocating(Slot slot, Slot holder);

    void HolderCreated(Slot holder);
}
