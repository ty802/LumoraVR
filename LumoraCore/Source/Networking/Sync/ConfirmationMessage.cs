// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking;
using Lumora.Nexus.Transport;

namespace Lumora.Core.Networking.Sync;

// Carries full state for conflicting elements.
public class ConfirmationMessage : BinaryMessageBatch
{
    public override MessageType MessageType => MessageType.Confirmation;
    public override bool Reliable => true;

    public ulong ConfirmTime { get; set; }

    public ConfirmationMessage(ulong confirmTime, ulong stateVersion, ulong syncTick, IConnection sender = null!)
        : base(stateVersion, syncTick, sender)
    {
        ConfirmTime = confirmTime;
    }
}
