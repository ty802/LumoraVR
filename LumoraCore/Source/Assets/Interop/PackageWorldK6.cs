// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

public static partial class PackageTypeMap
{
    static partial void AliasesK6(Dictionary<string, string> aliases)
    {
        // Puts the view, the tracked space or the ears of whoever carries the object onto a slot of it. Its
        // Root, View and Ears are ours by the same names.
        aliases["AvatarUserRootOverrideAssigner"] = "HolderViewOverride";

        // A body slot of a user's rig. Saved in a world it is never equipped, and the override above names it
        // for one thing only: whoever's rig the slot it sits on has been carried into, a grab being what
        // carries it. HolderViewOverride reads that straight off its own slot, and only believes it while the
        // carrier's own hand holds the object or the carrier's own socket wears it. Not our AvatarSocket, which is a
        // part of a user's rig and means nothing outside one. -xlinka
        Carried.Add("AvatarObjectSlot");
    }
}

public static partial class PackageMemberMapper
{
    private const string HandledK6 = "\u0000k6";

    static partial void MemberAliasesK6(Dictionary<string, Dictionary<string, string>> table)
    {
        table["HolderViewOverride"] = new(StringComparer.Ordinal)
        {
            ["Override"] = "Target",
            ["Node"] = "Mode",
            // The equipping handle, folded into the component's own slot (see AliasesK6).
            ["_equippingSlot"] = HandledK6,
        };
    }

    static partial void ApplyK6(Component component, DataTreeDictionary data, Action<ISyncRef, string> queueReference,
        Action<object, string, IReadOnlyList<string>> queueReferenceList, Func<Uri, Uri?> remapUrl)
    {
    }
}
