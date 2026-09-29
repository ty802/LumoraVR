// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Web;

namespace Lumora.Core.Scripting;

// The websocket, once it opened, and what went wrong when it did not.
public readonly record struct SocketOpened(ScriptWebSocket? Socket, string Error);
