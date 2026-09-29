// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Script;

// A list read and filled without its item type, for code that knows the type only as a script type.
internal interface IScriptBoxedList
{
    IEnumerable<object?> BoxedItems();

    void AddBoxed(object? item);
}
