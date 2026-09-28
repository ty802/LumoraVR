// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A fresh random guid every time it is read. Seed Random does not make it repeat.
[Node("Guids/New Guid")]
public sealed class NewGuid : ReadNode
{
    [return: Port("guid")] public static Guid Run() => Guid.NewGuid();
}
