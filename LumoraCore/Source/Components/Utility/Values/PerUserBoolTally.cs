// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components.Utility;

// A per-user switch that also says how the whole room stands: how many users have it on, how many off,
// and whether any, all or none do. Counted from the shared table, so it only knows the choices that made
// it into the table (see PerUserValue.Written).
//
// The counts are worked out on every machine from replicated rows and written locally, the same way a
// driven value is: broadcasting them would be one machine's sum fighting everybody else's. -xlinka
[ComponentCategory("Utility/Values")]
public class PerUserBoolTally : PerUserValue<bool>
{
    public readonly Sync<bool> Any;
    public readonly Sync<bool> All;
    public readonly Sync<bool> None;
    public readonly Sync<int> TrueCount;
    public readonly Sync<int> FalseCount;

    // Leave a host that is a bare server out of the count: nobody is sitting at it.
    public readonly Sync<bool> SkipServerHost;

    private readonly List<User> _users = new();

    public PerUserBoolTally()
    {
        Any = new Sync<bool>(this, false);
        All = new Sync<bool>(this, true);
        None = new Sync<bool>(this, true);
        TrueCount = new Sync<int>(this, 0);
        FalseCount = new Sync<int>(this, 0);
        SkipServerHost = new Sync<bool>(this, true);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null)
            return;

        world.GetAllUsers(_users);
        int on = 0;
        int off = 0;
        foreach (var user in _users)
        {
            if (user == null || user.IsDisposed)
                continue;
            if (SkipServerHost.Value && user.IsHost && user.HeadDevice.Value == HeadOutputDevice.Server)
                continue;
            if (ValueFor(user))
                on++;
            else
                off++;
        }
        _users.Clear();

        if (on == TrueCount.Value && off == FalseCount.Value)
            return;
        using (world.DataModelPermissions?.EnterSystemBypass())
        {
            TrueCount.SetValueSilently(on);
            FalseCount.SetValueSilently(off);
            Any.SetValueSilently(on > 0);
            All.SetValueSilently(off == 0);
            None.SetValueSilently(on == 0);
        }
    }
}
