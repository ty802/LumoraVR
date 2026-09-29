// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Someone finished a bite of the edible bound as its source, else the one on the object the script sits
// on. -xlinka
[Node("Events/On Bite Taken")]
[SourcedEvent]
public sealed class OnBiteTaken : EventNode<EdibleBite>
{
}
