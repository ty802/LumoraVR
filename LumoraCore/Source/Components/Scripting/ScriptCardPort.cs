// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Components.Scripting;

// One port row on a card: its name, which side and kind, and the data type it carries (none for flow).
public readonly record struct ScriptCardPort(string Name, ScriptCardHandleKind Kind, ScriptType? Type);
