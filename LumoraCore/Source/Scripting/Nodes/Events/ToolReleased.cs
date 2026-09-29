// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who let go of the tool's button, and whether it was secondary rather than primary.
public readonly record struct ToolReleased(User? User, bool Secondary);
