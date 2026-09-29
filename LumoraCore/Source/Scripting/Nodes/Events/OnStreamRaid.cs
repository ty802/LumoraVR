// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Another channel raided the one the source object's stream chat reader follows. Heard like On Message.
[Node("Events/Stream Chat/On Raid")]
[SourcedEvent]
public sealed class OnStreamRaid : EventNode<StreamRaid>
{
}
