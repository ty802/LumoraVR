// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A ticket's answer, waiting for the thread that drives the instance: which await node, which of its
// exits, the payload for its outputs, and the call of a waiting Function it runs on in, 0 for none.
public readonly record struct ScriptAnswer(string NodeId, string Exit, object Payload, int Word = 0);
