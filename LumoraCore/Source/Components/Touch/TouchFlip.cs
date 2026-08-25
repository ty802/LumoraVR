// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Touch;

// When a TouchFlip throws over.
public enum TouchFlipTrigger
{
    // The moment the tip makes contact. Immediate, and what a light switch should feel like.
    OnBegin,

    // The moment the tip comes away. Lets a user back out by sliding off without releasing.
    OnEnd,
}

// A latching toggle. One touch flips State and it stays flipped.
//
// State lives here and nowhere else - a light, a door, a colour driver all read this one flag - so
// every peer agrees on what is on without any of them having to have seen the touch that did it.
// -xlinka
[ComponentCategory("Interaction/Touch")]
public class TouchFlip : TouchControl
{
    public readonly Sync<bool> State;

    public readonly Sync<TouchFlipTrigger> Trigger;

    public readonly Sync<bool> IsHovering;

    // Runs on every flip, in either direction.
    public readonly SyncDelegate<TouchAction> Toggled;

    // Runs when the state goes true.
    public readonly SyncDelegate<TouchAction> TurnedOn;

    // Runs when the state goes false.
    public readonly SyncDelegate<TouchAction> TurnedOff;

    private TouchAction? _localToggled;
    private TouchAction? _localTurnedOn;
    private TouchAction? _localTurnedOff;

    public TouchFlip()
    {
        State = new Sync<bool>(this, false);
        Trigger = new Sync<TouchFlipTrigger>(this, TouchFlipTrigger.OnBegin);
        IsHovering = new Sync<bool>(this, false);
        Toggled = new SyncDelegate<TouchAction>(this);
        TurnedOn = new SyncDelegate<TouchAction>(this);
        TurnedOff = new SyncDelegate<TouchAction>(this);
    }

    // A component method replicates; a closure stays local.
    public void SetToggledAction(TouchAction? action) => Bind(Toggled, ref _localToggled, action);

    public void SetTurnedOnAction(TouchAction? action) => Bind(TurnedOn, ref _localTurnedOn, action);

    public void SetTurnedOffAction(TouchAction? action) => Bind(TurnedOff, ref _localTurnedOff, action);

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

        var wanted = Trigger.Value == TouchFlipTrigger.OnBegin ? TouchPhase.Begin : TouchPhase.End;
        if (contact.Contact != wanted)
            return;

        bool next = !State.Value;
        State.Value = next;
        PulseContact(in contact);

        Run(Toggled, _localToggled, in contact);
        Run(next ? TurnedOn : TurnedOff, next ? _localTurnedOn : _localTurnedOff, in contact);
    }
}
