// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// A member whose ELEMENT TYPE is part of its state: it holds one child sync member and replicates
// both which type that member is and the member itself. Everything else in the datamodel fixes the
// type at compile time, which is fine for a component that knows what it stores and useless for
// anything whose shape is decided at runtime - a node graph slot, a value keyed by whatever a person
// dropped on it.
//
// The child is a real element with its own RefID, so it syncs, persists, drives and shows in the
// inspector exactly like a declared member would. Swapping the type destroys the old child; the
// replacement lands on a fresh RefID rather than reusing the old one, because a peer holding the old
// element must not silently reinterpret it as the new type.
//
// INSTANTIATION IS GATED. The type name arrives from a save file or a peer, so it is checked against
// what a sync element can be before anything is constructed: no arbitrary Activator.CreateInstance on
// attacker-chosen names. -xlinka
public class SyncVariant : ConflictingSyncElement, ISyncMemberCopy
{
    private SyncElement? _element;

    public SyncElement? Element => _element;

    public bool IsNull => _element == null;

    public event Action<SyncVariant>? ElementChanged;

    // Assigning a different type replaces the child; assigning null clears it.
    public Type? ElementType
    {
        get => _element?.GetType();
        set
        {
            if (value == ElementType)
                return;
            if (value == null)
            {
                InternalSetElement(null);
                return;
            }
            if (!IsSupportedElementType(value))
                throw new ArgumentException($"{value} is not a usable sync element type", nameof(value));
            InternalCreateElement(RefID.Null, value);
        }
    }

    // Constructible, concrete, and a sync element. Everything the datamodel can hold lives under
    // SyncElement; a Worker (component, slot) does not, and must never be reachable from here.
    public static bool IsSupportedElementType(Type? type)
        => type != null
        && !type.IsAbstract
        && !type.IsGenericTypeDefinition
        && typeof(SyncElement).IsAssignableFrom(type)
        && type.GetConstructor(Type.EmptyTypes) != null;

    public void SetNull() => ElementType = null;

    // TYPED ACCESS
    // Each of these installs the matching element type when it differs, then hands back the child cast
    // to it. Pass setIfDifferent: false to inspect without changing the type - a null result then means
    // "holds something else", not "empty".

    public Sync<T>? AsField<T>(bool setIfDifferent = true)
    {
        if (setIfDifferent)
            ElementType = typeof(Sync<T>);
        return _element as Sync<T>;
    }

    public SyncRef<T>? AsRef<T>(bool setIfDifferent = true) where T : class, IWorldElement
    {
        if (setIfDifferent)
            ElementType = typeof(SyncRef<T>);
        return _element as SyncRef<T>;
    }

    public SyncList<T>? AsList<T>(bool setIfDifferent = true) where T : SyncElement, new()
    {
        if (setIfDifferent)
            ElementType = typeof(SyncList<T>);
        return _element as SyncList<T>;
    }

    public SyncArray<T>? AsArray<T>(bool setIfDifferent = true)
    {
        if (setIfDifferent)
            ElementType = typeof(SyncArray<T>);
        return _element as SyncArray<T>;
    }

    public SyncFieldList<T>? AsFieldList<T>(bool setIfDifferent = true)
    {
        if (setIfDifferent)
            ElementType = typeof(SyncFieldList<T>);
        return _element as SyncFieldList<T>;
    }

    public SyncList<SyncVariant>? AsVariantList(bool setIfDifferent = true)
    {
        if (setIfDifferent)
            ElementType = typeof(SyncList<SyncVariant>);
        return _element as SyncList<SyncVariant>;
    }

    public SyncElementDictionary<string, SyncVariant>? AsVariantDictionary(bool setIfDifferent = true)
    {
        if (setIfDifferent)
            ElementType = typeof(SyncElementDictionary<string, SyncVariant>);
        return _element as SyncElementDictionary<string, SyncVariant>;
    }

    public IField? AsGenericField() => _element as IField;

