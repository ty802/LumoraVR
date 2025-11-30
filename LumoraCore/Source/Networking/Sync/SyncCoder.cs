using System;
using System.IO;
using Lumora.Core.Math;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Binary encoding/decoding for sync types.
/// </summary>
public static class SyncCoder<T>
{
	public static void Encode(BinaryWriter writer, T value)
	{
		switch (value)
		{
			case bool b:
				writer.Write(b);
				break;
			case byte b:
				writer.Write(b);
				break;
			case sbyte sb:
				writer.Write(sb);
				break;
			case short s:
				writer.Write(s);
				break;
			case ushort us:
				writer.Write(us);
				break;
			case int i:
				writer.Write(i);
				break;
			case uint ui:
				writer.Write(ui);
				break;
			case long l:
				writer.Write(l);
				break;
			case ulong ul:
				writer.Write(ul);
				break;
			case float f:
				writer.Write(f);
				break;
			case double d:
				writer.Write(d);
				break;
			case string str:
				writer.Write(str ?? string.Empty);
				break;
			case float2 f2:
				writer.Write(f2.x);
				writer.Write(f2.y);
				break;
			case float3 f3:
				writer.Write(f3.x);
				writer.Write(f3.y);
				writer.Write(f3.z);
				break;
			case float4 f4:
				writer.Write(f4.x);
				writer.Write(f4.y);
				writer.Write(f4.z);
				writer.Write(f4.w);
				break;
			case floatQ q:
				writer.Write(q.x);
				writer.Write(q.y);
				writer.Write(q.z);
				writer.Write(q.w);
				break;
			case Enum e:
				writer.Write(Convert.ToInt32(e));
				break;
			default:
				throw new NotSupportedException($"SyncCoder: Type {typeof(T)} not supported");
		}
	}

	public static T Decode(BinaryReader reader)
	{
		var type = typeof(T);
		object result;

		if (type == typeof(bool))
			result = reader.ReadBoolean();
		else if (type == typeof(byte))
			result = reader.ReadByte();
		else if (type == typeof(sbyte))
			result = reader.ReadSByte();
		else if (type == typeof(short))
			result = reader.ReadInt16();
		else if (type == typeof(ushort))
			result = reader.ReadUInt16();
		else if (type == typeof(int))
			result = reader.ReadInt32();
		else if (type == typeof(uint))
			result = reader.ReadUInt32();
		else if (type == typeof(long))
			result = reader.ReadInt64();
		else if (type == typeof(ulong))
			result = reader.ReadUInt64();
		else if (type == typeof(float))
			result = reader.ReadSingle();
		else if (type == typeof(double))
			result = reader.ReadDouble();
		else if (type == typeof(string))
			result = reader.ReadString();
		else if (type == typeof(float2))
			result = new float2(reader.ReadSingle(), reader.ReadSingle());
		else if (type == typeof(float3))
			result = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		else if (type == typeof(float4))
			result = new float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		else if (type == typeof(floatQ))
			result = new floatQ(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		else if (type.IsEnum)
			result = Enum.ToObject(type, reader.ReadInt32());
		else
			throw new NotSupportedException($"SyncCoder: Type {type} not supported");

		return (T)result;
	}
}
