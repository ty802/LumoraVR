// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components.Variables;

// Exposes an EXISTING field under a variable name instead of owning a value of its own. The field
// stays the single source of truth: reads through the scope report what the field holds, writes
// through the scope land in the field.
//
// Use this to publish something the content already has - a slider's value, a light's intensity -
// without duplicating it into a second member that then has to be kept in step. -xlinka
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class FieldVariable<T> : VariableBase<T>
{
    public readonly SyncRef<IField<T>> TargetField;

    // Adopt the identity's existing value on binding, or push the field's own value into it.
    public readonly Sync<bool> OverrideOnBind;

    private IField<T>? _watched;

    public FieldVariable()
    {
        TargetField = new SyncRef<IField<T>>(this);
        OverrideOnBind = new Sync<bool>(this, false);
    }

    // See Variables.IsSupportedValueType.
    public static bool IsValidGenericType => Variables.IsSupportedValueType(typeof(T));

    public override bool IsWriteOnly => false;

    public override bool OverridesOnBind => OverrideOnBind.Value;

    // A field that is read-only, or already driven by something else, will not take the write, and
    // the scope reports that instead of pretending the write landed.
    public override bool AcceptsWrites
    {
        get
        {
            var field = TargetField.Target;
            return field != null && field.CanWrite && !field.IsDriven;
        }
    }

    // With no target there is nothing to publish, so the variable exists but contributes nothing
    // until the reference resolves.
    protected override bool HasLocalValue => TargetField.Target != null;

    protected override T LocalValue
    {
        get => TargetField.Target is { } field ? field.Value : default!;
        set
        {
            if (AcceptsWrites)
                TargetField.Target!.Value = value;
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        TargetField.OnTargetChange += OnTargetReferenceChanged;
    }

    public override void OnStart()
    {
        // Watch before the first bind so the field's current value is what gets published.
        RewatchTarget();
        base.OnStart();
    }

    public override void OnDestroy()
    {
        TargetField.OnTargetChange -= OnTargetReferenceChanged;
        Unwatch();
        base.OnDestroy();
    }

    protected override void BuildExtraInspectorRows(UIBuilder ui)
    {
        var field = TargetField.Target;
        InspectorStats.AddRow(ui, "Backing field", field == null ? "none" : field.ParentHierarchyToString());
        if (field != null && !AcceptsWrites)
            InspectorStats.AddRow(ui, "Writes", field.IsDriven ? "refused (field is driven)" : "refused (read only)");
    }

    private void OnTargetReferenceChanged(SyncRef<IField<T>> reference) => RewatchTarget();

    // The field is the store, so its own change event is what tells this variable to republish. No
    // polling, and no per-frame read of a field that may not have moved.
    private void RewatchTarget()
    {
        var next = TargetField.Target;
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
