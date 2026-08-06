// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking;
using Lumora.Nexus.Transport;

namespace Lumora.Core.Networking.Sync;

public class ControlMessage : SyncMessage
{
    public enum Message
    {
        JoinRequest,
        JoinGrant,
        JoinStartDelta,
        JoinReject,
        ServerClose,
        WorldSnapshot,
        RequestFullState,

        // Asset transfer protocol
        AssetRequest,
        // Job ID, URI, total byte count.
        AssetTransmissionStart,
        // Job ID, byte offset, chunk bytes.
        AssetChunk,
        AssetNextChunkRequest,
        AssetNotAvailable,

        // Host to joiner: nonce the joiner must sign with its machine key.
        JoinChallenge,
        // Joiner to host: the signed challenge nonce.
        JoinAuthenticate,

        // Session clock. Appended, never reordered: this enum goes on the wire as a byte, so an
        // existing value moving renames every message a peer on the other build sends. -xlinka
        // Peer to authority: clock probe carrying the sender's own stamp.
        ClockRequest,
        // Authority to peer: that stamp handed back plus the authority's reading.
        ClockReply,

        // Delta-loss recovery.
        // Peer -> authority: a specific set of elements whose incremental deltas no longer line up
        // with what the peer holds. Payload is a 7-bit count followed by that many RefIDs; the
        // authority answers with a full record for each, so one bad list costs one list instead of
        // the whole world.
        ResyncElements,
        // Peer -> authority: the peer has applied the full world state the authority pushed at it and
        // its outgoing deltas are based on that state from here. Lets the authority stop refusing the
        // peer's deltas and rebase the link cursor, which cannot be inferred from the peer's own
        // counter since that keeps running across the repair.
        ResyncComplete,
    }

    public override MessageType MessageType => MessageType.Control;
    public override bool Reliable => true;

    public Message ControlMessageType { get; set; }
    public byte[] Payload { get; set; } = null!;

    public ControlMessage(Message type, IConnection sender = null!)
        : base(0, 0, sender)
    {
        ControlMessageType = type;
    }

    public override byte[] Encode()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        writer.Write((byte)MessageType);
        writer.Write7BitEncoded(SenderStateVersion);
        writer.Write7BitEncoded(SenderSyncTick);
        writer.Write((byte)ControlMessageType);

        if (Payload != null)
        {
            writer.Write7BitEncoded((ulong)Payload.Length);
            writer.Write(Payload);
        }
        else
        {
            writer.Write7BitEncoded(0UL);
        }

        return output.ToArray();
    }

    public static ControlMessage Decode(BinaryReader reader)
    {
        var type = (Message)reader.ReadByte();
        var msg = new ControlMessage(type);

        // Bounded read: peer-declared payload length is capped before allocation.
        msg.Payload = reader.ReadBoundedBytes7Bit(NetworkLimits.MaxControlMessagePayload);
        return msg;
    }
}
