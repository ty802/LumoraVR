// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

internal static class WorkerInitializer
{
    private static readonly ConcurrentDictionary<Type, WorkerInitInfo> Cache = new();

    internal static WorkerInitInfo GetInitInfo(Type workerType)
    {
        if (workerType == null)
            throw new ArgumentNullException(nameof(workerType));

        // Two threads building the same type both finish, and both get the one that went in first.
        if (!Cache.TryGetValue(workerType, out var info))
            info = Cache.GetOrAdd(workerType, Initialize(workerType));

        return info;
    }

    // Every concrete worker type in the assembly, ahead of its first use. Returns how many.
    internal static int WarmUp(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        int count = 0;
        foreach (var type in types)
        {
            if (type == null || type.IsAbstract || type.ContainsGenericParameters || !typeof(Worker).IsAssignableFrom(type))
                continue;
            try
            {
                GetInitInfo(type);
                count++;
            }
            catch (Exception)
            {
                // A type that cannot be read here is read, and reported, when something first makes one.
            }
        }
        return count;
    }

    internal static WorkerInitInfo GetInitInfo(IWorker worker)
    {
        if (worker == null)
            throw new ArgumentNullException(nameof(worker));

        return GetInitInfo(worker.GetType());
    }

    private static WorkerInitInfo Initialize(Type workerType)
    {
        var info = new WorkerInitInfo();
        var fields = new List<FieldInfo>();
        GatherWorkerFields(workerType, fields);

        info.SyncMemberFields = fields.ToArray();
        info.FillSyncMembers = BuildMemberFill(workerType, info.SyncMemberFields);
        info.SyncMemberNames = new string[info.SyncMemberFields.Length];
        info.SyncMemberNonpersistent = new bool[info.SyncMemberFields.Length];
        info.SyncMemberNondrivable = new bool[info.SyncMemberFields.Length];
        info.SyncMemberDontCopy = new bool[info.SyncMemberFields.Length];
        info.DefaultValues = new object[info.SyncMemberFields.Length];
        info.SyncMemberNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < info.SyncMemberFields.Length; i++)
        {
            var field = info.SyncMemberFields[i];

            info.SyncMemberNonpersistent[i] = field.GetCustomAttribute<NonPersistentAttribute>() != null;
            info.SyncMemberNondrivable[i] = field.GetCustomAttribute<NonDrivableAttribute>() != null;
            info.SyncMemberDontCopy[i] = field.GetCustomAttribute<DontCopyAttribute>() != null;

            string name = field.GetCustomAttribute<NameOverrideAttribute>()?.Name!;
            if (string.IsNullOrEmpty(name))
            {
                name = field.Name;
                if (name.EndsWith("_Field", StringComparison.Ordinal) && name != "_Field")
                {
                    name = name[..name.LastIndexOf("_Field", StringComparison.Ordinal)];
                }
            }

            info.SyncMemberNames[i] = name;
            info.SyncMemberNameToIndex[name] = i;

            foreach (var oldName in field.GetCustomAttributes<OldNameAttribute>(inherit: false))
            {
                if (info.OldSyncMemberNames == null)
                {
                    info.OldSyncMemberNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                }

                if (!info.OldSyncMemberNames.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    info.OldSyncMemberNames.Add(name, list);
                }

                if (oldName.OldNames != null)
                {
                    foreach (var item in oldName.OldNames)
                    {
                        list.Add(item);
                    }
                }
            }

            if (field.GetCustomAttribute<DefaultValueAttribute>() is { } defaultValue)
            {
                info.DefaultValues[i] = defaultValue.Default;
            }
        }

        if (typeof(ComponentBase<>).IsAssignableFromGeneric(workerType))
        {
            var methodOrigin = workerType.FindGenericBaseClass(typeof(ComponentBase<>));
            bool isComponent = typeof(Component).IsAssignableFrom(workerType);

            // The origin for OnCommonUpdate depends on which branch of the hierarchy we are on. Component
            // overrides OnCommonUpdate purely as plumbing to forward into OnUpdate, so measuring that
            // override against ComponentBase<> answers "true" for EVERY component in the engine and puts
            // all of them on the per-frame dispatch - thousands of no-op InternalRunUpdate calls whose only
            // effect is to call two empty virtuals. For a component the real question is whether it
            // overrides PAST Component. -xlinka
            var commonOrigin = isComponent ? typeof(Component) : methodOrigin;
            var hasUpdateMethods = workerType.OverridesMethod("OnCommonUpdate", commonOrigin) |
                                   workerType.OverridesMethod("OnBehaviorUpdate", methodOrigin);

            if (isComponent)
            {
                hasUpdateMethods |= workerType.OverridesMethod("OnUpdate", typeof(Component));
                info.HasLateUpdateMethod = workerType.OverridesMethod("OnLateUpdate", typeof(Component));
            }

            info.HasUpdateMethods = hasUpdateMethods;
            info.HasLinkedMethod = workerType.OverridesMethod("OnLinked", methodOrigin);
            info.HasUnlinkedMethod = workerType.OverridesMethod("OnUnlinked", methodOrigin);
            info.HasAudioUpdateMethod = workerType.OverridesMethod("OnAudioUpdate", methodOrigin);
            info.HasAudioConfigurationChangedMethod = workerType.OverridesMethod("OnAudioConfigurationChanged", methodOrigin);

            var eventCount = Enum.GetValues(typeof(World.WorldEvent)).Length;
            info.ReceivesWorldEvent = new bool[eventCount];
            for (int i = 0; i < eventCount; i++)
            {
                var worldEvent = (World.WorldEvent)i;
                if (workerType.OverridesMethod(worldEvent.ToString(), methodOrigin))
                {
                    info.ReceivesWorldEvent[i] = true;
                    info.ReceivesAnyWorldEvent = true;
                }
            }
        }

