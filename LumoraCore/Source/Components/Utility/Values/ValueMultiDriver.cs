// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Fans one value out to any number of fields.
//
// The alternative is a CopyValue per target, which means N components to find, N to edit and N to keep
// pointed at the same source when the source moves. Here the drives sit in one list on one component.
//
// Source wins over Value when it is set, so this covers both "hold a number here and push it to twenty
// fields" and "follow that field and push it to twenty fields" without a second component in between.
// -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueMultiDriver<T> : Component
{
    // Used when Source is empty. Point a drive or a button at this.
    public readonly Sync<T> Value;

    public readonly SyncRef<IField<T>> Source;

    public readonly SyncList<FieldDrive<T>> Drives;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueMultiDriver()
    {
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Source = new SyncRef<IField<T>>(this);
        Drives = new SyncList<FieldDrive<T>>();
    }

    public FieldDrive<T> AddDrive(IField<T>? target)
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

        var source = Source.Target;
        T value = source != null ? source.Value : Value.Value;

        for (int i = 0; i < count; i++)
        {
            var drive = Drives[i];
            if (drive == null)
                continue;
            // Driving the source itself is a loop with one member in it: the field would be its own
            // input every frame and nothing else could ever write it.
            if (ReferenceEquals(drive.Target, source))
                continue;
            // Set here rather than at AddDrive: the flag is code state, not a sync member, so a drive
            // that arrived over the wire or came back from a save never passed through AddDrive. Every
            // peer runs this same fan-out from the same replicated inputs, so broadcasting the results
            // would double the traffic the source already costs. -xlinka
            drive.LocalValueOnly = true;
            drive.SetValue(value);
        }
    }
}

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
