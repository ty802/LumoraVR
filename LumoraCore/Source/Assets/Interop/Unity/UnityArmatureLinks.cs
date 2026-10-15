// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Avatar;

namespace Lumora.Core.Assets.Interop.Unity;

// The rest of a VRCFury armature link's settings. A garment that tags its bone names ("PantsShorts_Hips")
// names the tag to take off before matching; with none named, the tag is whatever the garment's own root bone
// adds to the name of the bone it links to ("Hips.Hoodie" onto "Hips"). The keep-offsets choice decides
// whether matched bones keep where they are or snap onto the body's. -xlinka
public static partial class UnityAvatarBuilder
{
    private static void LinkOptions(ArmatureLink linker, UnityNode data, Slot prop, Slot target)
    {
        var tag = data["removeBoneSuffix"]?.Text?.Trim() ?? "";
        if (tag.Length > 0)
        {
            linker.NamePrefix.Value = tag;
            linker.NameSuffix.Value = tag;
        }
        else
        {
            var own = prop.SlotName.Value ?? "";
            var body = target.SlotName.Value ?? "";
            if (body.Length > 0 && own.Length > body.Length)
            {
                if (own.StartsWith(body, StringComparison.Ordinal))
                    linker.NameSuffix.Value = own[body.Length..];
                else if (own.EndsWith(body, StringComparison.Ordinal))
                    linker.NamePrefix.Value = own[..^body.Length];
            }
        }
        switch (data["keepBoneOffsets2"]?.Int() ?? 0)
        {
            case 1:
                linker.KeepPose.Value = true;
                break;
            case 2:
                linker.KeepPose.Value = false;
                break;
        }
    }
}
