// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

// Takes a spawned player back out of the world once it has finished and nobody restarted it. A video
// someone dropped in to show one clip should not still be sitting there an hour later.
//
// ONE peer does the destroying: whoever spawned it, remembered in CheckingUser, because they are the
// one who knows they are done with it. Every peer counting its own grace period would race to destroy
// the same slot. If that user has left, the authority picks it up rather than the object becoming
// immortal. The countdown only runs while a live playable is actually there and stopped - a ref that
// has not resolved yet on a joining peer must not read as "finished". -xlinka
[ComponentCategory("Media")]
public sealed class StoppedPlayableCleaner : Component
{
    public readonly SyncRef<Component> Playable;

    // Seconds of not playing before the slot goes. Zero destroys on the first stopped frame.
    public readonly Sync<float> GracePeriod;

    public readonly SyncRef<User> CheckingUser;

    private float _stoppedFor;

    public IPlayable? Target => Playable.Target as IPlayable;

    public StoppedPlayableCleaner()
    {
        Playable = new SyncRef<Component>(this);
        GracePeriod = new Sync<float>(this, 5f);
        CheckingUser = new SyncRef<User>(this);
    }

    public override void OnAttach()
    {
        base.OnAttach();
        var local = World?.LocalUser;
        if (local != null)
            CheckingUser.Target = local;
    }

    private bool IsMine
    {
        get
        {
            var world = World;
            if (world == null)
                return false;
            var checker = CheckingUser.Target;
            if (checker != null && !checker.IsDestroyed)
                return ReferenceEquals(checker, world.LocalUser);
            return world.IsAuthority;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!IsMine)
            return;

        var target = Target;
        if (target == null)
        {
            _stoppedFor = 0f;
            return;
        }

        if (target.IsPlaying)
        {
            _stoppedFor = 0f;
            return;
        }

        _stoppedFor += delta;
        if (_stoppedFor >= GracePeriod.Value)
        {
            Slot.Destroy();
        }
    }
}
