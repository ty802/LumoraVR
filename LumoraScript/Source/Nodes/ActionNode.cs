// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Changes the world. Ports come from the parameters after a leading context, and the In and Next flow
// ports are added without being declared. The runtime runs the body inside the script principal's
// scope, so a write is judged by the gate exactly like the author's own would be.
public abstract class ActionNode : ScriptNode
{
}
