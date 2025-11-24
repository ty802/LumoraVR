using System.Collections.Generic;
using System.IO;

namespace Lumora.Core.Networking.Messages;

/// <summary>
/// Delta batch message - contains only changed properties.
/// 10-100x smaller than full state.
/// </summary>
public class DeltaBatch
{
    /// <summary>
    /// Sender's state version when this batch was created.
    /// Used for ordering and conflict detection.
    /// </summary>
    public ulong SenderStateVersion;

    /// <summary>
    /// World time when this batch was created.
    /// </summary>
    public double WorldTime;

    /// <summary>
    /// List of property changes.
    /// </summary>
    public List<DataRecord> Records = new();

    public MessageType Type => MessageType.Delta;
    public bool Reliable => true;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)Type);
        writer.Write(SenderStateVersion);
        writer.Write(WorldTime);
        writer.Write(Records.Count);

        foreach (var record in Records)
        {
            record.Encode(writer);
        }

        return ms.ToArray();
    }

    public static DeltaBatch Decode(BinaryReader reader)
    {
        var batch = new DeltaBatch
        {
            SenderStateVersion = reader.ReadUInt64(),
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
