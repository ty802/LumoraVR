// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Networking;

/// <summary>
/// Outgoing message bundle: a payload plus the connections to deliver it to,
/// with delivery flags. Used by transports that want to batch sends across
/// multiple targets in one frame.
/// </summary>
public sealed class RawOutMessage
{
    public byte[] Data { get; }
    public int Length { get; }
    public IReadOnlyList<IConnection> Targets { get; }
    public bool UseReliable { get; }
    public bool Background { get; }

    /// <summary>
    /// Optional callback invoked once the transport has finished consuming
    /// <see cref="Data"/>. Useful for returning the buffer to a pool.
    /// </summary>
    public Action OnTransmitted { get; set; } = null!;

    public RawOutMessage(byte[] data, int length, IReadOnlyList<IConnection> targets, bool reliable, bool background)
    {
        Data = data;
        Length = length;
        Targets = targets;
        UseReliable = reliable;
        Background = background;
    }

    public void TransmissionFinished() => OnTransmitted?.Invoke();
}
