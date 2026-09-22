// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// Same per-peer-divergence design as PerUserValue, for a reference field.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class PerUserReference<T> : Component where T : class, IWorldElement
{
    // Used for any user without an entry.
    public readonly SyncRef<T> Default;

    public readonly Sync<bool> ClearOnUserLeave;

    // Replicates in full to every peer.
    public readonly SyncList<UserReferenceEntry<T>> Overrides;

    // With the local user's target.
    public readonly DriveRef<T> Target;

    public PerUserReference()
    {
        Default = new SyncRef<T>(this);
        ClearOnUserLeave = new Sync<bool>(this, true);
        Overrides = new SyncList<UserReferenceEntry<T>>();
        Target = new DriveRef<T>(this) { LocalValueOnly = true };
    }

    public UserReferenceEntry<T> SetOverride(User user, T? target)
    {
        var entry = FindOverride(user);
        if (entry == null)
        {
            entry = Overrides.Add();
            entry.User.Target = user;
        }
        entry.Reference.Target = target!;
        return entry;
    }

    // Dropping them back to Default.
    public void RemoveOverride(User user)
    {
        Overrides.RemoveAll(e => e.User.Target == user);
    }

    // Null when they have none.
    public UserReferenceEntry<T>? FindOverride(User? user)
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
        Target.SetValue(entry != null ? entry.Reference.Target : Default.Target);
    }

    public override void OnUserLeft(User user)
    {
        if (ClearOnUserLeave.Value && World?.IsAuthority == true)
            RemoveOverride(user);
    }
}
