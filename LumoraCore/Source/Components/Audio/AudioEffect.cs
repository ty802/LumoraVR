// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// A stage in an effect chain: a listener's (AudioListener.Effects), run on everything that ear hears, or an
// output's (AudioOutput.Effects), run on that one sound before it is placed in the room.
//
// The component is what replicates and saves. The filter it owns is what the mixer runs, and it never
// leaves this machine: every peer builds its own from the same fields, so two people in one room hear the
// same reverb without a single sample crossing the wire. A filter holds one room's worth of state, so an
// effect runs in one chain at a time; when two chains point at the same effect, the world's audio manager
// gives it to the ear at the machine ahead of the other ears, and to any ear ahead of an output, and the
// rest go without. -xlinka
[ComponentCategory("Audio")]
public abstract class AudioEffect : Component
{
    private IAudioDspFilter? _filter;

    // Made on demand from the fields as they stand, so it never starts from defaults it was not given.
    public IAudioDspFilter Filter
    {
        get
        {
            if (_filter == null)
            {
                _filter = CreateFilter();
                ApplySettings(_filter);
            }
            return _filter;
        }
    }

    // Anything else drops out of every chain on the next frame.
    public bool IsEffectActive => !IsDestroyed && Enabled.Value && Slot != null && Slot.IsActive;

    protected abstract IAudioDspFilter CreateFilter();

    // Copies the fields into the filter, on the world thread; the filter glides to them on its own.
    protected abstract void ApplySettings(IAudioDspFilter filter);

    protected static int MixerSampleRate
        => Engine.Current?.AudioSystem?.Simulator.SampleRate ?? AudioSimulator.DefaultSampleRate;

    public override void OnChanges()
    {
        base.OnChanges();
        if (_filter != null && !IsDestroyed)
            ApplySettings(_filter);
    }
}
