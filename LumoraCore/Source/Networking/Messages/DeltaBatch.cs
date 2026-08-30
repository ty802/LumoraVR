// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking;
using Lumora.Nexus.Transport;

namespace Lumora.Core.Networking.Sync;

// Sent every sync tick.
public class DeltaBatch : BinaryMessageBatch
{
    public override MessageType MessageType => MessageType.Delta;
    public override bool Reliable => true;

    public DeltaBatch(ulong stateVersion, ulong syncTick, IConnection sender = null!)
        : base(stateVersion, syncTick, sender)
    {
    }
}
