// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;

namespace Lumora.Godot.Hooks;

// The slot a physics node stands for, tagged on the node as its RefID. An integer under a key made once:
// it is read on every physics hit for every node up the chain, and a string key, a decimal string value and
// the parse were three allocations a node. -xlinka
internal static class SlotRefMeta
{
    private static readonly StringName Key = "LumoraSlotRef";

    // Handed to GetMeta so a node without the tag answers quietly; with no default Godot logs an error.
    private static readonly Variant Missing = false;

    public static void Tag(Node node, RefID id) => node.SetMeta(Key, unchecked((long)(ulong)id));

    public static bool TryRead(Node node, out RefID id)
    {
        var value = node.GetMeta(Key, Missing);
        if (value.VariantType == Variant.Type.Int)
        {
            id = new RefID(unchecked((ulong)value.AsInt64()));
            return true;
        }
        id = default;
        return false;
    }
}
