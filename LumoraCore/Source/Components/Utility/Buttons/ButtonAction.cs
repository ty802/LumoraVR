// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Utility;

// Shared plumbing for the components that do something when a button is pressed.
//
// A Helio button carries ONE press action, a SyncDelegate naming a world element and a method. That is
// what makes an action survive duplication and a save, and it is also why several of these cannot each
// hold the button. So one of them claims the delegate and fans the press out to the rest: on a press,
// the holder runs every enabled button action sitting on its own slot or on the button's slot. Put the
// actions on either of those two slots and they all fire.
//
// The claim is re-checked each frame instead of only at start. The holder can be destroyed, the button
// can be retargeted, and the delegate can be rebound by something else - none of those raise a change
// event on the components that are waiting to take over, so a one-shot bind leaves a button that has
// silently stopped doing anything. The check is a couple of reference compares. -xlinka
//
// The press runs on the peer whose user pressed it. Writes from there replicate like any other
// datamodel edit, and are refused the same way if that user lacks permission.
public abstract class ButtonAction : Component
{
    // Defaults to a button on this slot.
    public readonly SyncRef<Button> SourceButton;

    protected ButtonAction()
    {
        SourceButton = new SyncRef<Button>(this);
    }

    public override void OnAttach()
    {
        base.OnAttach();
        if (SourceButton.Target == null)
            SourceButton.Target = Slot.GetComponent<Button>();
    }

    public override void OnUpdate(float delta)
    {
        // Only the authority claims. The delegate is a replicated field, so the host's write reaches
        // every peer and they all resolve to the same holder. Letting clients claim too would put every
        // peer in a permission-denied write loop over a field one of them already owns.
        if (World?.IsAuthority != true)
            return;

        var button = SourceButton.Target;
        if (button == null || button.IsDestroyed)
            return;

        var holder = ((ISyncRef)button.PressAction).RawTarget as ButtonAction;
        if (holder != null && !holder.IsDestroyed && holder.SourceButton.Target == button)
            return;

        // Nothing usable is holding the delegate. Take it, unless something that is not a button action
        // has it - that is a deliberately wired action and stealing it would break whatever set it.
        if (button.PressAction.Target != null && holder == null)
            return;

        button.SetAction(OnButtonPressed);
    }

    // Bound as the button's action by whichever action claims it.
    [SyncMethod]
    public void OnButtonPressed(Button button, UIInteractionContext context)
    {
        var actor = context.Actor;
        Dispatch(Slot, button, actor);
        var buttonSlot = button?.Slot;
        // A component belongs to exactly one slot, so two distinct slots cannot yield the same action
        // twice and nothing has to be de-duplicated.
        if (buttonSlot != null && buttonSlot != Slot)
            Dispatch(buttonSlot, button!, actor);
    }

    private static void Dispatch(Slot? slot, Button button, User? actor)
    {
        if (slot == null || slot.IsDestroyed)
            return;

        foreach (var action in slot.GetComponents<ButtonAction>())
        {
            if (action.IsDestroyed || !action.Enabled.Value || action.SourceButton.Target != button)
                continue;
            action.Pressed(actor);
        }
    }

    // Runs on the pressing user's peer.
    protected abstract void Pressed(User? actor);
}
