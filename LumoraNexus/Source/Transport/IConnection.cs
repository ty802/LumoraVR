// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;

namespace Lumora.Nexus.Transport;

public interface IConnection : IDisposable
{
    bool IsOpen { get; }

    string FailReason { get; }

    IPAddress IP { get; }

    Uri Address { get; }

    string Identifier { get; }

    ulong ReceivedBytes { get; }
    // What went out on the wire, the same measure ReceivedBytes takes of what came in.
    ulong SentBytes { get; }

    // Milliseconds, -1 if unknown (e.g. not yet connected). Diagnostics only.
    int Ping { get; }

    // Packets sent on this link since it opened, how many of those had to go again, and reliable packets
    // still waiting to go out. Zero where the transport does not count them. Diagnostics only.
    long PacketsSent => 0;

    long PacketsLost => 0;

    int QueuedPackets => 0;

    // Bytes handed to this link that have not gone out on the wire yet, every channel: how far behind the
    // peer is. Zero where the transport cannot tell. Read by the sync loop to pace what it sends.
    long PendingSendBytes => 0;

    // Diagnostics/telemetry only.
    bool IsEncrypted { get; }

    // Short transport name for diagnostics, e.g. "LNL", "Steam".
    string TransportName { get; }

    event Action<IConnection> Closed;

    event Action<IConnection> Connected;

    event Action<IConnection> ConnectionFailed;

    event Action<byte[], int> DataReceived;

    void Connect(Action<string> statusCallback);

    void Close();

    void Send(byte[] data, int length, bool reliable, bool background);

    // Reliable, but free to arrive out of order with other bulk sends: for data that says where it goes,
    // such as asset chunks. A transport with several reliable lanes spreads it over them.
    void SendBulk(byte[] data, int length) => Send(data, length, reliable: true, background: true);

    // Drive the transport for this connection. May be a no-op for transports
    // whose manager polls connections centrally (e.g. Steam). Called from the
    // session manager's per-frame poll loop. - xlinka
    void Poll();
}
