// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

public abstract class SyncField<T> : ConflictingSyncElement, IField<T>
{
    protected T _value;

    public Func<T, IField<T>, T>? LocalFilter;

    private ILinkRef? _directLink;
    private ILinkRef? _inheritedLink;

    // Flag check mask for hook bypass: init(5) + hookcallback(10) + loading(9)
    private const int HOOK_CHECK_FLAGS = 0x620;

    #region IField Implementation

    object IField.BoxedValue
    {
        get => Value!;
        set => Value = (T)value;
    }

    public Type ValueType => typeof(T);

    public virtual bool CanWrite => true;

    #endregion

    #region Value Property

    // Setting this will trigger network synchronization. When driven/linked, the value comes from the drive
    // source. If hooked and modification not allowed, calls the hook instead.
    public virtual T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (IsHooked && !ActiveLink!.IsModificationAllowed &&
                (_flags & HOOK_CHECK_FLAGS) == 0 &&
                ActiveLink is FieldHook<T> fieldHook && fieldHook.ValueSetHook != null)
            {
                try
                {
                    BeginHook();
                    fieldHook.ValueSetHook(this, value);
                    return;
                }
                finally
                {
                    EndHook();
                }
            }

            if (LocalFilter != null)
            {
                value = LocalFilter(value, this);
            }

