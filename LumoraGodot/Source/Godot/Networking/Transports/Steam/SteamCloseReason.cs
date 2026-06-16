// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Godot.Networking.Transports.Steam;

/// <summary>
/// Why a SteamConnection closed. Reported back through
/// <see cref="Lumora.Core.Networking.IConnection.FailReason"/> for logging /
/// reconnect logic. - xlinka
/// </summary>
public enum SteamCloseReason
{
    Undefined,
    ClosedLocally,
    ClosedRemotely,
    LocalProblem,
    ChannelMismatch,
    TransmissionError,
    ReceiveError,
    UnhandledException,
}