    public T GetValue<T>(bool setIfDifferent = true) => AsField<T>(setIfDifferent) is { } field ? field.Value : default!;

    public bool TryGetValue<T>(out T value)
    {
        if (AsField<T>(setIfDifferent: false) is { } field)
        {
            value = field.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void SetValue<T>(T value, bool setIfDifferent = true)
    {
        var field = AsField<T>(setIfDifferent);
        if (field != null)
            field.Value = value;
    }

    private SyncElement InternalCreateElement(RefID id, Type elementType, bool sync = true, bool change = true)
    {
        if (id != RefID.Null)
        {
            World.ReferenceController.AllocationBlockBegin(id);
        }
        else if (IsLocalElement)
        {
            World.ReferenceController.LocalAllocationBlockBegin();
        }

        var element = (SyncElement)Activator.CreateInstance(elementType)!;
        element.Initialize(World, this);

        if (id != RefID.Null)
        {
            World.ReferenceController.AllocationBlockEnd();
        }
        else if (IsLocalElement)
        {
            World.ReferenceController.LocalAllocationBlockEnd();
        }

        InternalSetElement(element, sync, change);
        return element;
    }

    private void InternalSetElement(SyncElement? element, bool sync = true, bool change = true)
    {
        if (!IsLoading && !IsInInitPhase)
        {
            AuthorizeDataModelMutation(
                DataModelPermissionAction.Write | DataModelPermissionAction.Create,
                DataModelPermissionSurface.SyncElement);
        }

        BeginModification();

        var previous = _element;
        bool trashPrevious = false;
        _element = element;

        if (IsInInitPhase)
        {
            if (element != null)
                RegisterNewInitializable(element);
        }
        else
        {
            if (element != null && element.IsInInitPhase)
                element.EndInitPhase();

            if (sync && GenerateSyncData)
            {
                // A peer that never heard about the previous child would resolve a delta naming it
                // against nothing, so the old element is kept recoverable until the swap is confirmed.
                trashPrevious = !IsSyncDirty;
                InvalidateSyncElement();
            }
        }

        BlockModification();
        if (change)
        {
            try
            {
                ElementChanged?.Invoke(this);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"Exception running ElementChanged on {this.ParentHierarchyToString()}:\n{ex}");
            }
        }
        UnblockModification();

        if (previous != null)
        {
            if (trashPrevious)
                World.ReferenceController.MoveToTrash(previous, World.SyncTick);
            else
                previous.Dispose();
        }

        EndModification();
    }

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not SyncVariant other || ReferenceEquals(other, this))
            return;

        ElementType = other.ElementType;
        if (_element != null && other._element != null)
            copyChild(other._element, _element);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        if (_element == null)
        {
            writer.Write7BitEncoded((ulong)RefID.Null);
            return;
        }
        writer.Write7BitEncoded((ulong)_element.ReferenceID);
        World.Workers.EncodeType(writer, _element.GetType());
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        var id = new RefID(reader.Read7BitEncoded());
        if (id == RefID.Null)
        {
            InternalSetElement(null, sync: false);
            return;
        }

        var elementType = World.Workers.DecodeType(reader);
        if (!IsSupportedElementType(elementType))
        {
            LumoraLogger.Error($"SyncVariant {ReferenceID}: refusing element type '{elementType?.FullName ?? "<unresolved>"}' from the network.");
            InternalSetElement(null, sync: false);
            return;
        }

        var tick = inboundMessage is ConfirmationMessage confirm ? confirm.ConfirmTime : World.SyncTick;
        if (World.ReferenceController.TryRetrieveFromTrash(tick, id) is SyncElement restored && restored.GetType() == elementType)
        {
            InternalSetElement(restored, sync: false);
            return;
        }

