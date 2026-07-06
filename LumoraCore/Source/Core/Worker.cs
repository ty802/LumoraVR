// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

public abstract class Worker : IWorker
{
    protected readonly WorkerInitInfo InitInfo;

    public World World { get; private set; } = null!;
    public IWorldElement? Parent { get; private set; }

    public RefID ReferenceID { get; private set; } = RefID.Null;
    public bool IsLocalElement { get; private set; }
    public bool IsDestroyed { get; protected set; }
    public bool IsInitialized { get; private set; }

    public virtual bool IsPersistent => true;
    public virtual bool IsRemoved => IsDestroyed;
    public bool PreserveWithAssets => InitInfo.PreserveWithAssets;
    public bool DontDuplicate => InitInfo.DontDuplicate;
    public bool GloballyRegistered => InitInfo.RegisterGlobally;

    public Type WorkerType => GetType();
    public string WorkerTypeName => WorkerType.FullName!;

    public int SyncMemberCount => InitInfo.SyncMemberFields.Length;

    protected Worker()
    {
        InitInfo = WorkerInitializer.GetInitInfo(GetType());
    }

    public IEnumerable<ISyncMember> SyncMembers
    {
        get
        {
            for (int i = 0; i < SyncMemberCount; i++)
            {
                yield return GetSyncMember(i);
            }
        }
    }

    public virtual ISyncMember GetSyncMember(int index)
    {
        return (InitInfo.SyncMemberFields[index].GetValue(this) as ISyncMember) ?? null!;
    }

    public FieldInfo GetSyncMemberFieldInfo(int index)
    {
        return InitInfo.SyncMemberFields[index];
    }

    public string GetSyncMemberName(int index)
    {
        return InitInfo.SyncMemberNames[index];
    }

    // Methods this worker type opens to SyncDelegate binding from data, via [SyncMethod]. Used to
    // validate a delegate target resolved from a save or a peer (see SyncDelegate) and to enumerate
    // the available actions for tooling.
    public int ListedMethodCount => InitInfo.ListedMethods.Length;

    public MethodInfo GetSyncMethod(int index) => InitInfo.ListedMethods[index];

    public MethodInfo? GetSyncMethod(string name)
    {
        var index = IndexOfSyncMethod(name);
        return index >= 0 ? InitInfo.ListedMethods[index] : null;
    }

    public int IndexOfSyncMethod(string name)
        => !string.IsNullOrEmpty(name) && InitInfo.ListedMethodNameToIndex.TryGetValue(name, out var index) ? index : -1;

    public bool IsListedSyncMethod(string name) => IndexOfSyncMethod(name) >= 0;

    // SCHEDULING
    // Thin conveniences over the world's update-loop scheduler so a component can defer work without
    // reaching for World directly. All are no-ops once the worker has no world (detached/destroyed).

    public virtual void RunSynchronously(Action action) => World?.RunSynchronously(action);

    public virtual void RunInUpdates(int updateCount, Action action) => World?.RunInUpdates(updateCount, action);

    public void RunInSeconds(float seconds, Action action) => World?.RunInSeconds(seconds, action);

    public Task DelaySeconds(float seconds) => World?.DelaySeconds(seconds) ?? Task.CompletedTask;

    public void StartTask(Func<Task> task) => World?.StartTask(task);

    public void StartCoroutine(IEnumerator routine) => World?.StartCoroutine(routine);

    // VALUE COPY
    // Shallow copies of sync FIELD values between workers. References are copied as-is (shared with the
    // source), not deep-cloned - use Slot.Duplicate for a deep clone. Members flagged [DontCopy] and
    // non-field members (collections/delegates) are skipped.

