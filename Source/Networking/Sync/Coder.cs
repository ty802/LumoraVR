using System;
using System.IO;
using Godot;

namespace Aquamarine.Source.Networking.Sync;

/// <summary>
/// Generic serialization system for network sync.
/// 
/// </summary>
public static class Coder<T>
{
    public static void Encode(BinaryWriter writer, T value)
    {
        // Handle common types
        switch (value)
        {
            case int i:
                writer.Write(i);
                break;
            case float f:
                writer.Write(f);
                break;
            case double d:
                writer.Write(d);
                break;
            case bool b:
                writer.Write(b);
                break;
            case string s:
                writer.Write(s ?? string.Empty);
                break;
            case Vector3 v3:
                writer.Write(v3.X);
                writer.Write(v3.Y);
                writer.Write(v3.Z);
                break;
            case Quaternion q:
                writer.Write(q.X);
                writer.Write(q.Y);
                writer.Write(q.Z);
                writer.Write(q.W);
                break;
            case Color c:
                writer.Write(c.R);
                writer.Write(c.G);
                writer.Write(c.B);
                writer.Write(c.A);
                break;
            case ulong ul:
                writer.Write(ul);
                break;
            case long l:
                writer.Write(l);
                break;
            case ushort us:
                writer.Write(us);
                break;
            case short s:
                writer.Write(s);
                break;
            case byte b:
                writer.Write(b);
                break;
            default:
                throw new NotSupportedException($"Type {typeof(T)} is not supported for encoding");
        }
    }

    public static T Decode(BinaryReader reader)
    {
        Type type = typeof(T);

        // Handle common types
        if (type == typeof(int))
            return (T)(object)reader.ReadInt32();
        if (type == typeof(float))
            return (T)(object)reader.ReadSingle();
        if (type == typeof(double))
            return (T)(object)reader.ReadDouble();
        if (type == typeof(bool))
            return (T)(object)reader.ReadBoolean();
        if (type == typeof(string))
            return (T)(object)reader.ReadString();
        if (type == typeof(Vector3))
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return (T)(object)new Vector3(x, y, z);
        }
        if (type == typeof(Quaternion))
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return (T)(object)new Quaternion(x, y, z, w);
        }
        if (type == typeof(Color))
        {
            float r = reader.ReadSingle();
            float g = reader.ReadSingle();
            float b = reader.ReadSingle();
            float a = reader.ReadSingle();
            return (T)(object)new Color(r, g, b, a);
        }
        if (type == typeof(ulong))
            return (T)(object)reader.ReadUInt64();
        if (type == typeof(long))
            return (T)(object)reader.ReadInt64();
        if (type == typeof(ushort))
            return (T)(object)reader.ReadUInt16();
        if (type == typeof(short))
            return (T)(object)reader.ReadInt16();
        if (type == typeof(byte))
            return (T)(object)reader.ReadByte();

        throw new NotSupportedException($"Type {type} is not supported for decoding");
    }

    public static bool Equals(T a, T b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Use type-specific equality
        if (typeof(T) == typeof(float))
        {
            return Math.Abs((float)(object)a - (float)(object)b) < 0.0001f;
        }
        if (typeof(T) == typeof(double))
        {
            return Math.Abs((double)(object)a - (double)(object)b) < 0.0001;
        }
        if (typeof(T) == typeof(Vector3))
        {
            Vector3 va = (Vector3)(object)a;
            Vector3 vb = (Vector3)(object)b;
            return va.IsEqualApprox(vb);
        }
        if (typeof(T) == typeof(Quaternion))
        {
            Quaternion qa = (Quaternion)(object)a;
            Quaternion qb = (Quaternion)(object)b;
            return qa.IsEqualApprox(qb);
        }

        // Default equality
        return a.Equals(b);
    }
}
