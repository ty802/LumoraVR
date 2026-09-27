// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Starts a flow when something happens. The payload's public properties become the outputs, in the
// order the payload declares them.
public abstract class EventNode<T> : ScriptNode where T : struct
{
}
