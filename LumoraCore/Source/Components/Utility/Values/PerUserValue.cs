// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// Drives a field with a value chosen per viewer: each peer gets its own local user's entry, or the
// shared default when that user has none.
//
// The overrides replicate, the SELECTION does not. Every peer holds the whole table and picks its own
// row, so the driven field genuinely differs from machine to machine while nothing per-user ever goes
// on the wire. That only works because a driven value is excluded from field sync in both directions,
// so one peer's pick can never be broadcast over another's. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class PerUserValue<T> : Component
{
    // Used for any user without an entry.
    public readonly Sync<T> Default;

    public readonly Sync<bool> ClearOnUserLeave;

    // Replicates in full to every peer.
    public readonly SyncList<UserValueEntry<T>> Overrides;

    // With the local user's value.
    public readonly FieldDrive<T> Target;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public PerUserValue()
    {
        Default = new Sync<T>(this, SyncCoder.GetDefault<T>());
        ClearOnUserLeave = new Sync<bool>(this, true);
        Overrides = new SyncList<UserValueEntry<T>>();
        // The whole point is per-peer divergence, so this must never generate sync data.
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
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

    public override void OnUpdate(float delta)
    {
        var entry = FindOverride(World?.LocalUser);
        Target.SetValue(entry != null ? entry.Value.Value : Default.Value);
    }

    public override void OnUserLeft(User user)
    {
        // One removal, not one per peer: a list edit run on every machine would fight itself.
        if (ClearOnUserLeave.Value && World?.IsAuthority == true)
            RemoveOverride(user);
    }
}
