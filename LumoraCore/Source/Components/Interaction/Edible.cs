// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Something eaten or drunk by holding it to your mouth. It comes in Bites, each taking BiteSeconds of being
// within Reach of someone's mouth (in this slot's scale), and the last bite finishes it. MustBeHeld leaves
// it alone unless a hand holds it; FeedOthers lets someone other than the holder eat it. PauseBetweenBites
// makes the mouth move away before the next bite starts, and KeepProgress keeps a bite half taken when it
// does. DestroyWhenEaten removes the whole object once it is finished.
//
// The host decides who is eating it, from where everyone's head is, and keeps count, so every machine
// agrees and nobody else writes it. Every machine tells what it sees happen: a bite coming along, a bite
// taken, the last one. -xlinka
[ComponentCategory("Interaction")]
public sealed class Edible : Component
{
    // In front of and a little below the eyes, at the person's scale.
    private const float MouthAhead = 0.07f;
    private const float MouthBelow = 0.06f;

    public readonly Sync<int> Bites;
    public readonly Sync<float> BiteSeconds;
    public readonly Sync<float> Reach;
    public readonly Sync<bool> MustBeHeld;
    public readonly Sync<bool> FeedOthers;
    public readonly Sync<bool> PauseBetweenBites;
    public readonly Sync<bool> KeepProgress;
    public readonly Sync<bool> DestroyWhenEaten;

    // Kept by the host.
    public readonly Sync<int> BitesTaken;
    [NonPersistent]
    public readonly Sync<float> Progress;
    [NonPersistent]
    public readonly SyncRef<User> Eater;

    // Who is eating it, the bite under way counting from zero, and how far into it, 0 to 1.
    public event Action<Edible, User?, int, float>? Eating;

    // Who took it and which bite, counting from zero.
    public event Action<Edible, User?, int>? BiteTaken;

    // Who took the last bite.
    public event Action<Edible, User?>? Eaten;

    private readonly List<User> _users = new();
    private bool _waiting;
    private bool _primed;
    private int _seenTaken;
    private float _seenProgress;

    public Edible()
    {
        Bites = new Sync<int>(this, 1);
        BiteSeconds = new Sync<float>(this, 1.5f);
        Reach = new Sync<float>(this, 0.1f);
        MustBeHeld = new Sync<bool>(this, true);
        FeedOthers = new Sync<bool>(this, false);
        PauseBetweenBites = new Sync<bool>(this, true);
        KeepProgress = new Sync<bool>(this, false);
        DestroyWhenEaten = new Sync<bool>(this, false);
        BitesTaken = new Sync<int>(this, 0);
        Progress = new Sync<float>(this, 0f);
        Eater = new SyncRef<User>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        BitesTaken.MarkHostOnly();
        Progress.MarkHostOnly();
        Eater.MarkHostOnly();
    }

    public bool IsEaten => BitesTaken.Value >= System.Math.Max(Bites.Value, 1);

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null || !world.IsAuthority || IsDestroyed || !Enabled.Value || IsEaten)
            return;

        var eater = Eater.Target;
        var candidate = Nearest(world);
        using (world.DataModelPermissions?.EnterSystemBypass())
        {
            if (eater != null && !ReferenceEquals(candidate, eater))
            {
                Eater.Target = null!;
                _waiting = false;
                if (!KeepProgress.Value && Progress.Value != 0f)
                    Progress.Value = 0f;
                eater = null;
            }
            if (candidate == null)
                return;
            if (eater == null)
                Eater.Target = candidate;
            if (_waiting)
                return;

            float seconds = MathF.Max(BiteSeconds.Value, 0.01f);
            float progress = System.Math.Clamp(Progress.Value + delta / seconds, 0f, 1f);
            if (progress < 1f)
            {
                Progress.Value = progress;
                return;
            }
            Progress.Value = 0f;
            BitesTaken.Value += 1;
            _waiting = PauseBetweenBites.Value;
        }
        if (IsEaten && DestroyWhenEaten.Value)
            RunInUpdates(1, () =>
            {
                if (!IsDestroyed && Slot is { IsDestroyed: false } slot)
                {
                    using (World?.DataModelPermissions?.EnterSystemBypass())
                        slot.Destroy();
                }
            });
    }

    // Whoever may eat it with a mouth in reach, the nearest of them.
    private User? Nearest(World world)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || !slot.IsActive)
            return null;
        var grabbable = slot.GetComponent<Grabbable>();
        var holder = grabbable?.Grabber?.OwningUser;
        if (MustBeHeld.Value && holder == null)
            return null;
        float reach = slot.LocalScaleToGlobal(MathF.Max(Reach.Value, 0f));
        if (!(reach > 0f))
            return null;
        var from = slot.GlobalPosition;
        User? best = null;
        float bestDistance = float.MaxValue;
        world.GetAllUsers(_users);
        foreach (var user in _users)
        {
            if (user == null || user.IsDestroyed || (holder != null && !FeedOthers.Value && !ReferenceEquals(user, holder)))
                continue;
            var root = user.Root ?? user.UserRootRef.Target;
            if (root == null || root.IsDestroyed || root.HeadSlot is not { IsDestroyed: false } head)
                continue;
            float scale = MathF.Max(MathF.Abs(root.GlobalScale), 0.01f);
            var mouth = head.GlobalPosition + head.GlobalRotation * (float3.Backward * (MouthAhead * scale) + float3.Down * (MouthBelow * scale));
            float distance = float3.Distance(from, mouth);
            if (distance <= reach && distance < bestDistance)
            {
                best = user;
                bestDistance = distance;
            }
        }
        return best;
    }

    // Told from what changed, so every machine tells the same things once their changes arrive.
    public override void OnChanges()
    {
        base.OnChanges();
        int taken = BitesTaken.Value;
        float progress = Progress.Value;
        if (!_primed)
        {
            _primed = true;
            _seenTaken = taken;
            _seenProgress = progress;
            return;
        }
        var eater = Eater.Target is { IsDestroyed: false } who ? who : null;
        int bites = System.Math.Max(Bites.Value, 1);
        if (taken > _seenTaken)
        {
            for (int bite = _seenTaken; bite < taken; bite++)
                Tell(() => BiteTaken?.Invoke(this, eater, bite));
            if (taken >= bites && _seenTaken < bites)
                Tell(() => Eaten?.Invoke(this, eater));
        }
        _seenTaken = taken;
        if (progress != _seenProgress && progress > 0f && eater != null)
            Tell(() => Eating?.Invoke(this, eater, taken, progress));
        _seenProgress = progress;
    }

    private void Tell(Action tell)
    {
        try
        {
            tell();
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Edible on {ParentHierarchyToString()}: a listener threw: {ex.Message}");
        }
    }
}
