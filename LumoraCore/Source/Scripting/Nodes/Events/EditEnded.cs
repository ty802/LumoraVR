// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who stopped typing, and what the field held then.
public readonly record struct EditEnded(User? User, string Text);
