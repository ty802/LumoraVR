// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Components.Interaction;
using UIButton = Helio.UI.Button;

namespace Lumora.Core.Components.Audio;

// Makes a button audible: press it and the clip plays from the start. The player does the sounding,
// this only pulls the trigger, so the same clip can be shared by a panel full of buttons.
//
// It hears the presses on its own slot the way every press receiver does, so a physical control rings
// it as well as a panel button. A button on another slot, named in Button, is followed through its local
// press event. This used to claim a button's single press action, and a save from then still names this
// component there; on load that claim is let go of, or every press would play twice. -xlinka
[ComponentCategory("Audio")]
public sealed class ButtonAudioClipPlayer : Component, IPressReceiver
{
    public readonly SyncRef<AudioClipPlayer> Player;

    // Empty means the presses on this slot.
    public readonly SyncRef<UIButton> Button;

    private UIButton? _followed;

    public ButtonAudioClipPlayer()
    {
        Player = new SyncRef<AudioClipPlayer>(this);
        Button = new SyncRef<UIButton>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        LetGoOfClaim();
        Follow();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Follow();
    }

    public override void OnDestroy()
    {
        Unfollow();
        base.OnDestroy();
    }

    public void OnPressed(in PressEvent press) => PlayFromStart();

    // Still here so a press action saved by the old claim resolves.
    [SyncMethod]
    public void OnButtonPressed(UIButton button, UIInteractionContext context) => PlayFromStart();

    [SyncMethod]
    public void PlayFromStart()
    {
        var player = Player.Target;
        if (player == null || player.IsDestroyed)
            return;
        // Play is "from the top" for every transport in the engine, which is exactly what a button
        // sound wants: press it twice in a row and you hear it twice.
        player.Play();
    }

    private void HandleSubmitted(UIInteractionContext context) => PlayFromStart();

    private void Follow()
    {
        var button = Button.Target;
        if (button != null && (button.IsDestroyed || ReferenceEquals(button.Slot, Slot)))
            button = null;
        if (ReferenceEquals(button, _followed))
            return;

        Unfollow();
        if (button == null)
            return;
        _followed = button;
        button.Submitted += HandleSubmitted;
    }

    private void Unfollow()
    {
        var button = _followed;
        _followed = null;
        if (button != null && !button.IsDestroyed)
            button.Submitted -= HandleSubmitted;
    }

    private void LetGoOfClaim()
    {
        if (World?.IsAuthority != true)
            return;
        Release(Button.Target);
        Release(Slot?.GetComponent<UIButton>());

        void Release(UIButton? button)
        {
            if (button is { IsDestroyed: false } && button.PressAction.Target is { } action && ReferenceEquals(action.Target, this))
                button.PressAction.Clear();
        }
    }
}
