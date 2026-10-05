// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lumora.Audio;
using Lumora.Core.Assets;
using Lumora.Core.Localization;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Assets;

// the asset gathers and decodes itself through AssetManager; this only resolves and requests the URL.
// pointing at a local:// URI is what makes a sound travel: content-addressed in the local DB,
// replicated with the component, and gathered by hash on join
//
// The edits rewrite the clip itself. One decodes the whole clip, works on it off the world thread, stores
// the result as a WAV in the local asset store the way an import does, and points URL at it back on the
// world thread. That URL write is all that replicates: every other machine fetches the new clip through
// the same transfer that brought it the old one. Only a user the permission gate allows to write URL may
// start an edit, and the gate is asked again before the result lands. The URL change undoes like any other
// edit of the field, one undo per edit.
//
// An edit applies only to the clip it started from. If URL moved while it ran (an undo, another edit, a
// hand edit), the result is dropped rather than written over that change. Edits started on one machine
// queue behind each other, so two cannot race; while one runs, Editor names whose machine is working and
// other machines are refused until it is done. -xlinka
[ComponentCategory("Assets")]
public sealed class StaticAudioClip : StaticAssetProvider<AudioClip>
{
    private const int MaxQueuedEdits = 8;
    // Long enough for a ten minute clip over a slow link; a transfer that dies answers sooner than this.
    private static readonly TimeSpan GatherTimeout = TimeSpan.FromMinutes(10);

    // Empty while nothing runs, the running edit while one does, why the last one failed after it did.
    [NonPersistent]
    [DontCopy]
    public readonly Sync<string> EditStatus;

    // The user whose machine is editing this clip; null when nobody is.
    [NonPersistent]
    [DontCopy]
    [HideInInspector]
    public readonly SyncRef<User> Editor;

    private readonly Queue<AudioClipEdit> _queued = new();
    private bool _editing;

    public StaticAudioClip()
    {
        EditStatus = new Sync<string>(this, "");
        Editor = new SyncRef<User>(this);
    }

    // An edit running or queued on this machine, or running on another.
    public bool IsEditing => _editing || OtherEditor != null;

    [SyncMethod]
    public void NormalizePeak(float peakDb = -1f) => Edit(AudioClipEdit.NormalizePeak(peakDb));

    [SyncMethod]
    public void NormalizeRms(float rmsDb = -20f) => Edit(AudioClipEdit.NormalizeRms(rmsDb));

    [SyncMethod]
    public void AdjustVolume(float decibels = -3f) => Edit(AudioClipEdit.AdjustVolume(decibels));

    [SyncMethod]
    public void TrimSilence(float thresholdDb = -50f, float minimumSilence = 0.1f, ClipTrimEnds ends = ClipTrimEnds.Both, float pad = 0.05f)
        => Edit(AudioClipEdit.TrimSilence(thresholdDb, minimumSilence, ends, pad));

    [SyncMethod]
    public void TrimStart(float seconds = 0.5f) => Edit(AudioClipEdit.TrimStart(seconds));

    [SyncMethod]
    public void TrimEnd(float seconds = 0.5f) => Edit(AudioClipEdit.TrimEnd(seconds));

    [SyncMethod]
    public void FadeIn(float seconds = 0.5f, ClipFadeCurve curve = ClipFadeCurve.EqualPower) => Edit(AudioClipEdit.FadeIn(seconds, curve));

    [SyncMethod]
    public void FadeOut(float seconds = 0.5f, ClipFadeCurve curve = ClipFadeCurve.EqualPower) => Edit(AudioClipEdit.FadeOut(seconds, curve));

    [SyncMethod]
    public void MakeLoopable(float crossfade = 0.5f) => Edit(AudioClipEdit.MakeLoopable(crossfade));

    [SyncMethod]
    public void ToMono() => Edit(AudioClipEdit.ToMono());

    [SyncMethod]
    public void ExtractMid() => Edit(AudioClipEdit.ExtractMid());

    [SyncMethod]
    public void ExtractSides() => Edit(AudioClipEdit.ExtractSides());

