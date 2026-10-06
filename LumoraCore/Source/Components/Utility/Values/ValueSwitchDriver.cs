// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Holds every target at a value picked by switches: the first of Switches that is on gives its own entry
// of SwitchValues (OnValue when it has none), and with all of them off the targets sit at OffValue. With
// no switches the On field picks between OnValue and OffValue, so a button can flip it directly.
//
// Several switches rather than one because the thing being switched is often shared: two outfits that both
// hide the same boots, or the steps of one slider that each set the same size. One driver per field keeps
// the field driven once, and a value per switch is what the steps mean. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueSwitchDriver<T> : Component
{
    public readonly Sync<bool> On;

    public readonly SyncRefList<IField<bool>> Switches;

    public readonly SyncFieldList<T> SwitchValues;

    public readonly Sync<T> OffValue;

    public readonly Sync<T> OnValue;

    public readonly SyncList<FieldDrive<T>> Drives;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueSwitchDriver()
    {
        On = new Sync<bool>(this, false);
        Switches = new SyncRefList<IField<bool>>(this);
        SwitchValues = new SyncFieldList<T>();
        OffValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        OnValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Drives = new SyncList<FieldDrive<T>>();
    }

    public FieldDrive<T> AddDrive(IField<T>? target)
    {
        var drive = Drives.Add();
        drive.DriveTarget(target);
        return drive;
    }

    public void AddSwitch(IField<bool> on, T value)
    {
        Switches.Add(on);
        SwitchValues.Add(value);
    }

    public T Current
    {
        get
        {
            if (Switches.Count == 0)
                return On.Value ? OnValue.Value : OffValue.Value;
            for (int i = 0; i < Switches.Count; i++)
            {
                if (Switches[i] is { } field && field.Value)
                    return i < SwitchValues.Count ? SwitchValues[i] : OnValue.Value;
            }
            return OffValue.Value;
        }
    }

    public override void OnUpdate(float delta)
    {
        int count = Drives.Count;
        if (count == 0)
            return;

        T value = Current;
        for (int i = 0; i < count; i++)
        {
            var drive = Drives[i];
            if (drive == null)
                continue;
            // Every peer computes this from the same replicated switches, so the result stays local.
            drive.LocalValueOnly = true;
            drive.SetValue(value);
        }
    }
}
