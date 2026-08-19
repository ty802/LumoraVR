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

    // Milliseconds, -1 if unknown (e.g. not yet connected). Diagnostics only.
    int Ping { get; }

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

    // Drive the transport for this connection. May be a no-op for transports
    // whose manager polls connections centrally (e.g. Steam). Called from the
    // session manager's per-frame poll loop. - xlinka
    void Poll();
}
