// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who pressed a Call node's button. The host reads the caller from the pressing machine's relay byte,
// never from a field that machine wrote.
public readonly record struct CallPressed(User? Caller);
