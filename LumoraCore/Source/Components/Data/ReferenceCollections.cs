// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Data;

// Storage components whose entries are world references rather than values: a set of spawn points, the
// slots a tool cycles through, a named lookup something else drives off. Entries are real reference
// members, so each one is a drop target, follows the clone on duplicate, and comes back resolved after
// a save. See ReferenceHolder for the single-entry version. -xlinka

[ComponentCategory("Data")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceList<T> : Component where T : class, IWorldElement
{
    public readonly SyncRefList<T> References;

    public ReferenceList()
    {
        References = new SyncRefList<T>(this);
    }
}

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
