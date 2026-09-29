// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Which variable moved: its scope's name (empty for a scope without one) and its own. Found is false when
// it is gone, or can no longer be reached from the source.
public readonly record struct ScopedVariableChanged(string Scope, string Name, bool Found);
