// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A slot left the one bound as its source, else the one the script sits on: moved elsewhere, or destroyed.
// Destroying the watched slot takes each of its children away first, and each is heard. Heard on the
// machine the script runs on, once for each child. -xlinka
[Node("Events/On Child Removed")]
[SourcedEvent]
public sealed class OnChildRemoved : EventNode<ChildRemoved>
{
}
