// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

// Rides on a seat made for one user (SeatSpawner). On that user's peer it sits them down as soon as the seat
// has arrived; on the authority it takes the seat away again once they have stood up, have left, or never
// sat at all. -xlinka
[ComponentCategory("Hidden")]
public class SeatClaim : Component
{
    public readonly SyncRef<User> Claimant;

    // A seat nobody has sat in by then was a touch that went nowhere.
    private const float GiveUpAfter = 5f;

    private bool _sat;
    private bool _wasOccupied;
    private float _age;

    public SeatClaim()
    {
        Claimant = new SyncRef<User>(this);
    }

    public override void OnUpdate(float delta)
    {
        var world = World;
        var slot = Slot;
        if (world == null || slot == null || slot.IsDestroyed)
            return;

        var seat = slot.GetComponent<Seat>();
        var user = Claimant.Target;
        if (!_sat && seat != null && user != null && user == world.LocalUser)
            _sat = seat.IsLocalUserSeated || seat.TrySit(user);

        if (!world.IsAuthority)
            return;

        _age += delta;
        bool occupied = seat != null && seat.IsOccupied;
        if (occupied)
            _wasOccupied = true;
        if (seat == null || user == null || user.IsDisposed || (_wasOccupied && !occupied) || (!_wasOccupied && _age > GiveUpAfter))
            slot.Destroy();
    }
}
