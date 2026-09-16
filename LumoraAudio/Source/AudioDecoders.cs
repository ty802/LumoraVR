// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// File decoders, all managed: WAV by hand, ogg/vorbis and mp3 through their packages. The whole file
// is decoded up front; clips are for sound effects and music, streams handle the long stuff.
//
// Nothing here throws. A caller decoding whatever a user dropped into the world gets back false and a
// sentence it can put on a label, because "the file is not audio this build can read" is an ordinary
// outcome of an import and not an exceptional one. -xlinka
public static class AudioDecoders
{
    // Ten minutes of stereo at 48 kHz, counted in float values, which is 230 MB of clip. Past that
    // the file wants streaming rather than a decoded buffer every player shares.
    public const int MaxDecodedSamples = 48000 * 600 * 2;

    // A source bigger than this cannot decode to anything under the cap anyway, so it is refused
    // before half a gigabyte is read into memory to find that out.
    private const long MaxSourceBytes = 512L * 1024L * 1024L;

    private enum ClipFormat
    {
        Unknown,
        Wav,
        Ogg,
        Mp3,
    }

    public static bool IsSupportedExtension(string? extension)
        => Identify(extension) != ClipFormat.Unknown;

    public static bool TryDecode(Stream file, string? extensionOrMime, out AudioClipData? clip, out string? error)
    {
        clip = null;
        error = null;

        if (file == null)
        {
            error = "there was no audio data to decode.";
            return false;
        }

        byte[] bytes;
        try
        {
            if (file.CanSeek && file.Length > MaxSourceBytes)
            {
                error = "this file is far too large to load as a clip.";
                return false;
            }

            if (file is MemoryStream memory && memory.TryGetBuffer(out var existing))
            {
                return TryDecode(new ReadOnlyMemory<byte>(existing.Array!, existing.Offset, existing.Count), extensionOrMime, out clip, out error);
            }

            using var copy = file.CanSeek ? new MemoryStream((int)Math.Min(file.Length, int.MaxValue)) : new MemoryStream();
            file.CopyTo(copy);
            bytes = copy.GetBuffer();
            return TryDecode(new ReadOnlyMemory<byte>(bytes, 0, (int)copy.Length), extensionOrMime, out clip, out error);
        }
        catch (Exception ex)
        {
            error = $"the audio data could not be read: {ex.Message}";
            return false;
        }
    }

    public static bool TryDecode(ReadOnlyMemory<byte> bytes, string? extensionOrMime, out AudioClipData? clip, out string? error)
    {
        clip = null;
        error = null;

        if (bytes.Length == 0)
        {
            error = "the file is empty.";
            return false;
        }

        if (bytes.Length > MaxSourceBytes)
        {
            error = "this file is far too large to load as a clip.";
            return false;
        }

        // Content first, hint second. The hint is whatever a URL's path happened to end in, and a
        // content-addressed store hands out addresses with no extension at all, so the bytes are the
        // only thing that actually knows what this is.
        var format = Sniff(bytes.Span);
        if (format == ClipFormat.Unknown)
            format = Identify(extensionOrMime);

        try
        {
            switch (format)
            {
                case ClipFormat.Wav:
                    return TryDecodeWav(bytes.Span, out clip, out error);
                case ClipFormat.Ogg:
                    return TryDecodeOgg(bytes, out clip, out error);
                case ClipFormat.Mp3:
                    return TryDecodeMp3(bytes, out clip, out error);
                default:
                    error = "this is not a WAV, Ogg Vorbis or MP3 file.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = $"the audio could not be decoded: {ex.Message}";
            return false;
        }
    }

    // FORMAT

    private static ClipFormat Identify(string? extensionOrMime)
    {
        if (string.IsNullOrWhiteSpace(extensionOrMime))
            return ClipFormat.Unknown;

        string hint = extensionOrMime.Trim().ToLowerInvariant();

        int semicolon = hint.IndexOf(';');
        if (semicolon >= 0)
            hint = hint.Substring(0, semicolon);

        int slash = hint.IndexOf('/');
        if (slash >= 0)
            hint = hint.Substring(slash + 1);

        // Also swallows a whole filename or a mime subtype that carries a dot of its own.
        int dot = hint.LastIndexOf('.');
        if (dot >= 0)
            hint = hint.Substring(dot + 1);

        hint = hint.Trim();

        return hint switch
        {
            "wav" or "wave" or "x-wav" or "x-pn-wav" => ClipFormat.Wav,
            "ogg" or "oga" or "vorbis" or "x-vorbis" => ClipFormat.Ogg,
            "mp3" or "mpeg" or "mpeg3" or "mpga" or "x-mpeg" => ClipFormat.Mp3,
            _ => ClipFormat.Unknown,
        };
    }

    private static ClipFormat Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && Is(data, 0, "RIFF") && Is(data, 8, "WAVE"))
            return ClipFormat.Wav;

        if (data.Length >= 4 && Is(data, 0, "OggS"))
            return ClipFormat.Ogg;

