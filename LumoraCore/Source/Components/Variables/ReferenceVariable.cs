// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Variables;

// Declares a named variable holding a reference to a world element: the slot an item should follow,
// the user who owns it, whatever the content wants to hand around by name.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements)]
public class ReferenceVariable<T> : VariableBase<T> where T : class, IWorldElement
{
    public readonly SyncRef<T> Reference;

    // Adopt the identity's existing target on binding, or replace it with this one.
    public readonly Sync<bool> OverrideOnBind;

    public ReferenceVariable()
    {
        Reference = new SyncRef<T>(this);
        OverrideOnBind = new Sync<bool>(this, false);
    }

    public override bool IsWriteOnly => false;

    public override bool OverridesOnBind => OverrideOnBind.Value;

    // A driven Reference would take the write and then lose it again on the drive's next pass, so
    // the scope says the write was refused instead of pretending it landed.
    public override bool AcceptsWrites => !Reference.IsDriven;

    protected override bool HasLocalValue => true;

    protected override T LocalValue
    {
        get => Reference.Target;
        // A destroyed element is dropped rather than stored: the ref would read back null anyway,
        // and keeping the dead RefID around makes the variable look bound when it isn't.
        set => Reference.Target = (value is { IsDestroyed: false } ? value : null)!;
    }
}
