// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Localization;

namespace Lumora.Core.Assets;

// One offline edit of a whole clip: its name, what it does to the samples, and the WAV sample format its
// result is written in (null keeps the source's). Built on the world thread from the values a button
// carried and applied on a pool thread to a decoded copy nothing else holds. A value out of range never
// reaches the samples: the edit carries the refusal instead, and so does a clip it cannot apply to.
public sealed class AudioClipEdit
{
    public const float MaxEditSeconds = 600f;

    private readonly Func<AudioClipData, AudioClipData>? _apply;
    private readonly Func<AudioClipData, LocaleText?>? _check;

    public LocaleText Name { get; }

    public WavSampleFormat? Format { get; }

    public LocaleText? Invalid { get; }

    private AudioClipEdit(LocaleText name, Func<AudioClipData, AudioClipData>? apply, Func<AudioClipData, LocaleText?>? check = null,
        WavSampleFormat? format = null, LocaleText? invalid = null)
    {
        Name = name;
        _apply = apply;
        _check = check;
        Format = format;
        Invalid = invalid;
    }

    // Null when the edit can run on this clip, otherwise why it cannot.
    public LocaleText? CheckSource(AudioClipData clip) => _check?.Invoke(clip);

    public AudioClipData Apply(AudioClipData clip)
    {
        if (_apply == null)
            throw new InvalidOperationException("an edit carrying a refusal has nothing to apply");
        return _apply(clip);
    }

    // GAIN

    public static AudioClipEdit NormalizePeak(float peakDb)
        => Refuse(Names.NormalizePeak, nameof(peakDb), peakDb, -80f, 0f)
            ?? new AudioClipEdit(Names.NormalizePeak,
                clip => Same(clip, ClipProcessing.NormalizePeak(clip.Samples, peakDb)),
                clip => Unless(ClipProcessing.Peak(clip.Samples) > 0f, Reasons.Silent));

    public static AudioClipEdit NormalizeRms(float rmsDb)
        => Refuse(Names.NormalizeRms, nameof(rmsDb), rmsDb, -80f, 0f)
            ?? new AudioClipEdit(Names.NormalizeRms,
                clip => Same(clip, ClipProcessing.NormalizeRms(clip.Samples, rmsDb)),
                clip => Unless(ClipProcessing.Rms(clip.Samples) > 0.0, Reasons.Silent));

