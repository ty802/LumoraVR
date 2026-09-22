// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Audio;
using Lumora.Core.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Audio;

// Where this avatar's voice comes from, so anything that wants to read the wearer's audio (a mouth
// visualiser, a level meter on a nametag) finds it through the user root registry instead of guessing
// at a slot name.
[ComponentCategory("Audio")]
public class AvatarVoiceInfo : UserRootComponent
{
    public readonly SyncRef<Component> AudioSource;

    public AvatarVoiceInfo()
    {
        AudioSource = new SyncRef<Component>(this);
    }
}
