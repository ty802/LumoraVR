// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking;
using Lumora.Nexus.Transport;

namespace Lumora.Core.Networking.Sync;

public class FullBatch : BinaryMessageBatch
{
    public override MessageType MessageType => MessageType.Full;
    public override bool Reliable => true;
    public bool UseBackgroundQueue { get; set; }
    public override bool Background => UseBackgroundQueue;

    // A world handed to a joiner in slices says on every slice how many records the whole of it holds, so
    // the joiner's progress counts against the world and not against the first slice. Zero on any other
    // full batch: the batch is the whole of it. -xlinka
    public int ExpectedRecords { get; set; }

    public FullBatch(ulong stateVersion, ulong syncTick, IConnection sender = null!)
        : base(stateVersion, syncTick, sender)
    {
    }
}