    public static AudioClipEdit AdjustVolume(float decibels)
        => Refuse(Names.AdjustVolume, nameof(decibels), decibels, -60f, 30f)
            ?? (decibels == 0f ? Refused(Names.AdjustVolume, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.AdjustVolume, clip => Same(clip, ClipProcessing.Gain(clip.Samples, decibels)));

    // TRIM

    public static AudioClipEdit TrimSilence(float thresholdDb, float minimumSilence, ClipTrimEnds ends, float pad)
        => Refuse(Names.TrimSilence, nameof(thresholdDb), thresholdDb, -120f, 0f)
            ?? Refuse(Names.TrimSilence, nameof(minimumSilence), minimumSilence, 0f, 60f)
            ?? Refuse(Names.TrimSilence, nameof(pad), pad, 0f, 10f)
            ?? (Enum.IsDefined(ends) ? null : Refused(Names.TrimSilence, Reasons.NotAChoice(nameof(ends))))
            ?? new AudioClipEdit(Names.TrimSilence, clip => Same(clip,
                ClipProcessing.TrimSilence(clip.Samples, clip.Channels, clip.SampleRate, thresholdDb, minimumSilence, ends, pad)));

    public static AudioClipEdit TrimStart(float seconds)
        => Refuse(Names.TrimStart, nameof(seconds), seconds, 0f, MaxEditSeconds)
            ?? (seconds == 0f ? Refused(Names.TrimStart, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.TrimStart, clip => Same(clip, ClipProcessing.Trim(clip.Samples, clip.Channels, clip.SampleRate, seconds, 0.0)));

    public static AudioClipEdit TrimEnd(float seconds)
        => Refuse(Names.TrimEnd, nameof(seconds), seconds, 0f, MaxEditSeconds)
            ?? (seconds == 0f ? Refused(Names.TrimEnd, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.TrimEnd, clip => Same(clip, ClipProcessing.Trim(clip.Samples, clip.Channels, clip.SampleRate, 0.0, seconds)));

    // FADES AND LOOPS

    public static AudioClipEdit FadeIn(float seconds, ClipFadeCurve curve)
        => Refuse(Names.FadeIn, nameof(seconds), seconds, 0f, MaxEditSeconds)
            ?? (Enum.IsDefined(curve) ? null : Refused(Names.FadeIn, Reasons.NotAChoice(nameof(curve))))
            ?? (seconds == 0f ? Refused(Names.FadeIn, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.FadeIn, clip => Same(clip, ClipProcessing.FadeIn(clip.Samples, clip.Channels, clip.SampleRate, seconds, curve)));

    public static AudioClipEdit FadeOut(float seconds, ClipFadeCurve curve)
        => Refuse(Names.FadeOut, nameof(seconds), seconds, 0f, MaxEditSeconds)
            ?? (Enum.IsDefined(curve) ? null : Refused(Names.FadeOut, Reasons.NotAChoice(nameof(curve))))
            ?? (seconds == 0f ? Refused(Names.FadeOut, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.FadeOut, clip => Same(clip, ClipProcessing.FadeOut(clip.Samples, clip.Channels, clip.SampleRate, seconds, curve)));

    // Equal power, because the two ends of a loop are rarely the same material and a linear crossfade dips
    // between unrelated sounds.
    public static AudioClipEdit MakeLoopable(float crossfade)
        => Refuse(Names.MakeLoopable, nameof(crossfade), crossfade, 0f, 60f)
            ?? (crossfade == 0f ? Refused(Names.MakeLoopable, Reasons.NoChange) : null)
            ?? new AudioClipEdit(Names.MakeLoopable,
                clip => Same(clip, ClipProcessing.MakeLoopable(clip.Samples, clip.Channels, clip.SampleRate, crossfade, ClipFadeCurve.EqualPower)),
                clip => Unless(clip.FrameCount >= 2 * ClipProcessing.ToFrames(crossfade, clip.SampleRate), Reasons.TooShortToLoop));

    // CHANNELS

    public static AudioClipEdit ToMono()
        => new(Names.ToMono, clip => new AudioClipData(ClipProcessing.ToMono(clip.Samples, clip.Channels), 1, clip.SampleRate));

    public static AudioClipEdit ExtractMid()
        => new(Names.ExtractMid, clip => Same(clip, ClipProcessing.ExtractMid(clip.Samples, clip.Channels)), NeedsPair);

    public static AudioClipEdit ExtractSides()
        => new(Names.ExtractSides, clip => Same(clip, ClipProcessing.ExtractSides(clip.Samples, clip.Channels)), NeedsPair);

    public static AudioClipEdit SwapChannels()
        => new(Names.SwapChannels, clip => Same(clip, ClipProcessing.SwapChannels(clip.Samples, clip.Channels)), NeedsPair);

    public static AudioClipEdit Reverse()
        => new(Names.Reverse, clip => Same(clip, ClipProcessing.Reverse(clip.Samples, clip.Channels)));

    // RATE, NOISE, FORMAT

    // Refused before any work when the result would not fit a clip: going up in rate multiplies the size.
    public static AudioClipEdit Resample(int sampleRate)
        => Refuse(Names.Resample, nameof(sampleRate), sampleRate, 8000f, 192000f)
            ?? new AudioClipEdit(Names.Resample,
                clip => new AudioClipData(ClipResampler.Resample(clip.Samples, clip.Channels, clip.SampleRate, sampleRate), clip.Channels, sampleRate),
                clip =>
                {
                    if (clip.SampleRate == sampleRate)
                        return Reasons.NoChange;
                    long samples = (long)ClipResampler.OutputFrames(clip.FrameCount, clip.SampleRate, sampleRate) * clip.Channels;
                    return Unless(samples <= AudioDecoders.MaxDecodedSamples, Reasons.TooLong);
                });

    public static AudioClipEdit Denoise()
        => new(Names.Denoise, clip => Same(clip, ClipDenoiser.Denoise(clip.Samples, clip.Channels, clip.SampleRate)));

    public static AudioClipEdit ExportWav(WavSampleFormat format)
        => !Enum.IsDefined(format)
            ? Refused(Names.ExportWav, Reasons.NotAChoice(nameof(format)))
            : new AudioClipEdit(Names.ExportWav, clip => clip, format: format);

    // HELPERS

    private static readonly Func<AudioClipData, LocaleText?> NeedsPair = clip => Unless(clip.Channels >= 2, Reasons.Mono);

    private static AudioClipData Same(AudioClipData clip, float[] samples) => new(samples, clip.Channels, clip.SampleRate);

    // Not a conditional expression: LocaleText converts from string, so `fine ? null : reason` types as
    // LocaleText and its null arrives as an empty refusal instead of no refusal.
    private static LocaleText? Unless(bool fine, LocaleText reason)
    {
        if (fine)
            return null;
        return reason;
    }

    private static AudioClipEdit Refused(LocaleText name, LocaleText reason) => new(name, null, invalid: reason);

    // NaN fails every comparison, so it lands here too.
    private static AudioClipEdit? Refuse(LocaleText name, string parameter, float value, float min, float max)
        => value >= min && value <= max ? null : Refused(name, Reasons.OutOfRange(parameter, min, max));

    public static class Names
    {
        public static readonly LocaleText NormalizePeak = "AudioClip.Edit.NormalizePeak".AsLocale("Normalize peak");
        public static readonly LocaleText NormalizeRms = "AudioClip.Edit.NormalizeRms".AsLocale("Normalize RMS");
        public static readonly LocaleText AdjustVolume = "AudioClip.Edit.AdjustVolume".AsLocale("Adjust volume");
        public static readonly LocaleText TrimSilence = "AudioClip.Edit.TrimSilence".AsLocale("Trim silence");
        public static readonly LocaleText TrimStart = "AudioClip.Edit.TrimStart".AsLocale("Trim start");
        public static readonly LocaleText TrimEnd = "AudioClip.Edit.TrimEnd".AsLocale("Trim end");
        public static readonly LocaleText FadeIn = "AudioClip.Edit.FadeIn".AsLocale("Fade in");
        public static readonly LocaleText FadeOut = "AudioClip.Edit.FadeOut".AsLocale("Fade out");
        public static readonly LocaleText MakeLoopable = "AudioClip.Edit.MakeLoopable".AsLocale("Make loopable");
        public static readonly LocaleText ToMono = "AudioClip.Edit.ToMono".AsLocale("To mono");
        public static readonly LocaleText ExtractMid = "AudioClip.Edit.ExtractMid".AsLocale("Extract mid");
        public static readonly LocaleText ExtractSides = "AudioClip.Edit.ExtractSides".AsLocale("Extract sides");
        public static readonly LocaleText SwapChannels = "AudioClip.Edit.SwapChannels".AsLocale("Swap channels");
        public static readonly LocaleText Reverse = "AudioClip.Edit.Reverse".AsLocale("Reverse");
        public static readonly LocaleText Resample = "AudioClip.Edit.Resample".AsLocale("Resample");
        public static readonly LocaleText Denoise = "AudioClip.Edit.Denoise".AsLocale("Denoise");
        public static readonly LocaleText ExportWav = "AudioClip.Edit.ExportWav".AsLocale("Export WAV");
    }

    public static class Status
    {
        public static LocaleText Running(LocaleText edit)
            => "AudioClip.Edit.Running".AsLocale("{0}...", edit.Resolve());

        public static LocaleText Failed(LocaleText edit, LocaleText reason)
            => "AudioClip.Edit.Failed".AsLocale("{0} failed: {1}", edit.Resolve(), reason.Resolve());
    }

    public static class Reasons
    {
        public static LocaleText OutOfRange(string parameter, float min, float max)
            => "AudioClip.Edit.Reason.OutOfRange".AsLocale("{0} has to be between {1} and {2}", parameter, min, max);

        public static readonly LocaleText NoChange = "AudioClip.Edit.Reason.NoChange".AsLocale("that would not change the clip");
        public static readonly LocaleText Silent = "AudioClip.Edit.Reason.Silent".AsLocale("the clip is silent");
        public static readonly LocaleText Mono = "AudioClip.Edit.Reason.Mono".AsLocale("the clip has one channel and this needs two");
        public static readonly LocaleText TooShortToLoop = "AudioClip.Edit.Reason.TooShortToLoop".AsLocale("the clip is shorter than two crossfades");
        public static readonly LocaleText Empty = "AudioClip.Edit.Reason.Empty".AsLocale("nothing of the clip would be left");
        public static readonly LocaleText TooLong = "AudioClip.Edit.Reason.TooLong".AsLocale("the result would run past the ten minute clip limit");
        public static readonly LocaleText NoClip = "AudioClip.Edit.Reason.NoClip".AsLocale("there is no clip to edit");
        public static readonly LocaleText NoStore = "AudioClip.Edit.Reason.NoStore".AsLocale("there is no asset store to keep the result in");
        public static readonly LocaleText SaveFailed = "AudioClip.Edit.Reason.SaveFailed".AsLocale("the result could not be saved");
        public static readonly LocaleText Changed = "AudioClip.Edit.Reason.Changed".AsLocale("the clip changed while the edit ran, so the result was dropped");
        public static readonly LocaleText Denied = "AudioClip.Edit.Reason.Denied".AsLocale("you are not allowed to change this clip");
        public static readonly LocaleText Driven = "AudioClip.Edit.Reason.Driven".AsLocale("something else drives this clip's URL");
        public static readonly LocaleText Timeout = "AudioClip.Edit.Reason.Timeout".AsLocale("the clip did not arrive in time");

        public static LocaleText NotAChoice(string parameter)
            => "AudioClip.Edit.Reason.NotAChoice".AsLocale("{0} is not one of its choices", parameter);

        public static LocaleText Unreadable(string detail)
            => "AudioClip.Edit.Reason.Unreadable".AsLocale("the clip could not be read: {0}", detail);

        public static LocaleText Error(string detail)
            => "AudioClip.Edit.Reason.Error".AsLocale("the edit failed: {0}", detail);
    }
}
