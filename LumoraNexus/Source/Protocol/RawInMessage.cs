// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Nexus.Transport;

namespace Lumora.Nexus.Protocol;

public class RawInMessage
{
    public byte[] Data { get; set; } = null!;
    public int Offset { get; set; }
    public int Length { get; set; }
    public IConnection Sender { get; set; } = null!;
}
