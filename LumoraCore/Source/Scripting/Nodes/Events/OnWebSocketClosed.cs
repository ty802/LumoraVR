// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A websocket this script opened closed, once, whichever side closed it. Opening is Connect's own answer, so
// with it this covers the whole life of a connection.
[Node("Events/On WebSocket Closed")]
public sealed class OnWebSocketClosed : EventNode<SocketClosed>
{
}
