// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Produces per-viseme weights (0..1) from the owning user's voice, for lip-sync. Consumed by
/// <see cref="DirectVisemeDriver"/>.
///
/// NOTE: the audio analysis is not wired yet (no voice sample pipeline exists). Until then this
/// outputs pure silence (mouth closed). The component + interface are in place so the moment a voice
/// source lands, only <see cref="OnUpdate"/> needs filling in - nothing downstream changes.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class VisemeAnalyzer : Component
{
    private readonly float[] _weights = new float[(int)Viseme.COUNT];

    /// <summary>Silence weight (1 = no speech). Drivers use it to suppress mouth tracking.</summary>
    public float Silence => _weights[(int)Viseme.Silence];

    /// <summary>Current weight for a viseme (0..1).</summary>
    public float this[Viseme viseme]
    {
        get
        {
            int i = (int)viseme;
            return (i >= 0 && i < _weights.Length) ? _weights[i] : 0f;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // TODO(audio): read the owning user's voice audio source, run amplitude/formant analysis, and
        // fill _weights[(int)Viseme.X]. A pragmatic v1 = drive jaw-open (AA) from voice volume; full
        // 15-viseme formant classification is a follow-up DSP effort. Until the voice pipeline exists,
        // output pure silence so the mouth stays neutral.
        for (int i = 0; i < _weights.Length; i++)
            _weights[i] = 0f;
        _weights[(int)Viseme.Silence] = 1f;
    }
}
