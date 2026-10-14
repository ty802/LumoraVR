// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// An inspectable input. Menu controls and contact pads feed it; Value is the fallback and also lets
// authors test a controller parameter without wearing the avatar. Source is a turned value feeding it, a
// menu slider or something that measures (a bone chain, a contact), read when no switch is on.
//
// Synced says whether the wearer's value is everyone's: a parameter that is not keeps its own value on each
// machine, so the menu switches feeding it only count for the wearer. Contacts and Source are read
// everywhere, since every machine measures them itself. A driver on the wearer's machine writes back
// through Write, which is how its result reaches everyone. Saved says whether the value survives the
// avatar being put away; one that is not starts from Default every time. -xlinka
[ComponentCategory("Animation")]
public sealed class ImportedAnimatorParameter : Component
{
    public readonly Sync<string> Parameter = new();
    public readonly Sync<float> Value = new();
    public readonly Sync<bool> OverrideInput = new();
    public readonly SyncRefList<IField<bool>> Switches = new();
    public readonly SyncFieldList<float> SwitchValues = new();
    public readonly SyncRefList<TouchPad> Contacts = new();
    public readonly SyncRef<IField<float>> Source = new();
    public readonly Sync<bool> Synced;
    public readonly Sync<bool> Saved;
    public readonly Sync<float> Default = new();

    public ImportedAnimatorParameter()
    {
        Synced = new Sync<bool>(this, true);
        Saved = new Sync<bool>(this, true);
    }

    public override void Load(Persistence.DataTreeNode node, Persistence.LoadControl control)
    {
        base.Load(node, control);
        if (!Saved.Value)
            Value.Value = Default.Value;
    }

    // Anything feeding it other than its own Value.
    public bool IsFed => Contacts.Count > 0 || Switches.Count > 0 || Source.Target != null;

    public float Read() => Read(true);

    // Menu false leaves the avatar's menu switches out: what a machine that is not the wearer's reads for a
    // parameter that does not sync. Switches something else measures still count.
    public float Read(bool menu)
    {
        if (Contacts.Count > 0)
        {
            foreach (var pad in Contacts)
                if (pad is { IsDestroyed: false } && pad.Enabled.Value && pad.Slot.IsActive && pad.Touching.Value)
                    return 1f;
            if (Source.Target == null && Switches.Count == 0)
                return 0f;
        }
        for (int i = 0; i < Switches.Count && i < SwitchValues.Count; i++)
        {
            var field = Switches[i];
            if (field?.Value != true || !menu && field is SyncElement { Parent: Avatar.AvatarToggle })
                continue;
            return SwitchValues[i];
        }
        if (Source.Target is { } source) return source.Value;
        return Value.Value;
    }

    // A value set from inside the animator: onto the switch that means it, else the slider, else Value.
    public void Write(float value)
    {
        bool matched = false;
        for (int i = 0; i < Switches.Count && i < SwitchValues.Count; i++)
        {
            if (Switches[i] is not { } field)
                continue;
            bool on = System.MathF.Abs(SwitchValues[i] - value) < 0.01f;
            matched |= on;
            if (field.Value != on && !field.IsDriven)
                field.Value = on;
        }
        if (matched)
            return;
        if (Contacts.Count == 0 && Source.Target is Sync<float> { IsDriven: false } slider)
        {
            if (slider.Value != value)
                slider.Value = value;
            return;
        }
        if (Value.Value != value)
            Value.Value = value;
    }
}
