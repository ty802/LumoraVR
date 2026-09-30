// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Audio;

// A parametric equalizer: up to eight bands in order, each a cookbook filter of its own type, then a gain on the
// whole output in decibels. A fresh one starts with a flat low shelf, bell and high shelf to pull on. Bands past
// the eighth are ignored. Changing, adding or removing a band while sound plays glides or crossfades; nothing
// clicks.
//
// The bands are a synced list of structured entries, so each one replicates and saves field by field. A list does
// not raise a change pass on the component that holds it, so every band reports to this equalizer itself, and
// adding or removing one does the same. -xlinka
[ComponentCategory("Audio")]
public class EqualizerEffect : AudioEffect
{
    public readonly SyncList<EqualizerBand> Bands;

    [Range(EqualizerSettings.MinOutputGain, EqualizerSettings.MaxOutputGain, "0.0")]
    public readonly Sync<float> OutputGain;

    public EqualizerEffect()
    {
        Bands = new SyncList<EqualizerBand>();
        OutputGain = new Sync<float>(this, EqualizerSettings.DefaultOutputGain);
        Bands.ElementsAdded += OnBandsAdded;
        Bands.ElementsRemoving += OnBandsRemoving;
        Bands.ElementsRemoved += OnBandsRemoved;
    }

    // The fields as stored, bands one to eight in order; the filter clamps what it is given.
    public EqualizerSettings Settings
    {
        get
        {
            var settings = new EqualizerSettings { OutputGain = OutputGain.Value };
            int count = Bands.Count;
            for (int i = 0; i < count && settings.BandCount < EqualizerSettings.MaxBands; i++)
            {
                var band = Bands[i];
                if (band != null)
                    settings.Add(band.Settings);
            }
            return settings;
        }
    }

    public EqualizerBand AddBand(BiquadFilterType type, float frequency, float gain, float q)
    {
        var band = Bands.Add();
        band.Type.Value = type;
        band.Frequency.Value = frequency;
        band.Gain.Value = gain;
        band.Q.Value = q;
        band.Enabled.Value = true;
        return band;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        AddBand(BiquadFilterType.LowShelf, 100f, 0f, BiquadSettings.DefaultQ);
        AddBand(BiquadFilterType.Peaking, 1000f, 0f, 1f);
        AddBand(BiquadFilterType.HighShelf, 8000f, 0f, BiquadSettings.DefaultQ);
    }

    protected override IAudioDspFilter CreateFilter() => new EqualizerFilter(MixerSampleRate, Settings);

    protected override void ApplySettings(IAudioDspFilter filter) => ((EqualizerFilter)filter).Settings = Settings;

    internal void BandChanged()
    {
        if (!IsDestroyed)
            MarkChangeDirty();
    }

    private void OnBandsAdded(SyncElementList<EqualizerBand> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var band = list[i];
            if (band != null)
                band.Owner = this;
        }
        BandChanged();
    }

    private void OnBandsRemoving(SyncElementList<EqualizerBand> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var band = list[i];
            if (band != null)
                band.Owner = null;
        }
    }

    private void OnBandsRemoved(SyncElementList<EqualizerBand> list, int index, int count) => BandChanged();
}
