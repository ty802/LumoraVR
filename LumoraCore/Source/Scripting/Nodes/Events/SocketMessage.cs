// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Web;

namespace Lumora.Core.Scripting;

// The websocket a text message came in on, and the text.
public readonly record struct SocketMessage(ScriptWebSocket? Socket, string Text);
