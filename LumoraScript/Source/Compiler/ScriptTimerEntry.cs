// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// One Delay node: the export that runs what comes after it.
public sealed record ScriptTimerEntry(string NodeId, string Export);
