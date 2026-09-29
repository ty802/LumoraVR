// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who is typing, and what the field holds after the change.
public readonly record struct TextEdited(User? User, string Text);
