// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// Runs another effect at a weight: 0 is the dry signal, 1 the other effect alone, and a change glides.
// The weight is applied where the sound goes into the effect, so pulling it down lets a reverb's tail ring
// out instead of fading it away. An effect that is off, gone, already running in another chain, or that
// leads back round to this blend is left out, and the blend passes the sound through untouched.
[ComponentCategory("Audio")]
public class AudioEffectBlend : AudioEffect
{
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> BlendWeight;

    public readonly SyncRef<AudioEffect> Effect;

    public AudioEffectBlend()
    {
        BlendWeight = new Sync<float>(this, 1f);
        Effect = new SyncRef<AudioEffect>(this);
    }

    protected override IAudioDspFilter CreateFilter() => new BlendFilter();

    protected override void ApplySettings(IAudioDspFilter filter) => ((BlendFilter)filter).Weight = BlendWeight.Value;

    // The world's audio manager binds the nested filter every frame, once it knows the nested effect is
    // free to run here.
    internal void BindNested(IAudioDspFilter? nested) => ((BlendFilter)Filter).Nested = nested;
}
