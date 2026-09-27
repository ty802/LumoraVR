// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A button came back up: the one bound as its source, else one on the object the script sits on.
[Node("Events/On Released")]
[SourcedEvent]
public sealed class OnReleased : EventNode<ButtonReleased>
{
}
