// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Enums the asset nodes hand out that no component keeps in a field, named the way component enums are:
// the type's name with its first letter lowered. One already known under that name is left as it is.
internal static class ScriptAssetEnums
{
    public static void Register()
    {
        Add(typeof(TextureFormatKind));
        Add(typeof(AssetLoadState));
    }

    private static void Add(Type type)
    {
        try
        {
            ScriptTypes.RegisterEnum(char.ToLowerInvariant(type.Name[0]) + type.Name[1..], type);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }
}
