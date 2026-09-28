// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Something stopped touching the object bound as its source, else the one the script sits on.
[Node("Events/On Collision Exit")]
[SourcedEvent]
public sealed class OnCollisionExit : EventNode<CollisionExited>
{
}
