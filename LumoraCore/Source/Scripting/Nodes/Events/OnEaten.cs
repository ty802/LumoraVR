// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The last bite of the edible bound as its source, else the one on the object the script sits on, was
// taken. It comes after that bite's On Bite Taken. -xlinka
[Node("Events/On Eaten")]
[SourcedEvent]
public sealed class OnEaten : EventNode<EdibleFinished>
{
}
