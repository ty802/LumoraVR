// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public enum FlowShapeKind : byte
{
    Branch,
    Sequence,
    Repeat,
    Delay,
    While,
    Each,
    Once,
    Toggle,
    Cooldown,
    Range,
    ListEach,
    Scope,
    Change,
    Watch,
    Follow,
    FrameDelta,
    Drive
}
