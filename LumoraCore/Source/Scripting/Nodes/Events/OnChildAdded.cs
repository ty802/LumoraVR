// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A slot came directly under the one bound as its source, else the one the script sits on: made there, or
// moved there from elsewhere. Heard on the machine the script runs on, once for each child. -xlinka
[Node("Events/On Child Added")]
[SourcedEvent]
public sealed class OnChildAdded : EventNode<ChildAdded>
{
}
