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
    
    /*
    public static (BinaryWriter writer, MemoryStream stream) StartSerialize(this IRootObject obj)
    {
        var stream = new MemoryStream(0xFF);
        var writer = new BinaryWriter(stream);
        return (writer, stream);
    }
    public static (BinaryReader reader, MemoryStream stream) StartDeserialize(this IRootObject obj, byte[] data)
    {
        var stream = new MemoryStream(data);
        var reader = new BinaryReader(stream);
        return (reader, stream);
    }
    public static void InternalSendChanges(this IRootObject obj)
    {
        var (writer, stream) = StartSerialize(obj);
        
        obj.Serialize(writer);
        
        foreach (var item in obj.ChildObjects.Where(i => i.Value.Dirty))
        {
            writer.Write(item.Key);
            item.Value.Serialize(writer);
        }
        var bytes = stream.ToArray();
        obj.Self.Rpc(ReceiveChangesName, bytes);
        obj.Dirty = false;
    }
    
    public static void InternalReceiveChanges(this IRootObject obj, byte[] data)
    {
        try
        {
            var (reader, stream) = obj.StartDeserialize(data);

            obj.Deserialize(reader);

            while (stream.Position < stream.Length)
            {
                var index = reader.ReadUInt16();
                obj.ChildObjects[index].Deserialize(reader);
            }
        }
        catch (Exception e)
        {
            GD.Print(e);
        }
    }
    */
    
    // TODO: a lot of this can probably be optimized with unsafe operations, but i don't feel like doing that right now
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
