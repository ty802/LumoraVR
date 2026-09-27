// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Binding slot n is handle n + 1 in every instance, pinned before the module starts.
public sealed record ScriptBindingEntry(string Name, ScriptType Type);
