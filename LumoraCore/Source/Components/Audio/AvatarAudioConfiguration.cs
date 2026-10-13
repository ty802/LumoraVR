// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Audio;
using Lumora.Core.Audio;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Audio;

// What an avatar wants its owner's voice to sound like. Optional: an avatar without one falls back to
// the manager's own presets, which is why every getter here can answer null.
//
// Whisper is deliberately absent. A whisper is a promise about how far the words travel and letting an
// avatar retune it would turn that promise into a suggestion. -xlinka
[ComponentCategory("Audio")]
public class AvatarAudioConfiguration : UserRootComponent
{
    public readonly VoiceModeConfig Normal = new();
    public readonly VoiceModeConfig Shout = new();
    public readonly VoiceModeConfig Broadcast = new();

    private bool _seeded;

    // The presets are seeded on the machine that MADE the component: everyone else gets the values with it.
    // Seeding in OnAwake ran on every machine that decoded one, so each joiner wrote the presets over the
    // owner's voice settings, refused, and would have wiped any retuning had it been let through. -xlinka
    public override void OnInit()
    {
        base.OnInit();
        if (IsMintedHere)
            SeedPresets();
    }

    // A save written before a mode existed leaves it at the plain field defaults; that mode gets its preset.
    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);
        if (!Normal.ValueCameFromData)
            Normal.ApplyPreset(VoiceMode.Normal);
        if (!Shout.ValueCameFromData)
            Shout.ApplyPreset(VoiceMode.Shout);
        if (!Broadcast.ValueCameFromData)
            Broadcast.ApplyPreset(VoiceMode.Broadcast);
    }

    private void SeedPresets()
    {
        if (_seeded)
            return;
        _seeded = true;
        Normal.ApplyPreset(VoiceMode.Normal);
        Shout.ApplyPreset(VoiceMode.Shout);
        Broadcast.ApplyPreset(VoiceMode.Broadcast);
    }

    public VoiceModeConfig? TryGetConfig(VoiceMode mode) => mode switch
    {
        VoiceMode.Normal => Normal,
        VoiceMode.Shout => Shout,
        VoiceMode.Broadcast => Broadcast,
        _ => null,
    };
}
