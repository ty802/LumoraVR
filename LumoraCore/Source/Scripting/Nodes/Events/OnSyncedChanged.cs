// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// A synced value kept on the source object changed, came or went, by name. Heard on every machine the
// frame after the change reaches it, once a frame however often it changed. -xlinka
[Node("Events/On Synced Changed")]
[SourcedEvent]
public sealed class OnSyncedChanged : EventNode<SyncedChanged>
{
}