        if (data.Length >= 3 && Is(data, 0, "ID3"))
            return ClipFormat.Mp3;

        // A bare MPEG frame: eleven set sync bits. Version and layer are left to the decoder.
        if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
            return ClipFormat.Mp3;

        return ClipFormat.Unknown;
    }

    private static bool Is(ReadOnlySpan<byte> data, int offset, string tag)
    {
        if (offset + tag.Length > data.Length)
            return false;
        for (int i = 0; i < tag.Length; i++)
        {
            if (data[offset + i] != (byte)tag[i])
                return false;
        }
        return true;
    }

    // WAV
    //
    // Walked as a chunk list rather than read as a fixed header, because a file written by a recorder
    // carries LIST, fact, bext and whatever else its author felt like between the format and the data,
    // and a reader that assumes data lands at offset 44 silently plays metadata as sound. -xlinka

    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private static bool TryDecodeWav(ReadOnlySpan<byte> data, out AudioClipData? clip, out string? error)
    {
        clip = null;
        error = null;

        if (data.Length < 12 || !Is(data, 0, "RIFF") || !Is(data, 8, "WAVE"))
        {
            error = "this is not a WAV file.";
            return false;
        }

        ushort formatTag = 0;
        int channels = 0;
        int sampleRate = 0;
        int bits = 0;
        bool haveFormat = false;
        int dataOffset = -1;
        int dataLength = 0;

        int position = 12;
        while (position + 8 <= data.Length)
        {
            long size = ReadUInt32(data, position + 4);
            int body = position + 8;
            // A streamed or truncated writer leaves a size that runs past the end of the file, often
            // 0xFFFFFFFF. Whatever is actually there is what gets played.
            if (size > data.Length - body)
                size = data.Length - body;

            if (Is(data, position, "fmt ") && size >= 16)
            {
                formatTag = ReadUInt16(data, body);
                channels = ReadUInt16(data, body + 2);
                sampleRate = (int)ReadUInt32(data, body + 4);
                bits = ReadUInt16(data, body + 14);
                haveFormat = true;

                // EXTENSIBLE hides the real tag in the first two bytes of its subformat GUID.
                if (formatTag == WaveFormatExtensible && size >= 40)
                    formatTag = ReadUInt16(data, body + 24);
            }
            else if (Is(data, position, "data"))
            {
                dataOffset = body;
                dataLength = (int)size;
            }

            // Chunks are word aligned, and the pad byte is not counted in the size.
            long advance = 8 + size + (size & 1);
            if (advance <= 0)
                break;
            position += (int)advance;
        }

        if (!haveFormat)
        {
            error = "this WAV has no format chunk.";
            return false;
        }
        if (dataOffset < 0 || dataLength <= 0)
        {
            error = "this WAV has no audio in it.";
            return false;
        }
        if (channels <= 0 || channels > 64)
        {
            error = $"this WAV claims {channels} channels, which is not a sound.";
            return false;
        }
        if (sampleRate <= 0 || sampleRate > 768000)
        {
            error = $"this WAV claims a sample rate of {sampleRate} Hz, which is not a sound.";
            return false;
        }

        bool isFloat = formatTag == WaveFormatFloat;
        if (formatTag != WaveFormatPcm && !isFloat)
        {
            error = $"this WAV holds compressed audio (format 0x{formatTag:X4}); only plain PCM and float WAV files can be read.";
            return false;
        }
        if (isFloat && bits != 32)
        {
            error = $"this WAV is {bits} bit float, and only 32 bit float is supported.";
            return false;
        }
        if (!isFloat && bits != 8 && bits != 16 && bits != 24 && bits != 32)
        {
            error = $"this WAV is {bits} bit, and only 8, 16, 24 and 32 bit PCM can be read.";
            return false;
        }

        int bytesPerSample = bits / 8;
        int total = dataLength / bytesPerSample;
        total -= total % channels;
        if (total <= 0)
        {
            error = "this WAV has no audio in it.";
            return false;
        }
        if (total > MaxDecodedSamples)
        {
            error = $"this clip is {Minutes(total, channels, sampleRate):0.#} minutes long, past the ten minute clip limit.";
            return false;
        }

        var samples = new float[total];
        var body2 = data.Slice(dataOffset, total * bytesPerSample);

        if (isFloat)
        {
            for (int i = 0; i < total; i++)
                samples[i] = BitConverter.Int32BitsToSingle((int)ReadUInt32(body2, i * 4));
        }
        else
        {
            switch (bits)
            {
                case 8:
                    // 8 bit WAV is the odd one out: unsigned, with silence at 128.
                    for (int i = 0; i < total; i++)
                        samples[i] = (body2[i] - 128) * (1f / 128f);
                    break;
                case 16:
                    for (int i = 0; i < total; i++)
                        samples[i] = (short)ReadUInt16(body2, i * 2) * (1f / 32768f);
                    break;
                case 24:
                    for (int i = 0; i < total; i++)
                    {
                        int at = i * 3;
                        int value = body2[at] | (body2[at + 1] << 8) | (body2[at + 2] << 16);
                        // Sign extend the 24 bit value into an int.
                        if ((value & 0x800000) != 0)
                            value |= unchecked((int)0xFF000000);
                        samples[i] = value * (1f / 8388608f);
                    }
                    break;
                default:
                    for (int i = 0; i < total; i++)
                        samples[i] = (int)ReadUInt32(body2, i * 4) * (1f / 2147483648f);
                    break;
            }
        }

        clip = new AudioClipData(samples, channels, sampleRate);
        return true;
    }

    // OGG VORBIS

    private static bool TryDecodeOgg(ReadOnlyMemory<byte> bytes, out AudioClipData? clip, out string? error)
    {
        clip = null;
        error = null;

        using var stream = AsStream(bytes);
        using var reader = new NVorbis.VorbisReader(stream, false);

        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        if (channels <= 0 || sampleRate <= 0)
        {
            error = "this Ogg file has no audio stream in it.";
            return false;
        }

        long declared = reader.TotalSamples;
        if (declared > 0 && declared * channels > MaxDecodedSamples)
        {
            error = $"this clip is {Minutes(declared * channels, channels, sampleRate):0.#} minutes long, past the ten minute clip limit.";
            return false;
        }

        var accumulator = new SampleAccumulator(declared > 0 ? (int)Math.Min(declared * channels, MaxDecodedSamples) : 0);

        // The read count has to be a whole number of frames or the reader refuses it.
        int span = 16384 - (16384 % channels);
        var buffer = new float[span];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, span)) > 0)
        {
            if (!accumulator.Add(buffer, read))
            {
                error = "this clip runs past the ten minute clip limit.";
                return false;
            }
        }

        if (accumulator.Count < channels)
        {
            error = "this Ogg file decoded to nothing.";
            return false;
        }

        clip = new AudioClipData(accumulator.ToArray(channels), channels, sampleRate);
        return true;
    }

    // MP3

    private static bool TryDecodeMp3(ReadOnlyMemory<byte> bytes, out AudioClipData? clip, out string? error)
    {
        clip = null;
        error = null;

        using var stream = AsStream(bytes);
        using var file = new NLayer.MpegFile(stream);

        int channels = file.Channels;
        int sampleRate = file.SampleRate;
        if (channels <= 0 || sampleRate <= 0)
        {
            error = "this MP3 has no audio stream in it.";
            return false;
        }

        double seconds = file.Duration.TotalSeconds;
        if (seconds > 0.0 && seconds * sampleRate * channels > MaxDecodedSamples)
        {
            error = $"this clip is {seconds / 60.0:0.#} minutes long, past the ten minute clip limit.";
            return false;
        }

        var accumulator = new SampleAccumulator(seconds > 0.0
            ? (int)Math.Min(seconds * sampleRate * channels, MaxDecodedSamples)
            : 0);

        int span = 16384 - (16384 % channels);
        var buffer = new float[span];
        int read;
        while ((read = file.ReadSamples(buffer, 0, span)) > 0)
        {
            if (!accumulator.Add(buffer, read))
            {
                error = "this clip runs past the ten minute clip limit.";
                return false;
            }
        }

        if (accumulator.Count < channels)
        {
            error = "this MP3 decoded to nothing.";
            return false;
        }

        clip = new AudioClipData(accumulator.ToArray(channels), channels, sampleRate);
        return true;
    }

    // HELPERS

    private static MemoryStream AsStream(ReadOnlyMemory<byte> bytes)
        => MemoryMarshal.TryGetArray(bytes, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(bytes.ToArray(), writable: false);

    private static double Minutes(long samples, int channels, int sampleRate)
        => samples / (double)channels / sampleRate / 60.0;

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    // Grows by doubling and hands out a buffer trimmed to a whole number of frames. A decoder that
    // does not know its own length up front is the normal case, so this never depends on one.
    private sealed class SampleAccumulator
    {
        private float[] _buffer;
        private int _count;

        public SampleAccumulator(int capacity)
        {
            _buffer = new float[Math.Clamp(capacity, 8192, MaxDecodedSamples)];
        }

        public int Count => _count;

        public bool Add(float[] source, int count)
        {
            if (count <= 0)
                return true;
            if ((long)_count + count > MaxDecodedSamples)
                return false;

            if (_count + count > _buffer.Length)
            {
                int size = _buffer.Length;
                while (size < _count + count)
                {
                    long doubled = (long)size * 2;
                    size = doubled >= MaxDecodedSamples ? MaxDecodedSamples : (int)doubled;
                }
                Array.Resize(ref _buffer, size);
            }

            Array.Copy(source, 0, _buffer, _count, count);
            _count += count;
            return true;
        }

        public float[] ToArray(int channels)
        {
            int trimmed = _count - (_count % channels);
            if (trimmed == _buffer.Length)
                return _buffer;
            var result = new float[trimmed];
            Array.Copy(_buffer, result, trimmed);
            return result;
        }
    }
}
