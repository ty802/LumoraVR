// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who submitted, and what they submitted.
public readonly record struct TextSubmitted(User? User, string Text);
