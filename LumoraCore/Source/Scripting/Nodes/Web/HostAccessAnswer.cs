// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The host the answer is for, as decisions are kept: lowercased, without scheme or port. Empty when the
// text named no host.
public readonly record struct HostAccessAnswer(string Key);
