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
    
    public static bool TryGetSByte(this Variant variant, out sbyte value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsSByte();
                return true;
            case Variant.Type.String or Variant.Type.StringName when sbyte.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetByte(this Variant variant, out byte value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsByte();
                return true;
            case Variant.Type.String or Variant.Type.StringName when byte.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetInt16(this Variant variant, out short value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsInt16();
                return true;
            case Variant.Type.String or Variant.Type.StringName when short.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetUInt16(this Variant variant, out ushort value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsUInt16();
                return true;
            case Variant.Type.String or Variant.Type.StringName when ushort.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetInt32(this Variant variant, out int value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsInt32();
                return true;
            case Variant.Type.String or Variant.Type.StringName when int.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetUInt32(this Variant variant, out uint value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsUInt32();
                return true;
            case Variant.Type.String or Variant.Type.StringName when uint.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetInt64(this Variant variant, out long value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsInt64();
                return true;
            case Variant.Type.String or Variant.Type.StringName when long.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetUInt64(this Variant variant, out ulong value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsUInt64();
                return true;
            case Variant.Type.String or Variant.Type.StringName when ulong.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetSingle(this Variant variant, out float value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsSingle();
                return true;
            case Variant.Type.String or Variant.Type.StringName when float.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
    public static bool TryGetDouble(this Variant variant, out double value)
    {
        switch (variant.VariantType)
        {
            case Variant.Type.Int or Variant.Type.Float:
                value = variant.AsDouble();
                return true;
            case Variant.Type.String or Variant.Type.StringName when double.TryParse(variant.AsString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }

    
    public static float[] AsFloatArray(this Transform3D transform)
    {
        var basis = transform.Basis;
        var col0 = basis.Column0;
        var col1 = basis.Column1;
        var col2 = basis.Column2;
        var origin = transform.Origin;
        return
        [
            col0.X,
            col0.Y,
            col0.Z,
            col1.X,
            col1.Y,
            col1.Z,
            col2.X,
            col2.Y,
            col2.Z,
            origin.X,
            origin.Y,
            origin.Z,
        ];
    }
    public static Transform3D ToTransform3D(this float[] a)
    {
        if (a.Length != 12) return Transform3D.Identity;
        return new Transform3D(
            new Vector3(a[0], a[1], a[2]), 
            new Vector3(a[3], a[4], a[5]), 
            new Vector3(a[6], a[7], a[8]), 
            new Vector3(a[9], a[10], a[11]));
    }

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
    public static Basis ReadBasis(this BinaryReader reader) => new(reader.ReadVector3(), reader.ReadVector3(), reader.ReadVector3());
    public static Transform3D ReadTransform3D(this BinaryReader reader) => new(reader.ReadBasis(), reader.ReadVector3());
    
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
    public static void Write(this BinaryWriter writer, Basis value)
    {
        writer.Write(value.Column0);
        writer.Write(value.Column1);
        writer.Write(value.Column2);
    }
    public static void Write(this BinaryWriter writer, Transform3D value)
    {
        writer.Write(value.Basis);
        writer.Write(value.Origin);
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
