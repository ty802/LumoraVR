// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// count items from start, as a new array; the part of that range outside the array is left out.
[Node("JSON/Arrays/Slice")]
public sealed class JsonSlice : ReadNode
{
    public static ScriptJsonArray Run(ScriptContextBase ctx, ScriptJsonArray? array, int start, int count)
        => array == null ? ScriptJsonArray.Empty : array.Slice(ctx.Collections, start, count);
}