    [SyncMethod]
    public void SwapChannels() => Edit(AudioClipEdit.SwapChannels());

    [SyncMethod]
    public void Reverse() => Edit(AudioClipEdit.Reverse());

    [SyncMethod]
    public void Resample(int sampleRate = 48000) => Edit(AudioClipEdit.Resample(sampleRate));

    [SyncMethod]
    public void Denoise() => Edit(AudioClipEdit.Denoise());

    [SyncMethod]
    public void ExportWav(WavSampleFormat format = WavSampleFormat.Pcm16) => Edit(AudioClipEdit.ExportWav(format));

    // Queues the edit on this machine. False when it was refused, which is logged; a refusal for a user
    // the gate turns away writes nothing at all, not even a status line.
    public bool Edit(AudioClipEdit edit)
    {
        if (edit == null || IsDestroyed || World == null)
            return false;
        string name = edit.Name.Resolve();

        if (!URL.IsWriteAllowed())
        {
            LumoraLogger.Warn($"StaticAudioClip: {name} refused, this user may not change the clip");
            return false;
        }

        // Another machine's edit is running, and a status line written here would bury its own.
        var other = OtherEditor;
        if (other != null)
        {
            LumoraLogger.Warn($"StaticAudioClip: {name} refused, {other.UserName.Value} is already editing this clip");
            return false;
        }

        LocaleText? refusal = edit.Invalid;
        if (refusal == null && URL.IsBlockedByDrive)
            refusal = AudioClipEdit.Reasons.Driven;
        if (refusal == null && URL.Value == null)
            refusal = AudioClipEdit.Reasons.NoClip;
        if (refusal is { } reason)
        {
            Report(edit, reason);
            return false;
        }

        if (_queued.Count >= MaxQueuedEdits)
        {
            LumoraLogger.Warn($"StaticAudioClip: {name} refused, {MaxQueuedEdits} edits are already waiting on this clip");
            return false;
        }

        _queued.Enqueue(edit);
        if (!_editing)
            StartNext();
        return true;
    }

    private User? OtherEditor
    {
        get
        {
            var user = Editor.Target;
            return user != null && !user.IsDestroyed && user != World?.LocalUser ? user : null;
        }
    }

    private void StartNext()
    {
        if (_queued.Count == 0)
        {
            _editing = false;
            if (Editor.Target != null && Editor.Target == World?.LocalUser)
                Editor.Target = null!;
            return;
        }

        var edit = _queued.Dequeue();
        _editing = true;
        StartTask(() => Run(edit));
    }

    private async Task Run(AudioClipEdit edit)
    {
        LocaleText? failure;
        try
        {
            failure = await Apply(edit);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"StaticAudioClip: {edit.Name.Resolve()} threw: {ex}");
            failure = AudioClipEdit.Reasons.Error(ex.Message);
        }

        try
        {
            await WorldContext.ToWorld();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (failure is { } reason)
            Report(edit, reason);
        else
            EditStatus.Value = "";
        StartNext();
    }