    /// <summary>Copy field values from another worker of the same type, member-for-member by index.</summary>
    public void CopyValues(Worker source)
    {
        if (source == null || source.GetType() != GetType())
            return;

        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (InitInfo.SyncMemberDontCopy[i])
                continue;
            if (GetSyncMember(i) is IField target && source.GetSyncMember(i) is IField src)
            {
                try { target.BoxedValue = src.BoxedValue; }
                catch (Exception ex) { LumoraLogger.Warn($"CopyValues: member '{GetSyncMemberName(i)}' on {WorkerTypeName} failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>Copy field values from another worker by matching member NAME, so values transfer
    /// between different worker types that share field names. Mismatched value types are skipped.</summary>
    public void CopyProperties(Worker source)
    {
        if (source == null)
            return;

        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (InitInfo.SyncMemberDontCopy[i])
                continue;
            if (GetSyncMember(i) is not IField target)
                continue;

            var src = source.TryGetField(GetSyncMemberName(i));
            if (src == null)
                continue;

            try { target.BoxedValue = src.BoxedValue; }
            catch (Exception ex) { LumoraLogger.Warn($"CopyProperties: member '{GetSyncMemberName(i)}' on {WorkerTypeName} failed: {ex.Message}"); }
        }
    }

    // PERSISTENCE
    // Serialize this worker as { ID, memberName: member.Save(), ... }. Slot overrides to also write
    // its child slots; a slot's components live in its "Components" member and serialize through it.

    public virtual DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("ID", control.SaveReference(ReferenceID));
        for (int i = 0; i < SyncMemberCount; i++)
        {
            var member = GetSyncMember(i);
            if (member == null || !ShouldSerializeMember(member))
                continue;
            var memberName = GetSyncMemberName(i);
            try
            {
                dictionary.Add(memberName, member.Save(control));
            }
            catch (NotSupportedException)
            {
                // The member's value type has no coder (e.g. a SyncField<object> holding a runtime
                // value). For such members, still record the member's identity ("<name>-ID" = its
                // RefID) so references to it resolve on load, instead of dropping the member and
                // aborting the save.
                dictionary.Add(memberName + "-ID", control.SaveReference(member.ReferenceID));
            }
        }
        return dictionary;
    }

    public virtual void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;

        var idNode = dictionary.TryGetNode("ID");
        if (idNode != null)
            control.AssociateReference(ReferenceID, idNode);

        for (int i = 0; i < SyncMemberCount; i++)
        {
            var member = GetSyncMember(i);
            if (member == null || !ShouldSerializeMember(member))
                continue;
            var memberName = GetSyncMemberName(i);
            var memberNode = dictionary.TryGetNode(memberName);

            // Member was renamed since this save was written: fall back to any [OldName] alias so the
            // old key still loads into the new member.
            if (memberNode == null && InitInfo.OldSyncMemberNames != null
                && InitInfo.OldSyncMemberNames.TryGetValue(memberName, out var oldNames))
            {
                foreach (var oldName in oldNames)
                {
                    memberNode = dictionary.TryGetNode(oldName);
                    if (memberNode != null)
                        break;
                }
            }

            if (memberNode != null)
            {
                try
                {
                    member.Load(memberNode, control);
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"Worker.Load: member '{memberName}' on {WorkerTypeName} failed: {ex.Message}");
                }
                continue;
            }

            // Saved as identity-only ("<name>-ID" for non-codeable/non-persistent members):
            // register the member's RefID so references to it still resolve on load.
            var memberIdNode = dictionary.TryGetNode(memberName + "-ID");
            if (memberIdNode != null)
                control.AssociateReference(member.ReferenceID, memberIdNode);
        }
    }

    /// <summary>Whether a member is serialized (both saved and loaded). Override to skip ones
    /// handled specially by the worker (e.g. a slot serializes its components itself).</summary>
    protected virtual bool ShouldSerializeMember(ISyncMember member) => true;

