// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

// Reads the raw sticks, NOT a locomotion module: sitting suppresses locomotion input, so anything that
// went through the normal movement path would be reading a channel the seat has already muted and
// nothing would ever release. The grace window exists because the same stick push that walked you into
// a touch trigger is often still held on the frame you land in the seat. - xlinka
[ComponentCategory("Users/Avatar")]
public class SeatReleaseOnMove : Component
{
    // falls back to a Seat on this slot
    public readonly SyncRef<Seat> TargetSeat;

    public readonly Sync<bool> ReleaseOnJump;

    // zero disables the stick path
    public readonly Sync<float> MoveStrengthThreshold;

    // seconds after sitting during which input is ignored
    public readonly Sync<float> GracePeriod;

    public SeatReleaseOnMove()
    {
        TargetSeat = new SyncRef<Seat>(this);
        ReleaseOnJump = new Sync<bool>(this, true);
        MoveStrengthThreshold = new Sync<float>(this, 0.8f);
        GracePeriod = new Sync<float>(this, 0.5f);
    }

    private bool _wasSeated;
    private float _grace;

    private Seat? ResolveSeat()
    {
        var seat = TargetSeat.Target;
        if (seat != null && !seat.IsDestroyed)
            return seat;
        seat = Slot?.GetComponent<Seat>();
        return seat != null && !seat.IsDestroyed ? seat : null;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var seat = ResolveSeat();
        bool seated = seat != null && seat.IsLocalUserSeated;

        if (seated && !_wasSeated)
            _grace = MathF.Max(GracePeriod.Value, 0f);
        _wasSeated = seated;

        if (!seated || seat == null)
            return;

        if (_grace > 0f)
        {
            _grace -= delta;
            return;
        }

        var input = Engine.Current?.InputInterface;

        float threshold = MoveStrengthThreshold.Value;
        if (threshold > 0f)
        {
            var axis = LocomotionInputHelper.ReadMovementAxis(input!);
            if (axis.LengthSquared >= threshold * threshold)
            {
                seat.Release();
                return;
            }
        }

        if (ReleaseOnJump.Value && LocomotionInputHelper.ReadJump(input!))
            seat.Release();
    }
}
