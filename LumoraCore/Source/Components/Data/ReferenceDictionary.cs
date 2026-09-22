// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Data;

// String keys because the component browser only offers single-argument generics, and a name is the
// key content authors actually reach for.
[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceDictionary<T> : Component where T : class, IWorldElement
{
    public readonly SyncRefDictionary<string, T> References;

    public ReferenceDictionary()
    {
        References = new SyncRefDictionary<string, T>();
    }
}
