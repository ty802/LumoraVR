// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.GodotUI.Wizards;

/// <summary>
/// Engine debug panel for viewing world performance and memory usage.
/// </summary>
[ComponentCategory("GodotUI/Wizards")]
public class GodotEngineDebug : GodotUIPanel
{
    protected override string DefaultScenePath => LumAssets.UI.EngineDebug;
    protected override float2 DefaultSize => new float2(560, 400);

    // Cache for type memory estimates
    private static readonly Dictionary<Type, long> _typeMemoryCache = new();

    public override Dictionary<string, string> GetUIData()
    {
        var data = new Dictionary<string, string>();

        if (World == null) return data;

        // World Info
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/WorldName/Value"] = World.WorldName.Value ?? "Unnamed";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/WorldFocus/Value"] = World.Focus.ToString();
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/Authority/Value"] = World.IsAuthority ? "Host" : "Client";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/LocalUser/Value"] = World.LocalUser?.UserName.Value ?? "N/A";

        // Performance
        var fps = GetDisplayFps();
        var frameTimeMs = GetDisplayFrameTimeMs(fps);
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/FPS/Value"] = $"{fps:F1}";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/FrameTime/Value"] = frameTimeMs > 0 ? $"{frameTimeMs:F2} ms" : "-";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/RenderTime/Value"] = $"{World.Metrics.RenderTimeMs:F2} ms";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/PhysicsTime/Value"] = $"{World.Metrics.PhysicsTimeMs:F2} ms";

        // Memory
        var gcMemory = GC.GetTotalMemory(false);
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/StaticMem/Value"] = FormatBytes(gcMemory);
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/VideoMem/Value"] = FormatBytes(World.Metrics.VideoMemoryBytes);
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/Objects/Value"] = $"{World.Metrics.GodotObjectCount:N0}";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/Nodes/Value"] = $"{World.Metrics.GodotNodeCount:N0}";

        // World Statistics
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Slots/Value"] = $"{World.Metrics.SlotCount:N0}";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Components/Value"] = $"{World.Metrics.ComponentCount:N0}";
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Users/Value"] = $"{GetUserCount()}";

        var depthStats = GetSlotDepthStats();
        data["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/MaxDepth/Value"] = $"{depthStats.maxDepth}";

        // Memory Profiler - Estimated memory by component type
        var memoryBreakdown = GetMemoryBreakdown();
        long totalEstimated = memoryBreakdown.Sum(kvp => kvp.Value.memory);
        var topTypes = memoryBreakdown.OrderByDescending(kvp => kvp.Value.memory).Take(10).ToList();

        for (int i = 0; i < 10; i++)
        {
            if (i < topTypes.Count)
            {
                var entry = topTypes[i];
                float pct = totalEstimated > 0 ? (entry.Value.memory * 100f / totalEstimated) : 0;
                data[$"MainPanel/VBox/Content/RightPanel/VBox/ScrollContainer/MemoryList/Item{i}/Name"] =
                    $"{entry.Key} ({entry.Value.count})";
                data[$"MainPanel/VBox/Content/RightPanel/VBox/ScrollContainer/MemoryList/Item{i}/Count"] =
                    $"{FormatBytes(entry.Value.memory)} ({pct:F1}%)";
            }
            else
            {
                data[$"MainPanel/VBox/Content/RightPanel/VBox/ScrollContainer/MemoryList/Item{i}/Name"] = "";
                data[$"MainPanel/VBox/Content/RightPanel/VBox/ScrollContainer/MemoryList/Item{i}/Count"] = "";
            }
        }

        return data;
    }

    public override Dictionary<string, color> GetUIColors()
    {
        var colors = new Dictionary<string, color>();
        if (World == null) return colors;

        var fps = GetDisplayFps();
        var frameTimeMs = GetDisplayFrameTimeMs(fps);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/WorldFocus/Value"] =
            World.Focus == Lumora.Core.World.WorldFocus.Focused ? GoodColor : WarnColor;
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/Authority/Value"] =
            World.IsAuthority ? new color(0.45f, 0.8f, 1f, 1f) : new color(0.65f, 0.72f, 0.95f, 1f);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldInfo/LocalUser/Value"] =
            World.LocalUser != null ? new color(0.45f, 0.95f, 1f, 1f) : BadColor;

        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/FPS/Value"] = PerformanceColor(fps);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/FrameTime/Value"] = FrameTimeColor(frameTimeMs);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/RenderTime/Value"] =
            MillisecondsColor(World.Metrics.RenderTimeMs, 8f, 14f);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Performance/PhysicsTime/Value"] =
            MillisecondsColor(World.Metrics.PhysicsTimeMs, 6f, 12f);

        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/StaticMem/Value"] =
            BytesColor(GC.GetTotalMemory(false), 256L * 1024L * 1024L, 512L * 1024L * 1024L);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/VideoMem/Value"] =
            BytesColor(World.Metrics.VideoMemoryBytes, 768L * 1024L * 1024L, 1536L * 1024L * 1024L);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/Objects/Value"] =
            CountColor(World.Metrics.GodotObjectCount, 3000, 8000);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/Memory/Nodes/Value"] =
            CountColor(World.Metrics.GodotNodeCount, 1500, 5000);

        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Users/Value"] =
            GetUserCount() > 0 ? GoodColor : BadColor;
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Slots/Value"] =
            CountColor(World.Metrics.SlotCount, 800, 2000);
        colors["MainPanel/VBox/Content/LeftPanel/Scroll/VBox/WorldStats/Components/Value"] =
            CountColor(World.Metrics.ComponentCount, 1500, 4000);

        for (int i = 0; i < 10; i++)
        {
            colors[$"MainPanel/VBox/Content/RightPanel/VBox/ScrollContainer/MemoryList/Item{i}/Count"] =
                i < 3 ? WarnColor : new color(0.62f, 0.9f, 0.62f, 1f);
        }

        return colors;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private int GetUserCount()
    {
        if (World == null) return 0;

        var users = World.GetAllUsers();
        return World.LocalUser != null && !users.Contains(World.LocalUser)
            ? users.Count + 1
            : users.Count;
    }

    private static readonly color GoodColor = new(0.4f, 0.95f, 0.55f, 1f);
    private static readonly color WarnColor = new(1f, 0.78f, 0.32f, 1f);
    private static readonly color BadColor = new(1f, 0.36f, 0.36f, 1f);

    private double GetDisplayFps()
    {
        if (World?.Metrics.GodotFps > 0)
        {
            return World.Metrics.GodotFps;
        }

        return World?.LocalUser?.FPS.Value ?? 0;
    }

    private double GetDisplayFrameTimeMs(double fps)
    {
        if (World?.Metrics.GodotFrameTimeMs > 0)
        {
            return World.Metrics.GodotFrameTimeMs;
        }

        return fps > 0 ? 1000.0 / fps : 0;
    }

    private static color PerformanceColor(double fps)
    {
        if (fps >= 72f) return GoodColor;
        if (fps >= 45f) return WarnColor;
        return BadColor;
    }

    private static color FrameTimeColor(double frameTimeMs)
    {
        if (frameTimeMs <= 0) return BadColor;
        return MillisecondsColor(frameTimeMs, 13.9f, 22.2f);
    }

    private static color MillisecondsColor(double value, double warnAt, double badAt)
    {
        if (value <= warnAt) return GoodColor;
        if (value <= badAt) return WarnColor;
        return BadColor;
    }

    private static color BytesColor(long value, long warnAt, long badAt)
    {
        if (value <= warnAt) return GoodColor;
        if (value <= badAt) return WarnColor;
        return BadColor;
    }

    private static color CountColor(int value, int warnAt, int badAt)
    {
        if (value <= warnAt) return GoodColor;
        if (value <= badAt) return WarnColor;
        return BadColor;
    }

    /// <summary>
    /// Estimate memory size for a component type based on its sync fields.
    /// </summary>
    private static long EstimateTypeMemory(Type type)
    {
        if (_typeMemoryCache.TryGetValue(type, out var cached))
            return cached;

        long size = 64; // Base object overhead

        // Count sync members
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (!typeof(ISyncMember).IsAssignableFrom(prop.PropertyType))
                continue;

            var propType = prop.PropertyType;

            // Estimate based on sync type
            if (propType.IsGenericType)
            {
                var genericType = propType.GetGenericArguments().FirstOrDefault();
                if (genericType == typeof(string))
                    size += 80; // String sync with avg 40 char
                else if (genericType == typeof(float) || genericType == typeof(int))
                    size += 24;
                else if (genericType == typeof(float2))
                    size += 32;
                else if (genericType == typeof(float3))
                    size += 40;
                else if (genericType == typeof(float4) || genericType == typeof(floatQ))
                    size += 48;
                else if (genericType == typeof(float4x4))
                    size += 96;
                else if (genericType == typeof(bool))
                    size += 20;
                else if (genericType?.IsEnum == true)
                    size += 24;
                else
                    size += 48; // Other types
            }
            else
            {
                size += 32; // Default for non-generic sync members
            }
        }

        _typeMemoryCache[type] = size;
        return size;
    }

    /// <summary>
    /// Get slot hierarchy depth statistics.
    /// </summary>
    public (int maxDepth, int avgDepth, int totalSlots) GetSlotDepthStats()
    {
        if (World == null) return (0, 0, 0);

        int maxDepth = 0;
        int totalDepth = 0;
        int slotCount = 0;

        void TraverseSlots(Slot slot, int depth)
        {
            slotCount++;
            totalDepth += depth;
            if (depth > maxDepth) maxDepth = depth;

            foreach (var child in slot.Children)
            {
                TraverseSlots(child, depth + 1);
            }
        }

        TraverseSlots(World.RootSlot, 0);
        int avgDepth = slotCount > 0 ? totalDepth / slotCount : 0;

        return (maxDepth, avgDepth, slotCount);
    }

    /// <summary>
    /// Get memory breakdown by component type with estimated memory usage.
    /// </summary>
    public Dictionary<string, (int count, long memory)> GetMemoryBreakdown()
    {
        var breakdown = new Dictionary<string, (int count, long memory, Type type)>();
        if (World == null) return new Dictionary<string, (int, long)>();

        void AnalyzeComponents(Slot slot)
        {
            foreach (var component in slot.Components)
            {
                var type = component.GetType();
                var typeName = type.Name;

                if (breakdown.TryGetValue(typeName, out var existing))
                {
                    breakdown[typeName] = (existing.count + 1, existing.memory + EstimateTypeMemory(type), type);
                }
                else
                {
                    breakdown[typeName] = (1, EstimateTypeMemory(type), type);
                }
            }

            foreach (var child in slot.Children)
            {
                AnalyzeComponents(child);
            }
        }

        AnalyzeComponents(World.RootSlot);

        // Convert to simpler format
        return breakdown.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value.count, kvp.Value.memory));
    }
}
