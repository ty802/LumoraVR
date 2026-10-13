// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Script;

// What a call answers with: a value for each of the waiting node's outputs, in order, and the ports the call
// was made with, so an answer meant for other ports runs nothing.
public sealed record CallAnswer(string Signature, IReadOnlyList<object?> Values);
