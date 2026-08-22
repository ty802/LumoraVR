// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;

namespace Lumora.Core.Assets.Animation;

// Stepped track: a key holds its value until the next one. For values with no midpoint - a bool
// toggling, an enum ordinal, a string swapping - and for sparse data where blending would invent
// frames that were never authored.
//
// It is not just "a curve track that always steps": no interpolation byte, no tangent arrays, no
// blend branch per sample. On sparse channels that is most of the storage. -xlinka
public sealed class DiscreteAnimationTrack<T> : AnimationTrackBase, IAnimationTrack<T>
{
    private static readonly IAnimationValueCodec<T> Codec =
        AnimationCodecs.Get<T>() ?? throw new NotSupportedException($"No animation codec for {typeof(T)}");

    private T[] _values = Array.Empty<T>();

    public override AnimationTrackType TrackType => AnimationTrackType.Discrete;

    public override Type ValueType => typeof(T);

    public override AnimationElementType ElementType => Codec.ElementType;

    public T KeyValue(int index) => _values[index];

    // Keys may arrive in any order; they are kept sorted by time.
    public void AddKey(float time, T value)
    {
        if (!float.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time), "Keyframe time must be finite");
        }
        if (time < 0f)
        {
            time = 0f;
        }

        int index = InsertIndexFor(time);
        InsertInto(ref _times, _count, index, time);
        InsertInto(ref _values, _count, index, value);
        _count++;
        ResetCursor();
    }

    public bool Evaluate(float time, out T value)
    {
        if (_count == 0)
        {
            value = default!;
            return false;
        }

        int i = FindKey(time);
        value = _values[i < 0 ? 0 : i];
        return true;
    }

    public override object? EvaluateBoxed(float time) => Evaluate(time, out var v) ? v : null;

    internal override void EncodeBody(BinaryWriter bw)
    {
        bw.Write7BitEncodedInt(_count);
        for (int i = 0; i < _count; i++)
        {
            bw.Write(_times[i]);
            Codec.Write(bw, _values[i]);
        }
    }

    internal override void DecodeBody(BinaryReader br)
    {
        int count = br.Read7BitEncodedInt();
        if (count < 0)
        {
            throw new InvalidDataException($"Negative keyframe count ({count})");
        }

        _times = new float[count];
        _values = new T[count];
        for (int i = 0; i < count; i++)
        {
            _times[i] = br.ReadSingle();
            _values[i] = Codec.Read(br);
        }

        _count = count;
        ResetCursor();
    }
}
