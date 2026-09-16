// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using UIButton = Helio.UI.Button;

namespace Lumora.Core.Components.Audio;

// Makes a button audible: press it and the clip plays from the start. The player does the sounding,
// this only pulls the trigger, so the same clip can be shared by a panel full of buttons.
//
// The wiring goes through the button's own press action, which stores a target and a method name and
// therefore survives a duplicate: the clone's button calls the clone's handler. A button carries ONE
// such action, so an empty one is claimed and an occupied one is left alone and ridden through its
// local press event instead of being stolen from whatever already owns it. Neither path is a closure,
// because a closure exists only in the process that built it. -xlinka
[ComponentCategory("Audio")]
public sealed class ButtonAudioClipPlayer : Component
{
    public readonly SyncRef<AudioClipPlayer> Player;

    // Empty means the button on this slot.
    public readonly SyncRef<UIButton> Button;

    private UIButton? _bound;
    private bool _ownsAction;

    public ButtonAudioClipPlayer()
    {
        Player = new SyncRef<AudioClipPlayer>(this);
        Button = new SyncRef<UIButton>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        Bind();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Bind();
    }

    public override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }

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

    private void HandlePressed(UIInteractionContext context) => PlayFromStart();

    private void Bind()
    {
        var button = Button.Target ?? Slot?.GetComponent<UIButton>();
        if (ReferenceEquals(button, _bound))
            return;

        Unbind();
        if (button == null || button.IsDestroyed)
            return;

        _bound = button;
        if (button.PressAction.Target == null)
        {
            button.SetAction(OnButtonPressed);
            _ownsAction = true;
        }
        else
        {
            button.Pressed += HandlePressed;
            _ownsAction = false;
        }
    }

    private void Unbind()
    {
        var button = _bound;
        _bound = null;
        if (button == null || button.IsDestroyed)
            return;

        if (_ownsAction)
        {
            var action = button.PressAction.Target;
            if (action != null && ReferenceEquals(action.Target, this))
                button.PressAction.Clear();
        }
        else
        {
            button.Pressed -= HandlePressed;
        }
        _ownsAction = false;
    }
}
