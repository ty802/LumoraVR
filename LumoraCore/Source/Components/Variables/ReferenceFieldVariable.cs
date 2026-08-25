// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Variables;

// Exposes an EXISTING reference member under a variable name. Reference-typed twin of
// FieldVariable<T>: the referenced member stays the store, and writes through the scope re-point it.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements)]
public class ReferenceFieldVariable<T> : VariableBase<T> where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> TargetReference;

    // Adopt the identity's existing target on binding, or push this member's own into it.
    public readonly Sync<bool> OverrideOnBind;

    private SyncRef<T>? _watched;

    public ReferenceFieldVariable()
    {
        TargetReference = new SyncRef<SyncRef<T>>(this);
        OverrideOnBind = new Sync<bool>(this, false);
    }

    public override bool IsWriteOnly => false;

    public override bool OverridesOnBind => OverrideOnBind.Value;

    public override bool AcceptsWrites
    {
        get
        {
            var reference = TargetReference.Target;
            return reference != null && reference.CanWrite && !reference.IsDriven;
        }
    }

    protected override bool HasLocalValue => TargetReference.Target != null;

    protected override T LocalValue
    {
        get => TargetReference.Target is { } reference ? reference.Target : null!;
        set
        {
            if (AcceptsWrites)
                TargetReference.Target!.Target = (value is { IsDestroyed: false } ? value : null)!;
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        TargetReference.OnTargetChange += OnTargetReferenceChanged;
    }

    public override void OnStart()
    {
        RewatchTarget();
        base.OnStart();
    }

    public override void OnDestroy()
    {
        TargetReference.OnTargetChange -= OnTargetReferenceChanged;
        Unwatch();
        base.OnDestroy();
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        var reference = TargetReference.Target;
        InspectorStats.AddRow(ui, "Backing reference", reference == null ? "none" : reference.ParentHierarchyToString());
        if (reference != null && !AcceptsWrites)
            InspectorStats.AddRow(ui, "Writes", reference.IsDriven ? "refused (reference is driven)" : "refused (read only)");
    }

    private void OnTargetReferenceChanged(SyncRef<SyncRef<T>> reference) => RewatchTarget();

    private void RewatchTarget()
    {
        var next = TargetReference.Target;
        if (ReferenceEquals(next, _watched))
            return;

        Unwatch();
        _watched = next;
        if (_watched != null)
            _watched.Changed += OnTargetValueChanged;

        MarkChangeDirty();
    }

    private void Unwatch()
    {
        if (_watched != null)
            _watched.Changed -= OnTargetValueChanged;
        _watched = null;
    }

    private void OnTargetValueChanged(IChangeable element) => MarkChangeDirty();
}
