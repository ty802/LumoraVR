// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lumora.Core.Assets.Animation;

// A named set of animated channels with a duration. The clip owns no references into the world: it
// is pure data, so the same decoded clip is shared by every requester and every peer, and one clip
// can be bound to any hierarchy whose node names match.
//
// SERIALIZATION SHAPE. Header fields (magic, version, name, duration, framerate, track count) are
// written UNCOMPRESSED and up front so a reader can identify a clip and report its length without
// decoding the keys. Each track then writes its own type tags before its body, which is what lets a
// newer file carrying a value type this build does not know be SKIPPED rather than corrupting the
// stream: the tag pair plus the byte length prefix is enough to step over it. Everything is
// little-endian - BinaryWriter is little-endian on every runtime we target, so no byte swapping
// happens anywhere and a clip written on one machine reads byte-identical on another. -xlinka
public sealed class AnimationClip
{
    // Four ASCII bytes, no length prefix.
    public static readonly byte[] Magic = { (byte)'L', (byte)'A', (byte)'N', (byte)'M' };

    // A reader accepts anything at or below this; a higher version is a hard failure because the
    // header layout itself could have moved.
    public const ushort Version = 1;

    private readonly List<AnimationTrack> _tracks = new();

    public string Name { get; set; } = string.Empty;

    // Normally the longest track's duration, but stored explicitly so a clip can hold trailing
    // silence that no key reaches.
    public float Duration { get; set; }

    // Informational: sampling is continuous in seconds and never quantizes to this.
    public float Framerate { get; set; }

    public int TrackCount => _tracks.Count;

    public AnimationTrack this[int index] => _tracks[index];

    public IReadOnlyList<AnimationTrack> Tracks => _tracks;

    public AnimationClip()
    {
    }

    public AnimationClip(string name, float duration = 0f, float framerate = 0f)
    {
        Name = name ?? string.Empty;
        Duration = duration;
        Framerate = framerate;
    }

