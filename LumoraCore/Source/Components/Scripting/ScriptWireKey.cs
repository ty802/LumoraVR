// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

// Which wire a curve is, the way the graph keeps it: an input's feed, owned by the node reading it, or an
// exit's next step, owned by the node the flow leaves.
public readonly record struct ScriptWireKey(ScriptGraphNode Owner, bool Exit, string Port);
