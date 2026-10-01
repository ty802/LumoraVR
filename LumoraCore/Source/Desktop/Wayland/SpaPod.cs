// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;

namespace Lumora.Core.Desktop;

// The few SPA pods a video consumer sends and reads. A pod is a size and a type, then the body padded to
// eight bytes; an object is its type and id, then key/flags/pod properties; a choice is its kind and one
// child header, then the child bodies back to back. -xlinka
internal static class SpaPod
{
    private const uint TypeId = 3, TypeInt = 4, TypeRectangle = 10, TypeFraction = 11, TypeObject = 15, TypeChoice = 19;
    private const uint ObjectFormat = 0x40003, ObjectBuffers = 0x40004;
    private const uint ParamEnumFormat = 3, ParamBuffers = 5;
    public const uint ParamFormat = 4;
    private const uint FormatMediaType = 1, FormatMediaSubtype = 2;
    private const uint FormatVideoFormat = 0x20001, FormatVideoSize = 0x20003, FormatVideoFramerate = 0x20004;
    private const uint MediaVideo = 2, SubtypeRaw = 1;
    private const uint ChoiceRange = 1, ChoiceEnum = 3, ChoiceFlags = 4;
    private const uint BuffersDataType = 6;
    private const uint DataMemPtr = 1, DataMemFd = 2;

    public const uint VideoRgbx = 7, VideoBgrx = 8, VideoRgba = 11, VideoBgra = 12;

    // Raw video in the four 32-bit layouts that need at most a channel swap, any size, any rate. No
    // modifier is offered, which is what keeps a compositor on shared-memory buffers instead of GPU ones.
    public static byte[] VideoFormats() => Object(ObjectFormat, ParamEnumFormat, w =>
    {
        Property(w, FormatMediaType, TypeId, 4, b => b.Write(MediaVideo));
        Property(w, FormatMediaSubtype, TypeId, 4, b => b.Write(SubtypeRaw));
        Choice(w, FormatVideoFormat, ChoiceEnum, TypeId, 4, new uint[] { VideoBgrx, VideoBgrx, VideoBgra, VideoRgbx, VideoRgba });
        Choice(w, FormatVideoSize, ChoiceRange, TypeRectangle, 8, new uint[] { 1920, 1080, 1, 1, 16384, 16384 });
        Choice(w, FormatVideoFramerate, ChoiceRange, TypeFraction, 8, new uint[] { 60, 1, 0, 1, 1000, 1 });
    });

    // Buffers in memory this side can read, not GPU buffers it cannot.
    public static byte[] MemoryBuffers() => Object(ObjectBuffers, ParamBuffers, w =>
        Choice(w, BuffersDataType, ChoiceFlags, TypeInt, 4, new uint[] { (1u << (int)DataMemPtr) | (1u << (int)DataMemFd) }));

    // The fixed format a stream settled on: video layout and size.
    public static unsafe bool TryReadFormat(byte* pod, out uint format, out int width, out int height)
    {
        format = 0;
        width = height = 0;
        if (pod == null)
            return false;
        uint size = *(uint*)pod, type = *(uint*)(pod + 4);
        if (type != TypeObject || size < 8)
            return false;
        byte* at = pod + 16, end = pod + 8 + size;
        bool haveFormat = false, haveSize = false;
        while (at + 16 <= end)
        {
            uint key = *(uint*)at;
            uint valueSize = *(uint*)(at + 8), valueType = *(uint*)(at + 12);
            byte* body = at + 16;
            byte* next = body + ((valueSize + 7) & ~7u);
            // A fixed value can still arrive dressed as a choice; its first entry is the value.
            if (valueType == TypeChoice && valueSize >= 16)
            {
                valueType = *(uint*)(body + 12);
                valueSize = *(uint*)(body + 8);
                body += 16;
            }
            if (key == FormatVideoFormat && valueType == TypeId && valueSize >= 4)
            {
                format = *(uint*)body;
                haveFormat = true;
            }
            else if (key == FormatVideoSize && valueType == TypeRectangle && valueSize >= 8)
            {
                width = (int)*(uint*)body;
                height = (int)*(uint*)(body + 4);
                haveSize = true;
            }
            at = next;
        }
        return haveFormat && haveSize;
    }

    private static byte[] Object(uint objectType, uint id, Action<BinaryWriter> properties)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, System.Text.Encoding.ASCII, true))
        {
            w.Write(objectType);
            w.Write(id);
            properties(w);
        }
        using var pod = new MemoryStream();
        using (var w = new BinaryWriter(pod))
        {
            w.Write((uint)body.Length);
            w.Write(TypeObject);
            w.Write(body.ToArray());
            Pad(w);
        }
        return pod.ToArray();
    }

    private static void Property(BinaryWriter w, uint key, uint type, uint size, Action<BinaryWriter> value)
    {
        w.Write(key);
        w.Write(0u);
        w.Write(size);
        w.Write(type);
        value(w);
        Pad(w);
    }

    private static void Choice(BinaryWriter w, uint key, uint kind, uint childType, uint childSize, uint[] values)
    {
        int words = (int)(childSize / 4);
        uint bodySize = 16 + childSize * (uint)(values.Length / words);
        w.Write(key);
        w.Write(0u);
        w.Write(bodySize);
        w.Write(TypeChoice);
        w.Write(kind);
        w.Write(0u);
        w.Write(childSize);
        w.Write(childType);
        foreach (var value in values)
            w.Write(value);
        Pad(w);
    }

    private static void Pad(BinaryWriter w)
    {
        while (w.BaseStream.Position % 8 != 0)
            w.Write((byte)0);
    }
}
