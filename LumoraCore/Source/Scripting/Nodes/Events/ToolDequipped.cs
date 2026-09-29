// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Scripting;

// Who had the tool equipped when it was put away, and in which hand.
public readonly record struct ToolDequipped(User? User, Chirality Side);
