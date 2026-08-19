// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;

namespace Lumora.Core.Networking.Streams;

public class StreamGroupManager
{
    private readonly Dictionary<ushort, string> _indexToName = new();
    private readonly Dictionary<string, ushort> _nameToIndex = new();
    private readonly Dictionary<ushort, StreamGroup> _groups = new();

    public User User { get; }

    public IEnumerable<StreamGroup> Groups => _groups.Values;

    public StreamGroupManager(User user)
    {
        User = user;
    }

    public ushort GetGroupIndex(string groupName)
    {
        if (_nameToIndex.TryGetValue(groupName, out var index))
            return index;

        index = _nameToIndex.Count == 0
            ? (ushort)1
            : (ushort)(_nameToIndex.Values.Max() + 1);

        _nameToIndex[groupName] = index;
        _indexToName[index] = groupName;

        return index;
    }

    public string GetGroupName(ushort index)
    {
        return (_indexToName.TryGetValue(index, out var name) ? name : null) ?? null!;
    }

    public void AssignToGroup(IStream stream, ushort? oldGroupIndex)
    {
        if (oldGroupIndex.HasValue && _groups.TryGetValue(oldGroupIndex.Value, out var oldGroup))
        {
            oldGroup.RemoveStream(stream);
            if (oldGroup.StreamCount == 0)
            {
                _groups.Remove(oldGroupIndex.Value);
                if (User?.IsLocal == true)
                {
                    _nameToIndex.Remove(_indexToName[oldGroupIndex.Value]);
                    _indexToName.Remove(oldGroupIndex.Value);
                }
            }
        }

        if (!_groups.TryGetValue(stream.GroupIndex, out var newGroup))
        {
            newGroup = new StreamGroup(this, stream.GroupIndex);
            _groups[stream.GroupIndex] = newGroup;
        }
        newGroup.AssignStream(stream);
    }

    public void StreamModified(IStream stream)
    {
        if (User?.IsLocal == true && _groups.TryGetValue(stream.GroupIndex, out var group))
        {
            group.GroupModified();
        }
    }

    public StreamGroup GetGroup(ushort index)
    {
        return (_groups.TryGetValue(index, out var group) ? group : null) ?? null!;
    }

    public bool ContainsStream(IStream stream)
    {
        if (_groups.TryGetValue(stream.GroupIndex, out var group))
        {
            return group.Streams.Contains(stream);
        }
        return false;
    }

    public void Clear()
    {
        _groups.Clear();
        _nameToIndex.Clear();
        _indexToName.Clear();
    }
}
