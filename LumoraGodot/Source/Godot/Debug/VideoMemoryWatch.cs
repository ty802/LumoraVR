// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Debug;

// Where video memory went, said in the log whenever the total moves by Step or more: the renderer's texture
// and buffer totals, the device allocator's own, and what in the scene holds geometry. An avatar arriving
// took gigabytes that the texture uploads added up to a small part of, and the one total the frame report
// carries says nothing about whose they are. Main thread, a look every couple of seconds; the scene walk
// only happens when the total has moved. -xlinka
internal static class VideoMemoryWatch
{
    private const long Step = 64L << 20;
    private const double IntervalSeconds = 2.0;

    private static double _timer;
    private static long _lastTotal = -1;

    public static void Tick(double delta, Node? root)
    {
        _timer += delta;
        if (_timer < IntervalSeconds || root == null)
            return;
        _timer = 0;

        long total = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        if (_lastTotal >= 0 && Math.Abs(total - _lastTotal) < Step)
            return;
        long before = _lastTotal;
        _lastTotal = total;

        long textures = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed);
        long buffers = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed);
        string device = string.Empty;
        var rd = RenderingServer.GetRenderingDevice();
        if (rd != null)
        {
            device = $"; device textures {(long)rd.GetMemoryUsage(RenderingDevice.MemoryType.Textures) >> 20} MB, "
                   + $"buffers {(long)rd.GetMemoryUsage(RenderingDevice.MemoryType.Buffers) >> 20} MB, "
                   + $"total {(long)rd.GetMemoryUsage(RenderingDevice.MemoryType.Total) >> 20} MB";
        }

        int instances = 0, skinned = 0, multimeshInstances = 0, particles = 0;
        long vertices = 0, shapeVertices = 0, biggestShapeVertices = 0;
        string biggest = string.Empty;
        var meshes = new HashSet<ulong>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            switch (node)
            {
                case MeshInstance3D mi:
                    instances++;
                    if (mi.Skin != null)
                        skinned++;
                    if (mi.Mesh is ArrayMesh mesh && meshes.Add(mesh.GetInstanceId()))
                    {
                        long meshVertices = 0;
                        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                            meshVertices += mesh.SurfaceGetArrayLen(s);
                        long meshShapes = meshVertices * mesh.GetBlendShapeCount();
                        vertices += meshVertices;
                        shapeVertices += meshShapes;
                        if (meshShapes > biggestShapeVertices)
                        {
                            biggestShapeVertices = meshShapes;
                            biggest = $"'{mi.Name}' {meshVertices} vertices x {mesh.GetBlendShapeCount()} shapes";
                        }
                    }
                    break;
                case MultiMeshInstance3D mm:
                    multimeshInstances += mm.Multimesh?.InstanceCount ?? 0;
                    break;
                case GpuParticles3D gp:
                    particles += gp.Amount;
                    break;
            }
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }

        LumoraLogger.Log($"VideoMemoryWatch: {total >> 20} MB (was {(before < 0 ? "-" : (before >> 20).ToString())}): textures {textures >> 20} MB, "
                       + $"buffers {buffers >> 20} MB{device}; {instances} mesh instances ({skinned} skinned) over {meshes.Count} meshes, "
                       + $"{vertices} vertices, {shapeVertices} blend-shape vertices (most: {biggest}), "
                       + $"{multimeshInstances} multimesh instances, {particles} GPU particles");
    }
}
