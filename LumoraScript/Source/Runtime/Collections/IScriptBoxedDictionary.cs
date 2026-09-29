// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Script;

// A dictionary read and filled without its key and value types, for code that knows them only as script
// types.
internal interface IScriptBoxedDictionary
{
    IEnumerable<(object Key, object? Value)> BoxedEntries();

    void SetBoxed(object key, object? value);
}
