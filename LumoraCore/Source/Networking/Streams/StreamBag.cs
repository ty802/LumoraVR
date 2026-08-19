// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Networking.Streams;

public class StreamBag
{
    private readonly Dictionary<RefID, Stream> _streams = new();
    private readonly List<Stream> _justAdded = new();

    public User User { get; private set; } = null!;

    public int Count => _streams.Count;

    public IEnumerable<Stream> Streams => _streams.Values;

    public event Action<Stream> StreamAdded = null!;

    public event Action<Stream> StreamRemoved = null!;

    public void Initialize(User user)
    {
        User = user;
    }

    public void Add(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        _streams[stream.ReferenceID] = stream;
        _justAdded.Add(stream);
        StreamAdded?.Invoke(stream);
    }

    public bool Remove(Stream stream)
    {
        if (stream == null)
            return false;

        _justAdded.Remove(stream);
        if (_streams.Remove(stream.ReferenceID))
        {
            StreamRemoved?.Invoke(stream);
            return true;
        }
        return false;
    }

    public bool Remove(RefID id)
    {
        if (_streams.TryGetValue(id, out var stream))
        {
            _justAdded.Remove(stream);
            _streams.Remove(id);
            StreamRemoved?.Invoke(stream);
            return true;
        }
        return false;
    }

    public Stream Get(RefID id)
    {
        return (_streams.TryGetValue(id, out var stream) ? stream : null) ?? null!;
    }

    public bool TryGet(RefID id, out Stream stream)
    {
        if (_streams.TryGetValue(id, out var found))
        {
            stream = found;
            return true;
        }
        stream = default!;
        return false;
    }

    public bool WasJustAdded(Stream stream)
    {
        return _justAdded.Contains(stream);
    }

    public void ClearJustAdded()
    {
        _justAdded.Clear();
    }

    public void Clear()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }
        _streams.Clear();
        _justAdded.Clear();
    }

    public void Update()
    {
        foreach (var stream in _streams.Values)
        {
            if (stream.Active)
            {
                stream.Update();
            }
        }
    }
}
