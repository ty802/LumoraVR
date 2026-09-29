// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Scripting;

// Who equipped the tool, and in which hand.
public readonly record struct ToolEquipped(User? User, Chirality Side);