    // Null when the edit landed, otherwise why it did not.
    private async Task<LocaleText?> Apply(AudioClipEdit edit)
    {
        if (!URL.IsWriteAllowed())
            return AudioClipEdit.Reasons.Denied;
        var source = URL.Value;
        if (source == null)
            return AudioClipEdit.Reasons.NoClip;
        var manager = Engine.Current?.AssetManager;
        var store = Engine.Current?.LocalDB;
        if (manager == null || store == null)
            return AudioClipEdit.Reasons.NoStore;
        var resolved = ProcessURL(source);
        if (resolved == null)
            return AudioClipEdit.Reasons.NoClip;

        EditStatus.Value = AudioClipEdit.Status.Running(edit.Name).Resolve();
        Editor.Target = World.LocalUser;

        byte[] bytes;
        try
        {
            bytes = await manager.RequestGather(resolved).WaitAsync(GatherTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return AudioClipEdit.Reasons.Timeout;
        }
        await WorldContext.ToBackground();

        if (bytes == null || bytes.Length == 0)
            return AudioClipEdit.Reasons.Unreadable("no data arrived");
        if (!AudioDecoders.TryDecode(new ReadOnlyMemory<byte>(bytes), AudioClip.ExtensionOf(resolved), out var clip, out var error) || clip == null)
            return AudioClipEdit.Reasons.Unreadable(error ?? "");
        if (edit.CheckSource(clip) is { } unsuitable)
            return unsuitable;

        var result = edit.Apply(clip);
        if (result.FrameCount == 0)
            return AudioClipEdit.Reasons.Empty;
        if (result.Samples.Length > AudioDecoders.MaxDecodedSamples)
            return AudioClipEdit.Reasons.TooLong;

        // The result keeps the source's sample format, 16-bit for a compressed source, unless it runs past
        // full scale, where an integer format would clip it and float keeps it whole.
        bool sourceIsWav = AudioDecoders.TryGetWavFormat(bytes, out int bits, out bool isFloat);
        var sourceFormat = !sourceIsWav ? WavSampleFormat.Pcm16
            : isFloat || bits == 32 ? WavSampleFormat.Float32
            : bits == 24 ? WavSampleFormat.Pcm24
            : WavSampleFormat.Pcm16;
        var format = edit.Format ?? sourceFormat;
        if (edit.Format == null && format != WavSampleFormat.Float32 && ClipProcessing.Peak(result.Samples) > 1f)
            format = WavSampleFormat.Float32;
        if (SameSamples(clip, result) && (edit.Format == null || (sourceIsWav && format == sourceFormat)))
            return AudioClipEdit.Reasons.NoChange;
        var wav = WavEncoder.Encode(result.Samples, result.Channels, result.SampleRate, format);

        await WorldContext.ToWorld();
        if (!SameUrl(URL.Value, source))
            return AudioClipEdit.Reasons.Changed;
        if (!URL.IsWriteAllowed())
            return AudioClipEdit.Reasons.Denied;

        await WorldContext.ToBackground();
        string? saved = await store.SaveAssetAsync(wav, ".wav").ConfigureAwait(false);
        await WorldContext.ToWorld();
        if (string.IsNullOrEmpty(saved))
            return AudioClipEdit.Reasons.SaveFailed;
        if (!SameUrl(URL.Value, source))
            return AudioClipEdit.Reasons.Changed;
        if (!URL.IsWriteAllowed())
            return AudioClipEdit.Reasons.Denied;

        var edited = new Uri(saved);
        URL.Value = edited;
        if (!SameUrl(URL.Value, edited))
            return AudioClipEdit.Reasons.Denied;
        InspectorUndo.Record(this, new FieldEditUndoBatch(URL, source, edited, edit.Name));
        LumoraLogger.Debug($"StaticAudioClip: {edit.Name.Resolve()} done, {saved} ({result.Channels} ch, {result.SampleRate} Hz, {result.Duration:0.###} s, {format})");
        return null;
    }

    private void Report(AudioClipEdit edit, LocaleText reason)
    {
        LumoraLogger.Warn($"StaticAudioClip: {edit.Name.Resolve()} failed, {reason.Resolve()}");
        EditStatus.Value = AudioClipEdit.Status.Failed(edit.Name, reason).Resolve();
    }

    // The machine id in a local:// address is case-sensitive and Uri equality lowercases it.
    private static bool SameUrl(Uri? a, Uri? b)
        => ReferenceEquals(a, b) || (a != null && b != null && string.Equals(a.OriginalString, b.OriginalString, StringComparison.Ordinal));

    private static bool SameSamples(AudioClipData a, AudioClipData b)
    {
        if (ReferenceEquals(a, b))
            return true;
        return a.Channels == b.Channels && a.SampleRate == b.SampleRate
            && MemoryMarshal.Cast<float, int>(a.Samples.AsSpan()).SequenceEqual(MemoryMarshal.Cast<float, int>(b.Samples.AsSpan()));
    }
}
