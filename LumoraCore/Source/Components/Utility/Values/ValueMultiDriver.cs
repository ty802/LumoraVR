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
//
// Pushes when something it reads changed: its own members, the source, a drive in the list, or somebody
// writing over a field it drives. -xlinka
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

    private ChangeFollow? _follow;

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

    public override void OnDestroy()
    {
        _follow?.Clear();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var follow = _follow ??= new ChangeFollow(this);
        int count = Drives.Count;
        follow.Begin();
        follow.AddRef(Source);
        follow.AddList(Drives);
        for (int i = 0; i < count; i++)
            follow.Add(Drives[i]?.Target);
        follow.End();
        if (count == 0)
            return;

        var source = Source.Target;
        T value = source != null ? source.Value : Value.Value;

        follow.BeginQuiet();
        try
        {
            Push(source, value, count);
        }
        finally
        {
            follow.EndQuiet();
        }
    }

    private void Push(IField<T>? source, T value, int count)
    {
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
