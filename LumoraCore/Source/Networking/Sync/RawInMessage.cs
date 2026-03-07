// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Raw incoming network message.
/// </summary>
public class RawInMessage
{
    public byte[] Data { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public IConnection Sender { get; set; }
}
