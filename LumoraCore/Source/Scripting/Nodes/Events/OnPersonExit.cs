// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A person who was touching the colliders, as On Person Enter means it, no longer is, or left the world
// while they were. -xlinka
[Node("Events/On Person Exit")]
[SourcedEvent]
public sealed class OnPersonExit : EventNode<PersonExited>
{
}
