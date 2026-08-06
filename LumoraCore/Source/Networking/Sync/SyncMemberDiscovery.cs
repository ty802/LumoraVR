// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Lumora.Core;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

public static class SyncMemberDiscovery
{
    public static List<ISyncMember> DiscoverSyncMembers(ISyncObject target)
    {
        var syncMembers = new List<ISyncMember>();
        Type type = target.GetType();

        FieldInfo[] fields = type.GetFields(
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance
        );

        int memberIndex = 0;
        foreach (var field in fields)
        {
            if (typeof(ISyncMember).IsAssignableFrom(field.FieldType))
            {
                ISyncMember syncMember = (ISyncMember)field.GetValue(target)!;

                if (syncMember == null)
                {
                    try
                    {
                        syncMember = (ISyncMember)Activator.CreateInstance(field.FieldType)!;
                        field.SetValue(target, syncMember);
                    }
                    catch (Exception ex)
                    {
                        LumoraLogger.Error($"Failed to create sync member {field.Name}: {ex.Message}");
                        continue;
                    }
                }

                syncMember.MemberIndex = memberIndex;
                syncMember.Name = field.Name;

                syncMembers.Add(syncMember);
                memberIndex++;
            }
        }

        LumoraLogger.Debug($"Discovered {syncMembers.Count} sync members in {type.Name}");
        return syncMembers;
    }

    // Each sync member gets its own RefID from the world's ReferenceController.
    public static List<ISyncMember> DiscoverAndInitializeSyncMembers(object target, World world, IWorldElement parent)
    {
        var syncMembers = new List<ISyncMember>();
        Type type = target.GetType();
        int memberIndex = 0;

        FieldInfo[] fields = type.GetFields(
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance
        );

        foreach (var field in fields)
        {
            if (typeof(ISyncMember).IsAssignableFrom(field.FieldType))
            {
                ISyncMember syncMember = (ISyncMember)field.GetValue(target)!;

                if (syncMember == null)
                {
                    try
                    {
                        syncMember = (ISyncMember)Activator.CreateInstance(field.FieldType)!;
                        field.SetValue(target, syncMember);
                    }
                    catch (Exception ex)
                    {
                        LumoraLogger.Error($"Failed to create sync member {field.Name}: {ex.Message}");
                        continue;
                    }
                }

                syncMember.MemberIndex = memberIndex;
                syncMember.Name = field.Name;

                if (world != null && syncMember.World == null)
                {
                    syncMember.Initialize(world, parent);
                    EndInitPhaseIfNeeded(syncMember);
                }

                HookUpChangedEvent(syncMember, parent);

                syncMembers.Add(syncMember);
                memberIndex++;
            }
        }

        PropertyInfo[] properties = type.GetProperties(
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance
        );

        foreach (var prop in properties)
        {
            if (typeof(ISyncMember).IsAssignableFrom(prop.PropertyType) && prop.CanRead)
            {
                try
                {
                    ISyncMember syncMember = (ISyncMember)prop.GetValue(target)!;

                    if (syncMember != null)
                    {
                        if (syncMember.Name == null)
                        {
                            syncMember.MemberIndex = memberIndex;
                            syncMember.Name = prop.Name;
                        }

                        if (world != null && syncMember.World == null)
                        {
                            syncMember.Initialize(world, parent);
                            EndInitPhaseIfNeeded(syncMember);
                        }

                        HookUpChangedEvent(syncMember, parent);

                        // Only add if not already in the list (avoid duplicates from backing fields)
                        if (!syncMembers.Contains(syncMember))
                        {
                            syncMembers.Add(syncMember);
                            memberIndex++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"Failed to access sync member property {prop.Name}: {ex.Message}");
                }
            }
        }

        LumoraLogger.Debug($"Discovered and initialized {syncMembers.Count} sync members in {type.Name}");
        return syncMembers;
    }

    public static void InitializeSyncMembers(List<ISyncMember> members, World world, IWorldElement parent)
    {
        if (world == null) return;

        foreach (var member in members)
        {
            member.Initialize(world, parent);
            EndInitPhaseIfNeeded(member);
        }
    }

    private static void EndInitPhaseIfNeeded(ISyncMember member)
    {
        if (member == null)
            return;

        if (member.IsInInitPhase)
        {
            member.EndInitPhase();
        }
    }

    public static List<ISyncMember> GetDirtySyncMembers(List<ISyncMember> members)
    {
        var dirty = new List<ISyncMember>();
        foreach (var member in members)
        {
            if (member.IsDirty)
            {
                dirty.Add(member);
            }
        }
        return dirty;
    }

    // Called after successful sync.
    public static void ClearDirtyFlags(List<ISyncMember> members)
    {
        foreach (var member in members)
        {
            member.IsDirty = false;
        }
    }

    public static void MarkAllDirty(List<ISyncMember> members)
    {
        foreach (var member in members)
        {
            member.IsDirty = true;
        }
    }

    private static void HookUpChangedEvent(ISyncMember syncMember, IWorldElement parent)
    {
        // Only hook if the sync member is IChangeable and parent is a Component
        if (syncMember is IChangeable changeable && parent is Component component)
        {
            changeable.Changed += (member) =>
            {
                if (component.IsDestroyed) return;

                component.NotifyChanged();
            };
        }
    }
}

