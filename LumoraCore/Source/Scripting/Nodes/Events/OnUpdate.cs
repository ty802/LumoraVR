// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Every frame, after the script's queued events.
[Node("Events/On Update")]
public sealed class OnUpdate : EventNode<FrameTick>
{
}
