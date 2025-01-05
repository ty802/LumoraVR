using System.Collections.Generic;
using System.IO;
using Godot;

namespace Aquamarine.Source.Helpers;

public static class SerializationHelpers
{
    public const int SessionControlChannel = 7;
    public const int WorldUpdateChannel = 8;
    public const int AssetChannel = 16;
    public const int PrefabChannel = 15;
    
    public static readonly StringName ReceiveChangesName = "ReceiveChanges";

    //DirtyFlags
    public static DirtyFlags8 ReadDirtyFlags8(this BinaryReader reader) => new(reader.ReadByte());
    public static DirtyFlags16 ReadDirtyFlags16(this BinaryReader reader) => new(reader.ReadUInt16());
    public static DirtyFlags32 ReadDirtyFlags32(this BinaryReader reader) => new(reader.ReadUInt32());
    public static DirtyFlags64 ReadDirtyFlags64(this BinaryReader reader) => new(reader.ReadUInt64());

    public static void Write(this BinaryWriter writer, DirtyFlags8 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags16 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags32 value) => writer.Write(value.Value);
    public static void Write(this BinaryWriter writer, DirtyFlags64 value) => writer.Write(value.Value);
    
    //Godot types
    public static Vector2 ReadVector2(this BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle());
    public static Vector3 ReadVector3(this BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    public static Vector4 ReadVector4(this BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    public static Color ReadColor(this BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    public static Vector2I ReadVector2I(this BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32());
    public static Vector3I ReadVector3I(this BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    public static Vector4I ReadVector4I(this BinaryReader reader) => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    
    public static void Write(this BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }
    public static void Write(this BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
    public static void Write(this BinaryWriter writer, Vector4 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }
    public static void Write(this BinaryWriter writer, Color value)
    {
        writer.Write(value.R);
        writer.Write(value.G);
        writer.Write(value.B);
        writer.Write(value.A);
    }
    public static void Write(this BinaryWriter writer, Vector2I value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }
    public static void Write(this BinaryWriter writer, Vector3I value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
    public static void Write(this BinaryWriter writer, Vector4I value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }
    
    public delegate void WriteAction<T>(BinaryWriter stream, T value);
    
    public static void WriteCollection<T>(this BinaryWriter stream, ICollection<T> collection, WriteAction<T> writer)
    {
        stream.Write(collection.Count);
        foreach (var item in collection) writer(stream, item);
    }

    public delegate void ReadAction<T>(BinaryReader stream, out T value);
    
    public static void ReadCollection<T>(this BinaryReader stream, ICollection<T> collection, ReadAction<T> reader)
    {
        collection.Clear();
        var length = stream.ReadInt32();
        for (var i = 0; i < length; i++)
        {
            reader(stream, out var value);
            collection.Add(value);
        }
    }
    public static void ReadArray<T>(this BinaryReader stream, out T[] array, ReadAction<T> reader)
    {
        var length = stream.ReadInt32();
        array = new T[length];
        for (var i = 0; i < length; i++)
        {
            reader(stream, out var value);
            array[i] = value;
        }
    }
}
