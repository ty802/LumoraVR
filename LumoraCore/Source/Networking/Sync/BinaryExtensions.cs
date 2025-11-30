using System.IO;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Binary reader/writer extensions for 7-bit encoded integers.
/// </summary>
public static class BinaryExtensions
{
	public static void Write7BitEncoded(this BinaryWriter writer, ulong value)
	{
		while (value >= 0x80)
		{
			writer.Write((byte)(value | 0x80));
			value >>= 7;
		}
		writer.Write((byte)value);
	}

	public static ulong Read7BitEncodedUInt64(this BinaryReader reader)
	{
		ulong result = 0;
		int shift = 0;
		byte b;

		do
		{
			b = reader.ReadByte();
			result |= (ulong)(b & 0x7F) << shift;
			shift += 7;
		} while ((b & 0x80) != 0);

		return result;
	}
}
