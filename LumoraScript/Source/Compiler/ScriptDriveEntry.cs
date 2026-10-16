// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Script;

// A drive node: the binding holding the field it writes, that field's type, the type of the value, and the
// globals holding what the drives function worked out for it last, which read like a stored variable's.
public sealed record ScriptDriveEntry(string NodeId, int Binding, ScriptType FieldType, ScriptType ValueType,
    IReadOnlyList<string> Exports, string? OnExport = null, int HeardBinding = -1, int HeardCountBinding = -1);
