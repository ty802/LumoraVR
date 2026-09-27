// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A ticket's answer, waiting for the thread that drives the instance: which await node, which of its
// exits, and the payload for its outputs.
public readonly record struct ScriptAnswer(string NodeId, string Exit, object Payload);
