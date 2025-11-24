using System.Collections.Generic;
using System.IO;

namespace Lumora.Core.Networking.Messages;

/// <summary>
/// Full state batch - contains complete state for all objects.
/// Sent to new users or for conflict resolution.
/// </summary>
public class FullBatch
{
	/// <summary>
	/// Authority's current state version.
	/// </summary>
	public ulong StateVersion;

	/// <summary>
	/// Current world time.
	/// </summary>
	public double WorldTime;

	/// <summary>
	/// Complete state of all sync members.
	/// </summary>
	public List<DataRecord> Records = new();

	public MessageType Type => MessageType.Full;
	public bool Reliable => true;

	public byte[] Encode()
	{
		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write((byte)Type);
		writer.Write(StateVersion);
		writer.Write(WorldTime);
		writer.Write(Records.Count);

		foreach (var record in Records)
		{
			record.Encode(writer);
		}

		return ms.ToArray();
	}

	public static FullBatch Decode(BinaryReader reader)
	{
		var batch = new FullBatch
		{
			StateVersion = reader.ReadUInt64(),
			WorldTime = reader.ReadDouble()
		};

		int count = reader.ReadInt32();
		for (int i = 0; i < count; i++)
		{
			batch.Records.Add(DataRecord.Decode(reader));
		}

		return batch;
	}
}