        var listedMethods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        GatherListedMethods(workerType, listedMethods);
        info.ListedMethods = new MethodInfo[listedMethods.Count];
        info.ListedMethodNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int listedIndex = 0;
        foreach (var entry in listedMethods)
        {
            info.ListedMethods[listedIndex] = entry.Value;
            info.ListedMethodNameToIndex[entry.Key] = listedIndex;
            listedIndex++;
        }

        info.SingleInstancePerSlot = workerType.GetCustomAttribute<SingleInstancePerSlotAttribute>(inherit: true) != null;
        info.DontDuplicate = workerType.GetCustomAttribute<DontDuplicateAttribute>(inherit: true) != null;
        info.PreserveWithAssets = workerType.GetCustomAttribute<PreserveWithAssetsAttribute>(inherit: true) != null;
        info.RegisterGlobally = workerType.GetCustomAttribute<GloballyRegisteredAttribute>(inherit: true) != null;
        info.DefaultUpdateOrder = workerType.GetCustomAttribute<DefaultUpdateOrderAttribute>()?.Order ?? 0;

        return info;
    }

    // Collects every method opened to SyncDelegate binding with [SyncMethod], keyed by name. Walks the
    // hierarchy base-first so a derived override replaces the base entry for the same name; binding from
    // data is by name only, matching how the delegate value persists.
    private static void GatherListedMethods(Type workerType, Dictionary<string, MethodInfo> methods)
    {
        if (workerType == null || workerType == typeof(object))
            return;

        GatherListedMethods(workerType.BaseType!, methods);

        foreach (var method in workerType.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<SyncMethodAttribute>() != null)
            {
                methods[method.Name] = method;
            }
        }
    }

    // FieldInfo.GetValue costs a boxed reflection call every time, and a worker's members are read in
    // several passes at init and again on every save, duplicate and join. One compiled delegate per field
    // per type turns those into a plain field load. Discovery is untouched: same fields, same order, this
    // only changes how they are READ. -xlinka
    // One small method per worker type that reads every member field into a table in one call. It was a
    // compiled expression per FIELD, about 125 us each, so a world full of types it had not met yet paid
    // for thousands of compiles on its first open. -xlinka
    private static Action<Worker, ISyncMember?[]> BuildMemberFill(Type workerType, FieldInfo[] fields)
    {
        try
        {
            var method = new DynamicMethod("FillSyncMembers_" + workerType.Name, null,
                new[] { typeof(Worker), typeof(ISyncMember?[]) }, typeof(Worker).Module, skipVisibility: true);
            var il = method.GetILGenerator();
            for (int i = 0; i < fields.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, fields[i].DeclaringType!);
                il.Emit(OpCodes.Ldfld, fields[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Action<Worker, ISyncMember?[]>>();
        }
        catch (Exception)
        {
            // No runtime IL generation (a fully AOT'd target would be the case): the reflected read.
            // Slower, identical result.
            return (worker, table) =>
            {
                for (int i = 0; i < fields.Length; i++)
                    table[i] = fields[i].GetValue(worker) as ISyncMember;
            };
        }
    }

    private static void GatherWorkerFields(Type workerType, List<FieldInfo> fields)
    {
        if (workerType.BaseType != typeof(Worker))
        {
            GatherWorkerFields(workerType.BaseType!, fields);
        }

        var declared = workerType.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => IsValidField(f, workerType));

        fields.AddRange(declared);
    }

    private static bool IsValidField(FieldInfo field, Type workerType)
    {
        if (!typeof(ISyncMember).IsAssignableFrom(field.FieldType))
        {
            return false;
        }

        if (field.FieldType.IsInterface)
        {
            return false;
        }

        if (!field.IsInitOnly)
        {
            return false;
        }

        if (field.FieldType.IsAbstract)
        {
            throw new Exception($"Field {field.Name} on Worker {workerType} is abstract type {field.FieldType}");
        }

        return true;
    }

    private static bool OverridesMethod(this Type type, string methodName, Type methodOrigin)
    {
        if (type == null || methodOrigin == null)
        {
            return false;
        }

        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return method != null && method.DeclaringType != methodOrigin;
    }

    private static Type FindGenericBaseClass(this Type type, Type genericBase)
    {
        var current = type;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericBase)
            {
                return current;
            }

            current = current.BaseType;
        }

        return null!;
    }

    private static bool IsAssignableFromGeneric(this Type genericType, Type toCheck)
    {
        if (genericType == null || toCheck == null)
        {
            return false;
        }

        var current = toCheck;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
