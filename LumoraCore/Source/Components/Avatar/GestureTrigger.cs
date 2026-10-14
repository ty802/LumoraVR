// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// On while the wearer's hand makes a shape. Active and Amount are what drivers point at: Active as a
// switch, Amount as how far to go (1, or the fist's squeeze with UseFistWeight). Both are worked out on
// every machine from the reader, so they are driven locally and never sent.
//
// BlockBlinking holds the eyes open while the gesture is on, for expressions that set the lids
// themselves. -xlinka
[ComponentCategory("Users/Avatar")]
[DefaultUpdateOrder(-250)]
public class GestureTrigger : Component
{
    public readonly SyncRef<HandGestureReader> Reader;

    public readonly Sync<GestureHand> Hand;

    public readonly Sync<HandGesture> Gesture;

    // Both: the right hand's shape; Gesture is the left's.
    public readonly Sync<HandGesture> OtherGesture;

    public readonly Sync<bool> UseFistWeight;

    public readonly Sync<bool> BlockBlinking;

    public readonly Sync<bool> Active;

    public readonly Sync<float> Amount;

    protected readonly FieldDrive<bool> _active;

    protected readonly FieldDrive<float> _amount;

    private BlinkDriver? _blink;
    private int _blinkSearchedAt = -1;

    public GestureTrigger()
    {
        Reader = new SyncRef<HandGestureReader>(this);
        Hand = new Sync<GestureHand>(this, GestureHand.Either);
        Gesture = new Sync<HandGesture>(this, HandGesture.Fist);
        OtherGesture = new Sync<HandGesture>(this, HandGesture.Neutral);
        UseFistWeight = new Sync<bool>(this, false);
        BlockBlinking = new Sync<bool>(this, false);
        Active = new Sync<bool>(this, false);
        Amount = new Sync<float>(this, 0f);
        _active = new FieldDrive<bool>(this) { LocalValueOnly = true };
        _amount = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        _active.DriveTarget(Active);
        _amount.DriveTarget(Amount);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var reader = Reader.Target;
        bool on = false;
        float amount = 0f;
        if (reader != null && !reader.IsDestroyed)
        {
            var wanted = Gesture.Value;
            switch (Hand.Value)
            {
                case GestureHand.Left:
                    on = reader.Left == wanted;
                    amount = Weight(reader, Chirality.Left);
                    break;
                case GestureHand.Right:
                    on = reader.Right == wanted;
                    amount = Weight(reader, Chirality.Right);
                    break;
                case GestureHand.Both:
                    on = reader.Left == wanted && reader.Right == OtherGesture.Value;
                    amount = on ? 1f : 0f;
                    break;
                default:
                    bool left = reader.Left == wanted, right = reader.Right == wanted;
                    on = left || right;
                    amount = System.Math.Max(left ? Weight(reader, Chirality.Left) : 0f, right ? Weight(reader, Chirality.Right) : 0f);
                    break;
            }
            if (!on)
                amount = 0f;
        }

        if (_active.IsLinkValid)
            _active.SetValue(on);
        if (_amount.IsLinkValid)
            _amount.SetValue(amount);

        if (on && BlockBlinking.Value)
        {
            int structure = Slot.StructureEpoch;
            if ((_blink == null || _blink.IsDestroyed) && structure != _blinkSearchedAt)
            {
                _blink = Slot?.GetComponentInParents<AvatarForm>()?.Slot.GetComponentInChildren<BlinkDriver>();
                _blinkSearchedAt = structure;
            }
            _blink?.HoldOpen();
        }
    }

    private float Weight(HandGestureReader reader, Chirality side)
        => UseFistWeight.Value && Gesture.Value == HandGesture.Fist ? reader.FistOf(side) : 1f;
}
