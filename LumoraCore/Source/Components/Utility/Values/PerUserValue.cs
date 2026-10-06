// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// What a write to a per-user field does.
public enum PerUserWrite
{
    // Nothing that lasts: the next update puts the viewer's value back.
    Dropped,
    // Changes the shared default, for everyone without an entry of their own.
    SetsDefault,
    // Becomes the writer's own entry.
    SetsOwn,
}

// Drives a field with a value chosen per viewer: each peer gets its own local user's entry, or the
// shared default when that user has none.
//
// The overrides replicate, the SELECTION does not. Every peer holds the whole table and picks its own
// row, so the driven field genuinely differs from machine to machine while nothing per-user ever goes
// on the wire. That only works because a driven value is excluded from field sync in both directions,
// so one peer's pick can never be broadcast over another's.
//
// Picks again when something it reads changed: its own members, an entry, the user at this machine
// arriving, or somebody writing over the field it drives. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class PerUserValue<T> : Component
{
    // Used for any user without an entry.
    public readonly Sync<T> Default;

    public readonly Sync<bool> ClearOnUserLeave;

    // What a write to the driven field does; a toggle or a slider on it is a per-user control with SetsOwn.
    public readonly Sync<PerUserWrite> Writes;

    // Replicates in full to every peer.
    public readonly SyncList<UserValueEntry<T>> Overrides;

    // With the local user's value. Holds unless writes are dropped, so a write lands in Written.
    public readonly HoldingFieldDrive<T> Target;

    // The local user's latest own write, which holds here even when the table would not take it. Never
    // synced: it changes what this machine shows and nothing anyone else sees.
    private bool _hasLocal;
    private T _local = default!;
    private ChangeFollow? _follow;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public PerUserValue()
    {
        Default = new Sync<T>(this, SyncCoder.GetDefault<T>());
        ClearOnUserLeave = new Sync<bool>(this, true);
        Writes = new Sync<PerUserWrite>(this, PerUserWrite.Dropped);
        Overrides = new SyncList<UserValueEntry<T>>();
        // The whole point is per-peer divergence, so this must never generate sync data.
        Target = new HoldingFieldDrive<T>(this) { LocalValueOnly = true };
        Target.Holds = () => Writes.Value != PerUserWrite.Dropped;
        Target.SetupValueSetHook(Written);
    }

    // A toggle or a slider writing the driven field, on the writer's machine. An own entry goes in the shared
    // table when the gate lets this user edit it, so every peer can count it; a world that is frozen for
    // them still gets their choice on their own screen, which is all a per-viewer value ever shows. A new
    // default is a shared edit like any other and is refused the same way. -xlinka
    private void Written(SyncField<T> field, T value)
    {
        var user = World?.LocalUser;
        if (user == null)
            return;
        if (Writes.Value == PerUserWrite.SetsDefault)
        {
            Default.Value = value;
            Target.SetValue(ValueFor(user));
            return;
        }
        _local = value;
        _hasLocal = true;
        if (Overrides.IsWriteAllowed())
        {
            try
            {
                SetOverride(user, value);
            }
            catch (System.UnauthorizedAccessException)
            {
            }
        }
        Target.SetValue(value);
    }

    public UserValueEntry<T> SetOverride(User user, T value)
    {
        var entry = FindOverride(user);
        if (entry == null)
        {
            entry = Overrides.Add();
            entry.User.Target = user;
        }
        entry.Value.Value = value;
        return entry;
    }

    // Dropping them back to Default.
    public void RemoveOverride(User user)
    {
        Overrides.RemoveAll(e => e.User.Target == user);
    }

    // Null when they have none.
    public UserValueEntry<T>? FindOverride(User? user)
    {
        if (user == null)
            return null;
        foreach (var entry in Overrides.Elements)
        {
            if (entry.User.Target == user)
                return entry;
        }
        return null;
    }

    // What this user sees: on their own machine their latest write, else their entry, else Default.
    public T ValueFor(User? user)
    {
        if (_hasLocal && user != null && user == World?.LocalUser)
            return _local;
        var entry = FindOverride(user);
        return entry != null ? entry.Value.Value : Default.Value;
    }

    public override void OnDestroy()
    {
        _follow?.Clear();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var follow = _follow ??= new ChangeFollow(this);
        follow.Begin();
        follow.AddList(Overrides);
        for (int i = 0; i < Overrides.Count; i++)
        {
            var entry = Overrides[i];
            if (entry == null)
                continue;
            follow.AddRef(entry.User);
            follow.Add(entry.Value);
        }
        follow.Add(Target.Target);
        follow.End();

        follow.BeginQuiet();
        try
        {
            Target.SetValue(ValueFor(World?.LocalUser));
        }
        finally
        {
            follow.EndQuiet();
        }
    }

    // On a join the table arrives before this machine knows which user is its own.
    public override void OnUserJoined(User user)
    {
        base.OnUserJoined(user);
        MarkChangeDirty();
    }

    public override void OnUserLeft(User user)
    {
        // One removal, not one per peer: a list edit run on every machine would fight itself.
        if (ClearOnUserLeave.Value && World?.IsAuthority == true)
            RemoveOverride(user);
    }
}
