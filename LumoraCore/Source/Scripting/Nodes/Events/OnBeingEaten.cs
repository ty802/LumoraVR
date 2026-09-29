// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Someone is eating the edible bound as its source, else the one on the object the script sits on: each
// time its bite moves along, which is every frame on the host and as often as the news arrives on other machines.
// -xlinka
[Node("Events/On Being Eaten")]
[SourcedEvent]
public sealed class OnBeingEaten : EventNode<EdibleEating>
{
}