    public int IndexOfMember(ISyncMember member)
    {
        if (member == null)
            return -1;

        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (GetSyncMember(i) == member)
            {
                return i;
            }
        }
        return -1;
    }

    public IField TryGetField(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null!;

        if (InitInfo.SyncMemberNameToIndex.TryGetValue(name, out var index))
        {
            return (GetSyncMember(index) as IField) ?? null!;
        }

        return null!;
    }

    public IField<T> TryGetField<T>(string name)
    {
        return (TryGetField(name) as IField<T>) ?? null!;
    }

    // Every object this worker points at through its sync data: each reference member yields its target,
    // and sync lists / nested sync objects are descended into. Used by duplication (remap references into a
    // copied subtree), saving (resolve outgoing references), and asset gathering. assetRefOnly keeps only
    // references to assets (or targets flagged to travel with assets); persistentOnly drops references to
    // non-persistent targets. -xlinka
    public virtual IEnumerable<IWorldElement> GetReferencedObjects(bool assetRefOnly, bool persistentOnly = true)
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            var member = GetSyncMember(i);
            if (member == null)
            {
                continue;
            }

            foreach (var referenced in CollectMemberReferences(member, assetRefOnly, persistentOnly))
            {
                yield return referenced;
            }
        }
    }

    private static IEnumerable<IWorldElement> CollectMemberReferences(ISyncMember member, bool assetRefOnly, bool persistentOnly)
    {
        if (member is ISyncRef syncRef)
        {
            var target = syncRef.Target;
            if (target == null)
            {
                yield break;
            }

            bool isAsset = syncRef is IAssetRef
                || target is IAssetProvider
                || (target is Worker worker && worker.PreserveWithAssets);

            if (assetRefOnly && !isAsset)
            {
                yield break;
            }
            // Asset refs are kept even when non-persistent (the asset still has to travel with the content).
            if (persistentOnly && !target.IsPersistent && !isAsset)
            {
                yield break;
            }

            yield return target;
        }
        else if (member is ISyncList list)
        {
            foreach (var element in list.Elements)
            {
                if (element is ISyncMember childMember)
                {
                    foreach (var referenced in CollectMemberReferences(childMember, assetRefOnly, persistentOnly))
                    {
                        yield return referenced;
                    }
                }
            }
        }
        else if (member is ISyncObject syncObject)
        {
            foreach (var childMember in syncObject.SyncMembers)
            {
                foreach (var referenced in CollectMemberReferences(childMember, assetRefOnly, persistentOnly))
                {
                    yield return referenced;
                }
            }
        }
    }

    public virtual string ParentHierarchyToString()
    {
        return WorkerType.Name;
    }

    protected virtual void InitializeSyncMembers()
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            var field = InitInfo.SyncMemberFields[i];
            var member = field.GetValue(this) as ISyncMember;
            if (member == null)
            {
                member = Activator.CreateInstance(field.FieldType) as ISyncMember;
                if (member == null)
                {
                    throw new InvalidOperationException($"Failed to create sync member for field {field.Name} on {GetType().FullName}");
                }
                field.SetValue(this, member);
            }

            if (member is SyncElement syncElement)
            {
                if (InitInfo.SyncMemberNonpersistent[i])
                {
                    syncElement.MarkNonPersistent();
                }
                if (InitInfo.SyncMemberNondrivable[i])
                {
                    syncElement.MarkNonDrivable();
                }
            }
        }
    }

    protected virtual void InitializeSyncMemberDefaults()
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (InitInfo.DefaultValues[i] == null)
            {
                continue;
            }

            if (GetSyncMember(i) is IField field)
            {
                field.BoxedValue = InitInfo.DefaultValues[i];
            }
        }
    }

    protected void AssignSyncMemberMetadata()
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            var member = GetSyncMember(i);
            if (member == null)
            {
                continue;
            }

            member.MemberIndex = i;
            member.Name = InitInfo.SyncMemberNames[i];
        }
    }

    protected void InitializeWorker(IWorldElement parent)
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));

        InitializeWorker(parent.World, parent);
    }

    protected void InitializeWorker(World world, IWorldElement? parent)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        try
        {
            World = world;
            Parent = parent;

            InitializeSyncMembers();
            AssignSyncMemberMetadata();

            ReferenceID = World.ReferenceController.AllocateID();
            IsLocalElement = ReferenceID.IsLocalID;

            for (int i = 0; i < SyncMemberCount; i++)
            {
                var member = GetSyncMember(i);
                if (member != null && member.World == null)
                {
                    member.Initialize(World, this);
                }
            }

            InitializeSyncMemberDefaults();
            HookSyncMemberChanges();

            World.ReferenceController.RegisterObject(this);
            PostInitializeWorker();
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            throw new Exception($"Exception during initializing Worker of type {GetType().FullName}", ex);
        }
    }

    protected virtual void PostInitializeWorker()
    {
    }

    protected virtual void SyncMemberChanged(IChangeable member)
    {
    }

    protected void EndInitializationStageForMembers()
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            var member = GetSyncMember(i);
            if (member != null && member.IsInInitPhase)
            {
                member.EndInitPhase();
            }
        }
    }

    protected void PrepareMembersForDestroy()
    {
    }

    public virtual void Dispose()
    {
        if (World == null && Parent == null)
        {
            return;
        }

        for (int i = 0; i < SyncMemberCount; i++)
        {
            GetSyncMember(i)?.Dispose();
        }

        World?.ReferenceController?.UnregisterObject(this);
        World = null!;
        Parent = null;
        IsDestroyed = true;
    }

    private void HookSyncMemberChanges()
    {
        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (GetSyncMember(i) is IChangeable changeable)
            {
                changeable.Changed += SyncMemberChangedInternal;
            }
        }
    }

    private void SyncMemberChangedInternal(IChangeable member)
    {
        SyncMemberChanged(member);
    }
}
