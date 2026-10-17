// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

public abstract class PhosSubmesh
{
    public readonly PhosMesh Mesh;

    internal int[] indices = Array.Empty<int>();
    internal int[] primitiveIDs = Array.Empty<int>();

    private int _currentID = 0;

    public abstract PhosTopology Topology { get; }

    public abstract int IndicesPerElement { get; }

    public int Index => Mesh.IndexOfSubmesh(this);

    public int Count { get; protected set; }

    public int IndexCount => Count * IndicesPerElement;

    // The live array, not a copy.
    public int[] RawIndices => indices;

    // In primitives, not indices.
    public int Capacity
    {
        get
        {
            int[] array = indices;
            return (array != null ? array.Length : 0) / IndicesPerElement;
        }
    }

    internal int PrimitivesVersion { get; private set; } = -1;

    protected PhosSubmesh(PhosMesh mesh)
    {
        Mesh = mesh;
    }

    public bool SetCountAndSequence(int count, int sequenceStart = 0)
    {
        int delta = count - Count;
        if (delta > 0)
        {
            int oldCount = Count;
            IncreaseCount(delta);
            for (int i = oldCount * IndicesPerElement; i < IndexCount; i++)
            {
                indices[i] = i + sequenceStart;
            }
        }
        else if (delta < 0)
        {
            RemoveFromEnd(-delta);
        }
        return delta != 0;
    }

    public void Clear()
    {
        Count = 0;
    }

    public bool SetCount(int count)
    {
        int delta = count - Count;
        if (delta > 0)
        {
            IncreaseCount(delta);
        }
        else if (delta < 0)
        {
            RemoveFromEnd(-delta);
        }
        return delta != 0;
    }

    public void IncreaseCount(int count)
    {
        Count += count;
        if (Capacity < Count)
        {
            EnsureCapacity(LuminaMath.Max(Capacity * 2, Count, 4));
        }

        // Track primitive IDs for removal detection
        if (Mesh.TrackRemovals)
        {
            for (int i = Count - count; i < Count; i++)
            {
                primitiveIDs[i] = _currentID++;
            }
        }
    }

    public void Append(PhosSubmesh submesh)
    {
        if (submesh.Topology != Topology)
        {
            throw new InvalidOperationException("Submesh topology mismatch!");
        }

        IncreaseCount(submesh.Count);
        Array.Copy(submesh.RawIndices, 0, RawIndices, IndexCount - submesh.IndexCount, submesh.IndexCount);
    }

    public void RemoveFromEnd(int count)
    {
        Remove(Count - count, count);
    }

    public void Remove(int index)
    {
        Remove(index, 1);
    }

    public void Remove(int index, int count)
    {
        if (count == 0) return;

        VerifyIndex(index);
        if (index + count > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (index + count != Count)
        {
            PrimitivesVersion--;
        }

        int indicesPerElement = IndicesPerElement;
        Mesh.RemoveElements(indices, index * indicesPerElement, count * indicesPerElement, Count * indicesPerElement, false, 0);
        Mesh.RemoveElements(primitiveIDs, index, count, Count, true, int.MaxValue);
        Count -= count;
    }

    private void EnsureCapacity(int capacity)
    {
        Mesh.EnsureIndices(ref indices, capacity * IndicesPerElement);
        Mesh.EnsureArray(Mesh.TrackRemovals, ref primitiveIDs, capacity, 0);
    }

    // Hands the index array back to the mesh at a Clear and leaves this submesh empty, so a write through a
    // reference somebody kept fails loudly instead of landing in the next build's indices.
    internal int[] Release()
    {
        var released = indices;
        indices = Array.Empty<int>();
        Count = 0;
        return released;
    }

    internal void VerifyIndex(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException($"index = {index}");
        }
    }

    internal bool UpdateIndex(ref int version, ref int index)
    {
        if (version < 0)
        {
            if (version != PrimitivesVersion)
            {
                throw new Exception("Primitive has been invalidated");
            }
        }
        else if (version != primitiveIDs[index])
        {
            while (index > 0 && primitiveIDs[index] > version)
            {
                index--;
            }

            if (primitiveIDs[index] != version)
            {
                throw new Exception("Primitive has been removed");
            }

            return true;
        }

        return false;
    }

    internal void VerticesRemoved(int index, int count)
    {
        int end = index + count;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= index && indices[i] < end)
            {
                indices[i] = -1;  // Mark as invalid
            }
            else if (indices[i] >= end)
            {
                indices[i] -= count;  // Shift down
            }
        }
    }

    internal void EnableTrackRemovals()
    {
        for (int i = 0; i < Count; i++)
        {
            primitiveIDs[i] = _currentID++;
        }
    }

    public BoundingBox CalculateBoundingBox()
    {
        var bounds = new BoundingBox();
        bounds.MakeEmpty();

        float3[] positions = Mesh.positions;
        if (positions != null)
        {
            for (int i = 0; i < IndexCount; i++)
            {
                int vertexIndex = indices[i];
                if (vertexIndex >= 0 && vertexIndex < positions.Length)
                {
                    bounds.Encapsulate(positions[vertexIndex]);
                }
            }
        }

        return bounds;
    }
}
