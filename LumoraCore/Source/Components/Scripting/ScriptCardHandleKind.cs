// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

public enum ScriptCardHandleKind : byte
{
    Header,
    DataInput,
    DataOutput,
    FlowInput,
    FlowOutput,

    // A Write Value's "var": dragged onto a Stored Value card to choose what it writes.
    Variable
}
