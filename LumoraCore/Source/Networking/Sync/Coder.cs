using System;
using System.IO;
using Lumora.Core.Math;

namespace Lumora.Core.Networking.Sync;

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
            case float3 v3:
                writer.Write(v3.x);
                writer.Write(v3.y);
                writer.Write(v3.z);
                break;
            case floatQ q:
                writer.Write(q.x);
                writer.Write(q.y);
                writer.Write(q.z);
                writer.Write(q.w);
                break;
            case color col:
                writer.Write(col.r);
                writer.Write(col.g);
                writer.Write(col.b);
                writer.Write(col.a);
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
        if (type == typeof(float3))
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return (T)(object)new float3(x, y, z);
        }
        if (type == typeof(floatQ))
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return (T)(object)new floatQ(x, y, z, w);
        }
        if (type == typeof(color))
        {
            float r = reader.ReadSingle();
            float g = reader.ReadSingle();
            float b = reader.ReadSingle();
            float a = reader.ReadSingle();
            return (T)(object)new color(r, g, b, a);
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
            return System.Math.Abs((float)(object)a - (float)(object)b) < 0.0001f;
        }
        if (typeof(T) == typeof(double))
        {
            return System.Math.Abs((double)(object)a - (double)(object)b) < 0.0001;
        }
        if (typeof(T) == typeof(float3))
        {
            float3 va = (float3)(object)a;
            float3 vb = (float3)(object)b;
            return va.Equals(vb); // Use float3's Equals method
        }
        if (typeof(T) == typeof(floatQ))
        {
            floatQ qa = (floatQ)(object)a;
            floatQ qb = (floatQ)(object)b;
            return qa.Equals(qb); // Use floatQ's Equals method
        }
        if (typeof(T) == typeof(color))
        {
            color ca = (color)(object)a;
            color cb = (color)(object)b;
            return ca.Equals(cb);
        }

        // Default equality
        return a.Equals(b);
    }
}
