// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Starts work the host finishes later, such as a web request. The static Start is the node: an optional
// context first, then the ticket, then the inputs. Flow carries on through next at once. When the work is
// done its ticket answers with one of the exits [Answers] names, and that exit runs as a call of its own
// with the payload's properties on the node's outputs. Nothing the starting chain computed survives into
// that call, the same as after a Delay, and the outputs are readable only there. -xlinka
public abstract class AwaitNode<TPayload> : ScriptNode where TPayload : struct
{
}
