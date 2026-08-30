// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core;
using Lumora.Core.Networking;
using Lumora.Nexus.Protocol;

namespace Lumora.Core.Networking.Messages;

public class ConfirmationMessage
{
    public MessageType Type => MessageType.Confirmation;
    public bool Reliable => true;

    public ulong AuthorityStateVersion { get; set; }

    public ulong ClientStateVersion { get; set; }

    public List<ConfirmationRecord> Records { get; set; } = new();

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)Type);
        writer.Write(AuthorityStateVersion);
        writer.Write(ClientStateVersion);
        writer.Write(Records.Count);

		foreach (var record in Records)
		{
			writer.WriteRefID(record.TargetID);
			writer.Write(record.MemberIndex);
			writer.Write(record.Accepted);

            if (!record.Accepted)
            {
                writer.Write(record.CorrectedData.Length);
                writer.Write(record.CorrectedData);
                writer.Write(record.RejectionReason ?? "");
            }
        }

        return ms.ToArray();
    }

    public static ConfirmationMessage Decode(BinaryReader reader)
    {
        var message = new ConfirmationMessage
        {
            AuthorityStateVersion = reader.ReadUInt64(),
            ClientStateVersion = reader.ReadUInt64()
        };

		int recordCount = reader.ReadInt32();
		for (int i = 0; i < recordCount; i++)
		{
			var record = new ConfirmationRecord
			{
				TargetID = reader.ReadRefID(),
				MemberIndex = reader.ReadInt32(),
				Accepted = reader.ReadBoolean()
			};

            if (!record.Accepted)
            {
                int dataLength = reader.ReadInt32();
                record.CorrectedData = reader.ReadBytes(dataLength);
                record.RejectionReason = reader.ReadString();
            }

            message.Records.Add(record);
        }

        return message;
    }
}

	public class ConfirmationRecord
	{
	public RefID TargetID { get; set; }

    public int MemberIndex { get; set; }

    public bool Accepted { get; set; }

    // Set when rejected: the authority's corrected value.
    public byte[] CorrectedData { get; set; } = null!;

    public string RejectionReason { get; set; } = null!;
}
