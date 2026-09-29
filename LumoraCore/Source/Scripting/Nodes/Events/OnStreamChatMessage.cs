// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A message in the live chat of the stream chat reader on the source object, the script's own when none is
// bound. Each machine reads the chat itself, so a Shared script hears the host's reader and a Local one
// its own machine's. A reader added to the object later is heard from then on. -xlinka
[Node("Events/Stream Chat/On Message")]
[SourcedEvent]
public sealed class OnStreamChatMessage : EventNode<StreamChatMessage>
{
}
