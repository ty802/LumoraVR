// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who changed the field, when the host can tell: someone else's change carries them, the host's own does not.
public readonly record struct FieldChanged(User? ChangedBy);
