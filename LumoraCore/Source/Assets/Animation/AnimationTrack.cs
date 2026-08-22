// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;

namespace Lumora.Core.Assets.Animation;

// One animated channel. A track names WHAT it animates by string, never by reference: a node name
// (a bone or slot) plus a property path. Binding those names to real fields is somebody else's job,
// which is what lets one clip drive any hierarchy whose names line up. -xlinka
public abstract class AnimationTrack
{
    private string _node = string.Empty;
    private string _property = string.Empty;

    // A bone or slot name as authored in the source file.
    public string Node
    {
        get => _node;
        set => _node = value ?? string.Empty;
    }

    // Either a bare member name ("Position", "Rotation", "Scale", "BlendShape.Smile") or
    // "Component.Member" to reach into a component on the node's slot.
    public string Property
    {
        get => _property;
        set => _property = value ?? string.Empty;
    }

    public abstract AnimationTrackType TrackType { get; }

    public abstract Type ValueType { get; }

    public abstract AnimationElementType ElementType { get; }

    public abstract int KeyCount { get; }

    // In seconds. Zero for an empty track.
    public abstract float Duration { get; }

    // Convenience for inspectors; the typed path allocates nothing.
    public abstract object? EvaluateBoxed(float time);

    internal abstract void EncodeBody(BinaryWriter bw);

    internal abstract void DecodeBody(BinaryReader br);

    internal void EncodeHeader(BinaryWriter bw)
    {
        bw.Write(_node);
        bw.Write(_property);
    }

    internal void DecodeHeader(BinaryReader br)
    {
        _node = br.ReadString();
        _property = br.ReadString();
    }

    public override string ToString() => $"{GetType().Name}({_node}.{_property}, {KeyCount} keys)";
}

// Typed sampling face, so a binder that knows the field type can skip boxing.
public interface IAnimationTrack<T>
{
    // Time already wrapped into the clip range. Returns false only when the track holds no keys,
    // in which case value is the type default.
    bool Evaluate(float time, out T value);
}

// Shared key-index search for both track families.
//
// Playback is overwhelmingly sequential, so the cached index makes the common step O(1): from the
// last hit, walking forward one key covers a normal frame advance. The binary search is the fallback
// for a seek, a loop wrap, or a scrub, which is exactly when the cache is useless anyway. Keeping the
// cursor per TRACK rather than per clip matters because tracks have wildly different key densities. -xlinka
public abstract class AnimationTrackBase : AnimationTrack
{
    // Index of the key at or before the last sampled time. Mutable playback state, not file data.
    private int _cursor;

    // Times of every key, ascending. Kept sorted by the insert path.
    protected float[] _times = Array.Empty<float>();

    // Live key count; _times may be longer while a track is being built.
    protected int _count;

    public override int KeyCount => _count;

    public override float Duration => _count > 0 ? _times[_count - 1] : 0f;

    public float KeyTime(int index) => _times[index];

    // Call after editing keys.
    protected void ResetCursor() => _cursor = 0;

    // Returns -1 when time precedes the first key. Advances the cursor, so it is NOT safe to sample
    // one track from two threads at once - a clip is shared between requesters, so binders sample
    // on the world thread only.
    protected int FindKey(float time)
    {
        int count = _count;
        if (count == 0)
        {
            return -1;
        }
        if (time < _times[0])
        {
            _cursor = 0;
            return -1;
        }
        if (time >= _times[count - 1])
        {
            _cursor = count - 1;
            return count - 1;
        }

        int c = _cursor;
        if (c >= count)
        {
            c = count - 1;
        }

        // Sequential fast path: same key, or the very next one.
        if (_times[c] <= time)
        {
            if (c + 1 >= count || time < _times[c + 1])
            {
                _cursor = c;
                return c;
            }
            if (c + 2 >= count || time < _times[c + 2])
            {
                _cursor = c + 1;
                return c + 1;
            }
        }

        // Seek / wrap / scrub: binary search for the last key <= time.
        int lo = 0;
        int hi = count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_times[mid] <= time)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        _cursor = lo;
        return lo;
    }

    // Keeps _times ascending; appends when time is at the end.
    protected int InsertIndexFor(float time)
    {
        if (_count == 0 || time >= _times[_count - 1])
        {
            return _count;
        }
        int lo = 0;
        int hi = _count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_times[mid] <= time)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    protected static void InsertInto<TItem>(ref TItem[] array, int count, int index, TItem item)
    {
        if (count >= array.Length)
        {
            int size = array.Length == 0 ? 8 : array.Length * 2;
            Array.Resize(ref array, size);
        }
        if (index < count)
        {
            Array.Copy(array, index, array, index + 1, count - index);
        }
        array[index] = item;
    }
}
