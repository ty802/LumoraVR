// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Touch;

// A momentary button. Down while touched, up when released, and it fires the same kind of bound
// action a Helio screen button fires - so one handler can serve both a panel row and a physical
// switch on the wall without knowing which one pressed it.
//
// State is written by the TOUCHING user's client and replicates from there. Nobody arbitrates
// presses: if the toucher lacks permission to write the flag, the write is refused by the normal
// datamodel gate and the button simply does not go down for them. That is the correct outcome and
// it needs no special-casing here. -xlinka
[ComponentCategory("Interaction/Touch")]
public class TouchSwitch : TouchControl
{
    public readonly Sync<bool> IsPressed;

    public readonly Sync<bool> IsHovering;

    // Text field driven as this switch's caption. Also names it on the pointer.
    public readonly SyncRef<IField<string>> Label;

    // Runs once when the switch goes down.
    public readonly SyncDelegate<TouchAction> Pressed;

    // Runs every frame the switch is held.
    public readonly SyncDelegate<TouchAction> Pressing;

    // Runs once when the switch comes back up.
    public readonly SyncDelegate<TouchAction> Released;

    private TouchAction? _localPressed;
    private TouchAction? _localPressing;
    private TouchAction? _localReleased;

    public TouchSwitch()
    {
        IsPressed = new Sync<bool>(this, false);
        IsHovering = new Sync<bool>(this, false);
        Label = new SyncRef<IField<string>>(this);
        Pressed = new SyncDelegate<TouchAction>(this);
        Pressing = new SyncDelegate<TouchAction>(this);
        Released = new SyncDelegate<TouchAction>(this);
    }

    public string? LabelText
    {
        get => Label.Target?.Value;
        set
        {
            if (Label.Target != null)
                Label.Target.Value = value!;
        }
    }

    protected override string? PointerLabel => LabelText ?? base.PointerLabel;

    // A component method replicates; a closure stays local.
    public void SetPressedAction(TouchAction? action) => Bind(Pressed, ref _localPressed, action);

    public void SetPressingAction(TouchAction? action) => Bind(Pressing, ref _localPressing, action);

    public void SetReleasedAction(TouchAction? action) => Bind(Released, ref _localReleased, action);

    public override SyncDelegate<TouchAction>? ResponseSlot(TouchResponse response) => response switch
    {
        TouchResponse.Pressed => Pressed,
        TouchResponse.Pressing => Pressing,
        TouchResponse.Released => Released,
        _ => null,
    };

    protected override void OnTouchContact(in TouchContact contact)
    {
        if (contact.Hover == TouchPhase.Begin)
        {
            IsHovering.Value = true;
            PulseHover(in contact);
        }
        else if (contact.Hover == TouchPhase.End)
        {
            IsHovering.Value = false;
        }

        switch (contact.Contact)
        {
            case TouchPhase.Begin:
                IsPressed.Value = true;
                PulseContact(in contact);
                Run(Pressed, _localPressed, in contact);
                break;

            case TouchPhase.Stay:
                // A probe can be handed to us mid-contact when a hand sweeps in from an adjacent
                // control, so treat a Stay we never saw begin as the press itself rather than
                // dropping it on the floor. -xlinka
                if (!IsPressed.Value)
                {
                    IsPressed.Value = true;
                    PulseContact(in contact);
                    Run(Pressed, _localPressed, in contact);
                }
                else
                {
                    Run(Pressing, _localPressing, in contact);
                }
                break;

            case TouchPhase.End:
                if (IsPressed.Value)
                {
                    IsPressed.Value = false;
                    PulseContact(in contact);
                    Run(Released, _localReleased, in contact);
                }
                break;
        }
    }
}
