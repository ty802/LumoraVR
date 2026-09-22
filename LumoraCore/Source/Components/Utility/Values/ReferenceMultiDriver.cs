// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// The reference-typed half of the pair, for fanning one world element out to many reference fields.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceMultiDriver<T> : Component where T : class, IWorldElement
{
    public readonly SyncRef<T> Reference;

    public readonly SyncList<DriveRef<T>> Drives;

    public ReferenceMultiDriver()
    {
        Reference = new SyncRef<T>(this);
        Drives = new SyncList<DriveRef<T>>();
    }

    public DriveRef<T> AddDrive(SyncRef<T>? target)
    {
        var drive = Drives.Add();
        drive.DriveTarget(target);
        return drive;
    }

    public override void OnUpdate(float delta)
    {
        int count = Drives.Count;
        if (count == 0)
            return;

        var value = Reference.Target;
        for (int i = 0; i < count; i++)
        {
            var drive = Drives[i];
            if (drive == null || ReferenceEquals(drive.Target, Reference))
                continue;
            drive.LocalValueOnly = true;
            drive.SetValue(value);
        }
    }
}
