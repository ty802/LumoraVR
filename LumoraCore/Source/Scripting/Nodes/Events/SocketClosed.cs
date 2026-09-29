// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Web;

namespace Lumora.Core.Scripting;

// The websocket that closed and why: closed by the script, by the server (with its code and words), a
// message too big, or the connection lost.
public readonly record struct SocketClosed(ScriptWebSocket? Socket, string Reason);
