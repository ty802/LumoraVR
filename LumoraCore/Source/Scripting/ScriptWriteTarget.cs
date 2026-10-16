// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;

namespace Lumora.Core.Scripting;

// One thing a node writes to and the port it names it on: a field, a reference, a component, a slot, or the
// Stored Value card a Write Value writes.
public readonly record struct ScriptWriteTarget(ScriptGraphNode Writer, string Port, ScriptCardHandleKind PortKind, IWorldElement Target);