        InternalCreateElement(id, elementType, sync: false);
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
    }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Type", new DataTreeValue(_element?.GetType().FullName));
        if (_element != null)
            dictionary.Add("Data", _element.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;

        string? typeName = dictionary.TryGetNode("Type") is DataTreeValue { IsNull: false } value
            ? value.Extract<string>()
            : null;

        if (string.IsNullOrEmpty(typeName))
        {
            InternalSetElement(null, sync: false);
            return;
        }

        // Quiet lookup: the message below already says what happened, in terms that mention the member.
        WorkerManager.TryGetType(typeName!, out var elementType);
        if (!IsSupportedElementType(elementType))
        {
            LumoraLogger.Error($"SyncVariant {ReferenceID}: saved element type '{typeName}' is not a usable sync element, loading empty.");
            InternalSetElement(null, sync: false);
            return;
        }

        var element = InternalCreateElement(RefID.Null, elementType, sync: false);
        if (dictionary.TryGetNode("Data") is { } data)
            element.Load(data, control);
    }

    // RAW DATA TREE BRIDGE
    // Moves a plain data tree in and out of the element form, so data that arrived as a file (or is
    // headed back into one) can be held as real, drivable, inspectable members instead of an opaque
    // node. Only the shapes a variant can actually hold are covered - a value, a list of variants, a
    // string-keyed map of variants - which is exactly the shape of a data tree.
    //
    // This is a CONVERSION, not preservation: anything a variant cannot represent has nowhere to go.
    // Data that must come back byte-for-byte belongs in a SyncRawData, which keeps the node itself. -xlinka

    public DataTreeNode ToRawDataTreeNode()
    {
        switch (_element)
        {
            case null:
                return new DataTreeValue((IConvertible?)null);

            case IField<Uri> url:
                return new DataTreeValue(url.Value);

            case IField<string> text:
                return new DataTreeValue(text.Value);

            case IField field:
                return new DataTreeValue(field.BoxedValue as IConvertible);

            case SyncList<SyncVariant> list:
            {
                var node = new DataTreeList();
                foreach (var child in list)
                    node.Add(child.ToRawDataTreeNode());
                return node;
            }

            case SyncElementDictionary<string, SyncVariant> dictionary:
            {
                var node = new DataTreeDictionary();
                foreach (var (key, child) in dictionary)
                    node.Add(key, child.ToRawDataTreeNode());
                return node;
            }

            default:
                throw new NotSupportedException($"SyncVariant holds a {_element.GetType()}, which has no data tree form.");
        }
    }

    public void FromRawDataTreeNode(DataTreeNode node)
    {
        switch (node)
        {
            case DataTreeValue { IsNull: true }:
            case null:
                SetNull();
                return;

            case DataTreeValue { IsUrl: true } url:
                SetValue(url.ExtractUrl());
                return;

            case DataTreeValue value:
            {
                // A stored string carries the '@' escaping the format put on it, so it comes back
                // through Extract rather than off the raw value.
                if (value.Value is string)
                {
                    SetValue(value.Extract<string>());
                    return;
                }
                ElementType = typeof(Sync<>).MakeGenericType(value.Value!.GetType());
                ((IField)_element!).BoxedValue = value.Value;
                return;
            }

            case DataTreeList list:
            {
                var elements = AsVariantList()!;
                elements.Clear();
                foreach (var child in list.Children)
                    elements.Add().FromRawDataTreeNode(child);
                return;
            }

            case DataTreeDictionary dictionary:
            {
                var entries = AsVariantDictionary()!;
                entries.Clear();
                foreach (var (key, child) in dictionary.Children)
                    entries.Add(key).FromRawDataTreeNode(child);
                return;
            }

            default:
                throw new NotSupportedException($"SyncVariant cannot take a {node.GetType()}.");
        }
    }

    public override object? GetValueAsObject() => _element?.GetValueAsObject();

    public override string ToString() => ElementType?.Name ?? "<null>";

    public override void Dispose()
    {
        ElementChanged = null;
        _element?.Dispose();
        _element = null;
        base.Dispose();
    }
}