    public void AddTrack(AnimationTrack track)
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }
        _tracks.Add(track);
    }

    public CurveAnimationTrack<T> AddCurveTrack<T>(string node, string property)
    {
        var track = new CurveAnimationTrack<T> { Node = node, Property = property };
        _tracks.Add(track);
        return track;
    }

    public DiscreteAnimationTrack<T> AddDiscreteTrack<T>(string node, string property)
    {
        var track = new DiscreteAnimationTrack<T> { Node = node, Property = property };
        _tracks.Add(track);
        return track;
    }

    public AnimationTrack? FindTrack(string node, string property)
    {
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (_tracks[i].Node == node && _tracks[i].Property == property)
            {
                return _tracks[i];
            }
        }
        return null;
    }

    // Ignores the stored Duration.
    public float GetMaxTrackDuration()
    {
        float max = 0f;
        for (int i = 0; i < _tracks.Count; i++)
        {
            float d = _tracks[i].Duration;
            if (d > max)
            {
                max = d;
            }
        }
        return max;
    }

    // Called by importers once keys are in.
    public void RecalculateDuration() => Duration = GetMaxTrackDuration();

    public float Wrap(float time, AnimationWrapMode mode) => AnimationWrap.Wrap(time, Duration, mode);

    // SERIALIZATION

    // Leaves the stream open.
    public void Encode(Stream stream)
    {
        using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(Name ?? string.Empty);
        bw.Write(Duration);
        bw.Write(Framerate);
        bw.Write7BitEncodedInt(_tracks.Count);

        // Each track body is length-prefixed so an unknown element type can be skipped instead of
        // desynchronizing everything after it. Costs a few bytes per track and buys forward
        // compatibility for every type added later.
        using var scratch = new MemoryStream();
        foreach (var track in _tracks)
        {
            bw.Write((byte)track.TrackType);
            bw.Write((byte)track.ElementType);
            track.EncodeHeader(bw);

            scratch.SetLength(0);
            using (var bodyWriter = new BinaryWriter(scratch, Encoding.UTF8, leaveOpen: true))
            {
                track.EncodeBody(bodyWriter);
                bodyWriter.Flush();
            }

            bw.Write7BitEncodedInt((int)scratch.Length);
            bw.Write(scratch.GetBuffer(), 0, (int)scratch.Length);
        }

        bw.Flush();
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        Encode(ms);
        return ms.ToArray();
    }

    // Throws InvalidDataException on a malformed clip.
    public static AnimationClip Decode(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
        {
            throw new InvalidDataException("Not an animation clip (bad magic)");
        }

        ushort version = br.ReadUInt16();
        if (version > Version)
        {
            throw new InvalidDataException($"Animation clip version {version} is newer than supported ({Version})");
        }

        var clip = new AnimationClip
        {
            Name = br.ReadString(),
            Duration = br.ReadSingle(),
            Framerate = br.ReadSingle()
        };

        int trackCount = br.Read7BitEncodedInt();
        if (trackCount < 0)
        {
            throw new InvalidDataException($"Negative track count ({trackCount})");
        }

        for (int i = 0; i < trackCount; i++)
        {
            var trackType = (AnimationTrackType)br.ReadByte();
            var elementType = (AnimationElementType)br.ReadByte();

            string node = br.ReadString();
            string property = br.ReadString();
            int bodyLength = br.Read7BitEncodedInt();
            if (bodyLength < 0)
            {
                throw new InvalidDataException($"Negative track body length ({bodyLength})");
            }

            var track = CreateTrack(trackType, elementType);
            if (track == null)
            {
                // Unknown element or track type from a newer writer. Step over the body and keep the
                // rest of the clip: a clip with one channel this build cannot represent is still worth
                // playing.
                Skip(br, bodyLength);
                continue;
            }

            track.Node = node;
            track.Property = property;

            var body = br.ReadBytes(bodyLength);
            if (body.Length != bodyLength)
            {
                throw new InvalidDataException("Truncated animation track body");
            }

            using (var bodyStream = new MemoryStream(body, writable: false))
            using (var bodyReader = new BinaryReader(bodyStream, Encoding.UTF8, leaveOpen: true))
            {
                track.DecodeBody(bodyReader);
            }

            clip._tracks.Add(track);
        }

        return clip;
    }

    public static AnimationClip FromBytes(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            throw new InvalidDataException("Empty animation clip data");
        }
        using var ms = new MemoryStream(data, writable: false);
        return Decode(ms);
    }

    // Cheap sniff, no full decode.
    public static bool LooksLikeClip(byte[] data)
        => data != null && data.Length >= 6
           && data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2] && data[3] == Magic[3];

    private static void Skip(BinaryReader br, int count)
    {
        if (br.BaseStream.CanSeek)
        {
            br.BaseStream.Seek(count, SeekOrigin.Current);
            return;
        }
        int remaining = count;
        while (remaining > 0)
        {
            int chunk = br.Read(new byte[System.Math.Min(remaining, 4096)], 0, System.Math.Min(remaining, 4096));
            if (chunk <= 0)
            {
                throw new InvalidDataException("Truncated animation track body");
            }
            remaining -= chunk;
        }
    }

    // Null when this build has no codec for that element type.
    private static AnimationTrack? CreateTrack(AnimationTrackType trackType, AnimationElementType elementType)
    {
        var valueType = AnimationCodecs.TypeFor(elementType);
        if (valueType == null)
        {
            return null;
        }

        var open = trackType switch
        {
            AnimationTrackType.Curve => typeof(CurveAnimationTrack<>),
            AnimationTrackType.Discrete => typeof(DiscreteAnimationTrack<>),
            _ => null
        };
        if (open == null)
        {
            return null;
        }

        return (AnimationTrack?)Activator.CreateInstance(open.MakeGenericType(valueType));
    }

    public override string ToString()
        => $"AnimationClip('{Name}', {Duration:0.###}s, {_tracks.Count} tracks)";
}
