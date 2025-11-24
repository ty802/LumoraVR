using System.IO;

namespace Lumora.Core.Networking.Messages;

/// <summary>
/// Represents a single property change.
/// Part of delta and full batches.
/// </summary>
public struct DataRecord
{
    /// <summary>
    /// Target object's reference ID.
    /// </summary>
    public ulong TargetID;

    /// <summary>
    /// Index of the sync member being updated.
    /// </summary>
    public int MemberIndex;

    /// <summary>
    /// Serialized property data.
    /// </summary>
    public byte[] Data;

    public void Encode(BinaryWriter writer)
    {
        writer.Write(TargetID);
        writer.Write(MemberIndex);
        writer.Write(Data.Length);
        writer.Write(Data);
    }

    public static DataRecord Decode(BinaryReader reader)
    {
        return new DataRecord
        {
            TargetID = reader.ReadUInt64(),
            MemberIndex = reader.ReadInt32(),
            Data = reader.ReadBytes(reader.ReadInt32())
        };
    }
}
