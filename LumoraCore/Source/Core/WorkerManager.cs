// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Networking;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Manages worker type registration and object creation during sync decode.
/// Uses a synchronized type index table.
/// </summary>
public class WorkerManager
{
    public readonly World World;

    private SyncArray<string> indexTable;
    private Dictionary<string, int> nameToIndex;
    private List<Type> types;
    private HashSet<string> typesToRegister;

    public WorkerManager(World world)
    {
        World = world;
        nameToIndex = new Dictionary<string, int>();
        types = new List<Type>();
        typesToRegister = new HashSet<string>();
        
        // Synchronized type index table
        indexTable = new SyncArray<string>();
        indexTable.Initialize(world, world.RootSlot);
        indexTable.EndInitPhase();
        indexTable.DataWritten += NewIndexes;
        
        if (world.IsAuthority)
        {
            // Index 0 is reserved for null/unknown types
            indexTable.Append("");
        }
    }

    public void RegisterTypes()
    {
        if (!World.IsAuthority)
        {
            return;
        }
        
        foreach (string typeName in typesToRegister)
        {
            RegisterType(typeName);
        }
        typesToRegister.Clear();
    }

    public void InformOfTypeUse(Type type)
    {
        if (World.IsAuthority && !nameToIndex.ContainsKey(type.FullName!))
        {
            typesToRegister.Add(type.FullName!);
        }
    }

    private void NewIndexes(SyncArray<string> sender, int startIndex, int length)
    {
        for (int i = 0; i < length; i++)
        {
            int index = startIndex + i;

            // Skip indices we already know (happens when a full-snapshot delta re-sends all entries)
            if (index < types.Count)
                continue;

            if (index == 0)
            {
                types.Add(null!);
                continue;
            }

            string typeName = sender[index];
            Type type = GetType(typeName);
            nameToIndex[typeName] = index;
            types.Add(type);
        }
    }

    public int GetIndex(Type type)
    {
        int value = 0;
        nameToIndex.TryGetValue(type.FullName!, out value);
        if (value == 0)
        {
            InformOfTypeUse(type);
        }
        return value;
    }

    private int RegisterType(string typeName)
    {
        int count = indexTable.Count;
        indexTable.Append(typeName);
        return count;
    }

    /// <summary>
    /// Encode type information into sync stream
    /// </summary>
    public void EncodeType(BinaryWriter writer, Type type)
    {
        int index = GetIndex(type);
        writer.Write7BitEncoded((ulong)index);
        if (index == 0)
        {
            // Type not in index table yet, write full name
            writer.Write(type.FullName!);
        }
    }

    /// <summary>
    /// Decode type information from sync stream
    /// </summary>
    public Type DecodeType(BinaryReader reader)
    {
        int index = (int)reader.Read7BitEncoded();

        if (index >= types.Count)
        {
            throw new Exception($"Unknown type index: {index}, total known indexes: {types.Count}");
        }

        if (index > 0)
        {
            Type resolved = types[index];
            if (resolved == null)
            {
                // This index resolved to null when the type table first synced - almost always because
                // the owning assembly wasn't loaded yet. Don't trust that forever: try again now (cheap,
                // GetType caches hits) and heal the slot if it resolves. Without this, ONE early miss
                // permanently nulls every component of that type for the world's whole lifetime, which
                // silently drops the component and orphans its child slots. A still-null result just
                // means a host type we genuinely don't have. -xlinka
                string name = indexTable[index];
                resolved = GetType(name);
                if (resolved != null)
                    types[index] = resolved;
            }
            return resolved!;
        }

        // Index 0 means type name follows
        string typeName = reader.ReadString();
        Type type = GetType(typeName);
        InformOfTypeUse(type);
        return type;
    }

    public static T Instantiate<T>() where T : IWorker, new()
    {
        return new T();
    }

    public static IWorker Instantiate(Type type)
    {
        try
        {
            return (IWorker)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error instantiating type: {ex}", ex);
        }
    }

    public static IWorker Instantiate(string typename)
    {
        try
        {
            return Instantiate(GetType(typename));
        }
        catch (Exception innerException)
        {
            throw new Exception($"Error instantiating type with typename: {typename}", innerException);
        }
    }

    // Resolved types are cached across all worlds (a type name maps to the same Type process-wide).
    // We only cache hits - a miss may resolve later once its assembly is loaded, so we never cache
    // null. -xlinka
    private static readonly Dictionary<string, Type> _typeCache = new();

    public static Type GetType(string typename)
    {
        if (string.IsNullOrEmpty(typename))
            return null!;

        lock (_typeCache)
        {
            if (_typeCache.TryGetValue(typename, out var cached))
                return cached;
        }

        Type type = Type.GetType(typename)!;
        if (type == null)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(typename)!;
                if (type != null)
                    break;
            }
        }

        if (type != null)
        {
            lock (_typeCache)
            {
                _typeCache[typename] = type;
            }
            return type;
        }

        // A type the host used can't be resolved here. The component will be skipped on this client
        // and its whole subtree will go missing - this is the loud signal for that. -xlinka
        LumoraLogger.Error($"WorkerManager.GetType: UNRESOLVED TYPE '{typename}' - a host component of this type will be SKIPPED on this client (assembly not loaded or name mismatch). Joined world will be incomplete.");
        return null!;
    }
}

