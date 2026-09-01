// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// Row-major two-dimensional view over SyncArray's flat buffer: the storage stays one contiguous T[]
// and the row width travels with it as state.
//
// Changing the width ALWAYS clears. A receiver holding the old rows cannot be repaired by
// re-indexing - every element past the first row lands somewhere else - and the clear is what forces
// the next delta onto the full-snapshot branch, which is the only shape that restates the width and
// the contents together. The sparse-index branch never resizes, so a width it did not send is still
// the width the indices were computed against. -xlinka
public class SyncGrid<T> : SyncArray<T>
{
    private int _blockSize = 1;

    // Elements per row. Never zero: an empty grid is one column wide with no rows.
    public int BlockSize => _blockSize;

    public int RowCount => Count / _blockSize;

    public T this[int row, int column]
    {
        get => base[LinearIndex(row, column)];
        set => base[LinearIndex(row, column)] = value;
    }

    public int LinearIndex(int row, int column)
    {
        if ((uint)column >= (uint)_blockSize)
            throw new ArgumentOutOfRangeException(nameof(column));
        if (row < 0)
            throw new ArgumentOutOfRangeException(nameof(row));
        return row * _blockSize + column;
    }

    public void SetDimensions(int blockSize)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (blockSize == _blockSize)
            return;

        Clear();
        // A refused clear leaves rows laid out at the old width; adopting the new one anyway would
        // reinterpret every element still sitting in the buffer.
        if (Count != 0)
            return;

        _blockSize = blockSize;
        InvalidateSyncElement();
    }

    public void AddRow(params T[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        if (values.Length != _blockSize)
            throw new ArgumentException($"Row needs exactly {_blockSize} values, got {values.Length}", nameof(values));
        AddRange(values);
    }

    public IEnumerable<T> Row(int row)
    {
        for (int i = 0; i < _blockSize; i++)
            yield return this[row, i];
    }

    public override void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is SyncGrid<T> grid)
            _blockSize = grid._blockSize;
        base.CopyFromSource(source, copyChild);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write7BitEncoded((ulong)_blockSize);
        base.InternalEncodeFull(writer, outboundMessage);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        int blockSize = (int)reader.Read7BitEncoded();
        _blockSize = blockSize > 0 ? blockSize : 1;
        base.InternalDecodeFull(reader, inboundMessage);
    }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("BlockSize", _blockSize);
        dictionary.Add("Data", base.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        int blockSize = dictionary.ExtractOrDefault("BlockSize", 1);
        _blockSize = blockSize > 0 ? blockSize : 1;
        if (dictionary.TryGetNode("Data") is { } data)
            base.Load(data, control);
    }

    public override string ToString() => $"SyncGrid<{typeof(T).Name}>[{RowCount}x{_blockSize}]";
}
