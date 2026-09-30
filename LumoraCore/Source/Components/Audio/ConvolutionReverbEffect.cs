// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Audio;
using Lumora.Core.Assets;
using Room = Lumora.Audio.ImpulseResponse;

namespace Lumora.Core.Components.Audio;

// A recorded room carried by a listener, the way ReverbEffect carries a made one: everything the listener
// hears plays through the impulse response in the clip, apart from sounds that skip effects. Mix runs from
// the dry signal alone (0) through both at full level (0.5) to the room alone (1), Level is decibels on the
// room only, PreDelay milliseconds before it starts.
//
// Once the clip has loaded it is prepared on a worker, converted to the mixer's rate and cut into blocks the
// size of the mixer's frame, and handed to the filter, which fades it in. Until then the room is silent and
// the dry signal passes; a new clip replaces the old room only once it is ready. Every machine prepares its
// own from the same clip, so nothing but the fields crosses the wire. The response is normalised to unit
// energy, so a room recorded hot and one recorded quiet come back at the level of what goes in. -xlinka
[ComponentCategory("Audio")]
public class ConvolutionReverbEffect : AudioEffect
{
    public readonly AssetRef<AudioClip> ImpulseResponse;

    [Range(ConvolutionSettings.MinMix, ConvolutionSettings.MaxMix, "0.00")]
    public readonly Sync<float> Mix;

    // On the room only; the dry signal is never touched by it.
    [Range(ConvolutionSettings.MinLevel, ConvolutionSettings.MaxLevel, "0.0")]
    public readonly Sync<float> Level;

    [Range(ConvolutionSettings.MinPreDelay, ConvolutionSettings.MaxPreDelay, "0")]
    public readonly Sync<float> PreDelay;

    private readonly object _prepareLock = new();
    private ConvolutionFilter? _convolution;
    private AudioClipData? _requested;
    private int _requestedRate;
    private int _requestedBlock;
    private int _generation;
    private volatile bool _preparing;
    private volatile string? _failure;

    public ConvolutionReverbEffect()
    {
        ImpulseResponse = new AssetRef<AudioClip>(this);
        Mix = new Sync<float>(this, ConvolutionSettings.DefaultMix);
        Level = new Sync<float>(this, ConvolutionSettings.DefaultLevel);
        PreDelay = new Sync<float>(this, ConvolutionSettings.DefaultPreDelay);
    }

    // The fields as stored; the filter clamps what it is given.
    public ConvolutionSettings Settings => new()
    {
        Mix = Mix.Value,
        Level = Level.Value,
        PreDelay = PreDelay.Value,
    };

    // The room handed to the filter: null until the clip has loaded and been prepared on this machine.
    public Room? PreparedResponse => _convolution?.Response;

    public bool IsPreparing => _preparing;

    // Why the last clip could not be made into a room, or null.
    public string? PrepareFailure => _failure;

    private static int MixerFrameSize => Engine.Current?.AudioSystem?.Simulator.FrameSize ?? AudioSimulator.DefaultFrameSize;

    protected override IAudioDspFilter CreateFilter()
    {
        _convolution = new ConvolutionFilter(MixerSampleRate, Settings);
        return _convolution;
    }

    protected override void ApplySettings(IAudioDspFilter filter)
    {
        var convolution = (ConvolutionFilter)filter;
        convolution.Settings = Settings;
        Refresh(convolution);
    }

    // The clip's own change reaches OnChanges; a new mixer frame size does not, so it is looked for here.
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (_convolution != null && !IsDestroyed)
            Refresh(_convolution);
    }

    public override void OnDestroy()
    {
        lock (_prepareLock)
            _generation++;
        base.OnDestroy();
    }

    // World thread. Starts a preparation when the clip, the mixer's rate or its frame has moved since the
    // last one; a preparation overtaken by a newer one is dropped when it finishes.
    private void Refresh(ConvolutionFilter filter)
    {
        var clip = ImpulseResponse.Asset;
        var data = clip != null && clip.IsReady ? clip.Data : null;
        int rate = MixerSampleRate;
        int block = Room.BlockSizeFor(MixerFrameSize);
        if (data == null)
        {
            if (ImpulseResponse.Target == null && _requested != null)
            {
                _requested = null;
                lock (_prepareLock)
                {
                    _generation++;
                    filter.SetResponse(null);
                }
                _preparing = false;
                _failure = null;
            }
            return;
        }
        if (ReferenceEquals(data, _requested) && rate == _requestedRate && block == _requestedBlock)
            return;

        _requested = data;
        _requestedRate = rate;
        _requestedBlock = block;
        int generation;
        lock (_prepareLock)
            generation = ++_generation;
        _preparing = true;
        _failure = null;
        Task.Run(() => Prepare(filter, data, rate, block, generation));
    }

    private void Prepare(ConvolutionFilter filter, AudioClipData data, int rate, int block, int generation)
    {
        try
        {
            var room = Room.FromClip(data, rate, block);
            lock (_prepareLock)
            {
                if (generation == _generation)
                    filter.SetResponse(room);
            }
        }
        catch (Exception ex)
        {
            bool current;
            lock (_prepareLock)
                current = generation == _generation;
            if (current)
            {
                _failure = ex.Message;
                Logging.Logger.Warn($"Convolution Reverb: the impulse response could not be prepared: {ex.Message}");
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _generation))
                _preparing = false;
        }
    }
}