            InternalSetValue(in value);
        }
    }

    // Bypasses the hook machinery.
    public T DirectValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => InternalSetValue(in value);
    }

    #endregion

    #region Events

    public event SyncFieldEvent<T>? OnValueChange;

    public event Action<T>? OnChanged;

    public event Action<IChangeable>? Changed;

    #endregion

    #region Linking

    // IsLinked / IsDriven / IsHooked live on SyncElement and read through ResolveActiveLink, so the
    // base class's drive gates (sync suppression, inbound-delta ignore, IsBlockedByDrive) and this
    // public ILinkable surface can never disagree about whether the field is driven. -xlinka

    public ILinkRef? ActiveLink => _inheritedLink ?? _directLink;

    protected override ILinkRef? ResolveActiveLink() => _inheritedLink ?? _directLink;

    public ILinkRef? DirectLink => _directLink;

    public ILinkRef? InheritedLink => _inheritedLink;

    public IEnumerable<ILinkable>? LinkableChildren => null;

    ILinkRef? ILinkable.ActiveLink => ActiveLink;

    public void Link(ILinkRef link)
    {
        _directLink = link;
        if (link == ActiveLink)
        {
            UpdateLinkHierarchy(link);
        }
        SyncElementChanged();
    }

    public void InheritLink(ILinkRef link)
    {
        _inheritedLink = link;
        UpdateLinkHierarchy(link);
        SyncElementChanged();
    }

    public void ReleaseLink(ILinkRef link)
    {
        if (_directLink == link)
        {
            _directLink = null;
            UpdateLinkHierarchy(link);
            SyncElementChanged();
        }
    }

    public void ReleaseInheritedLink(ILinkRef link)
    {
        if (_inheritedLink != link)
            throw new InvalidOperationException("The link being released isn't the one currently inherited");

        _inheritedLink = null;
        UpdateLinkHierarchy(link);
        SyncElementChanged();
    }

    protected void UpdateLinkHierarchy(ILinkRef changedLink)
    {
        if (IsDisposed)
            return;

        if (changedLink.WasLinkGranted && changedLink.IsDriving)
        {
            // The drive that was controlling this field is going away. Register it so the sync loop
            // re-broadcasts our real current value to peers - while driven we suppressed deltas, so
            // they're holding the last driven value and won't otherwise hear that it's free again.
            // Register first (while we can still confirm the link was granted+driving), then invalidate.
            // ReleaseLink/ReleaseInheritedLink already null the link before we get here, so IsDriven is
            // false by now, which is exactly what the drain wants to confirm. -xlinka
            World?.LinkManager?.DriveReleased(this);
            Invalidate();
        }

        // Fields don't have linkable children
    }

    #endregion

    #region Internal Value Setting

    protected virtual bool InternalSetValue(in T value, bool sync = true, bool change = true)
    {
        if (BeginModification(throwOnError: false))
        {
            _value = value;

            if (sync && GenerateSyncData)
            {
                InvalidateSyncElement();
            }

            if (change)
            {
                BlockModification();
                ValueChanged();
                UnblockModification();
            }

            EndModification();
            return true;
        }
        return false;
    }

    protected virtual void ValueChanged()
    {
        SyncElementChanged();
        OnValueChange?.Invoke(this);
        OnChanged?.Invoke(_value);
    }

    protected void SyncElementChanged(IChangeable member = null!)
    {
        member = member ?? this;
        try
        {
            Changed?.Invoke(member);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Exception in SyncElementChanged: {ex}");
        }

        if (member == this)
        {
            WasChanged = true;
        }
    }

    // Bypasses the equality check.
    public void ForceSet(T value)
    {
        InternalSetValue(in value);
    }

    internal void SetValueSilently(T value, bool change = true)
    {
        InternalSetValue(in value, sync: false, change: change);
    }

    // Bypasses the IsDriven check so drives can push values.
    internal void SetDrivenValue(T value)
    {
        if (SyncCoder.Equals(_value, value)) return;

        _value = value;
        WasChanged = true;

        if (GenerateSyncData)
        {
            InvalidateSyncElement();
        }

        ValueChanged();
    }

    // For drives whose source state replicates on its own and whose computation runs on every peer (avatar pose
    // driving) - broadcasting the result would duplicate the source traffic and fight the remote peer's own
    // computation.
    internal void SetDrivenValueLocal(T value)
    {
        if (SyncCoder.Equals(_value, value)) return;

        _value = value;
        WasChanged = true;

        ValueChanged();
    }

    #endregion

    #region Constructors

    public SyncField()
    {
        _value = SyncCoder.GetDefault<T>();
    }

    protected SyncField(T init)
    {
        _value = init;
    }

    #endregion

    #region Encoding/Decoding

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        SyncCoder.Encode(writer, _value);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        T value = SyncCoder.Decode<T>(reader);
        InternalSetValue(in value, sync: false);
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        InternalEncodeFull(writer, outboundMessage);
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        InternalDecodeFull(reader, inboundMessage);
    }

    protected override void InternalClearDirty()
    {
        // No additional dirty state to clear
    }

    public void Encode(BinaryWriter writer)
    {
        SyncCoder.Encode(writer, _value);
    }

    public void Decode(BinaryReader reader)
    {
        T value = SyncCoder.Decode<T>(reader);
        InternalSetValue(in value, sync: false);
    }

    #endregion

    #region Disposal

    public override void Dispose()
    {
        OnValueChange = null;
        Changed = null;
        _directLink = null;
        _inheritedLink = null;
        base.Dispose();
    }

    #endregion

    #region Utility

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T(SyncField<T> field) => field.Value;

    public override string ToString() => _value?.ToString() ?? "<null>";

    public override object? GetValueAsObject() => _value;

    public override DataTreeNode Save(SaveControl control)
    {
        if (!DataTreeCoder.IsSupported(typeof(T)))
            throw new NotSupportedException($"SyncField<{typeof(T).Name}> has no persistence coder.");
        return DataTreeCoder.Encode(Value);
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        var value = DataTreeCoder.Decode<T>(node);
        // Pure load: don't generate network sync data or fire change events.
        InternalSetValue(in value, sync: false, change: false);
    }

    public override string ParentHierarchyToString()
    {
        var memberName = ((ISyncMember)this).Name ?? GetType().Name;
        if (_parent is Slot slot)
            return $"{slot.Name?.Value ?? "?"}/{memberName}";
        if (_parent != null)
            return $"{_parent.GetType().Name}/{memberName}";
        return memberName;
    }

    // Alias for WasChanged, for hook compatibility.
    public bool IsDirty => WasChanged;

    // Hooks use this to check and acknowledge in one call.
    public bool GetWasChangedAndClear()
    {
        bool changed = WasChanged;
        WasChanged = false;
        return changed;
    }

    #endregion
}

public class Sync<T> : SyncField<T>
{
    public new event Action<T>? OnChanged;

    public override T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (!SyncCoder.Equals(_value, value))
            {
                base.Value = value;
            }
        }
    }

    protected override void ValueChanged()
    {
        base.ValueChanged();
        OnChanged?.Invoke(_value);
    }

    public Sync() : base()
    {
    }

    public Sync(T init) : base(init)
    {
    }

    public Sync(IWorldElement? owner, T init) : base(init)
    {
        _parent = owner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T(Sync<T> field) => field.Value;
}
