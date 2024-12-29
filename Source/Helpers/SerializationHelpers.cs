using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aquamarine.Source.Scene.ObjectTypes;
using Godot;

namespace Aquamarine.Source.Helpers;

public static class SerializationHelpers
{
    public const int WorldUpdateChannel = 8;
    public const int AssetChannel = 16;
    
    public static readonly StringName ReceiveChangesName = "ReceiveChanges";


    public static DirtyFlags8 ReadDirtyFlags8(this BinaryReader reader) => new(reader.ReadByte());
    public static DirtyFlags16 ReadDirtyFlags16(this BinaryReader reader) => new(reader.ReadUInt16());
    public static DirtyFlags32 ReadDirtyFlags32(this BinaryReader reader) => new(reader.ReadUInt32());
    public static DirtyFlags64 ReadDirtyFlags64(this BinaryReader reader) => new(reader.ReadUInt64());

    public static void Write(this BinaryWriter writer, DirtyFlags8 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags16 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags32 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags64 value) => writer.Write(value.Value);
    
    public delegate void WriteAction<T>(BinaryWriter stream, T value);
    
    public static void WriteArray<T>(this BinaryWriter stream, ICollection<T> collection, WriteAction<T> writer) where T : struct
    {
        stream.Write(collection.Count);
        foreach (var item in collection) writer(stream, item);
    }

    public delegate void ReadAction<T>(BinaryReader stream, out T value);
    
    public static void ReadArray<T>(this BinaryReader stream, ICollection<T> collection, ReadAction<T> reader) where T : struct
    {
        collection.Clear();
        var length = stream.ReadInt32();
        for (var i = 0; i < length; i++)
        {
            reader(stream, out var value);
            collection.Add(value);
        }
    }
}
