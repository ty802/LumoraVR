// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Components.Assets;
using Lumora.Core.Localization;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// An animation clip opened in the curve editor, one track per animated property.
//
// A clip is an asset, so an edit is a new clip: the edited copy is encoded, stored in the local asset
// store, and the provider's URL is pointed at it, the same path an audio edit takes. That URL write is
// the only thing that replicates; every other machine fetches the new clip the way it fetched the old
// one, and every animator playing it rebinds by itself. One undo step per edit, undoing the URL.
//
// Edits land on a private copy first, which is what this machine draws from until the saved clip has
// loaded back. Saves queue behind each other on one machine and the last one wins, so a quick run of
// edits costs a couple of stores rather than one per drag frame. A save that finds the URL moved under
// it (an undo, someone else's edit) drops rather than writing over that change. -xlinka
public sealed class ClipCurveSource : ICurveSource
{
    private readonly AnimationProvider _provider;
    private readonly Component _owner;
    private readonly List<CurveTrackInfo> _tracks = new();
    private IClipTrackAdapter[] _adapters = Array.Empty<IClipTrackAdapter>();

    private AnimationClip? _seen;
    private AnimationClip? _described;
    private AnimationClip? _working;
    private Uri? _workingBase;
    private int _workingVersion;

    private bool _saving;
    private bool _saveAgain;
    private LocaleText _step;
    private Action<string>? _report;

    public ClipCurveSource(AnimationProvider provider, Component owner)
    {
        _provider = provider;
        _owner = owner;
    }

    private AnimationClip? AssetClip => _provider.IsDestroyed ? null : (_provider.Asset as AnimationAsset)?.Clip;

    private AnimationClip? Current
    {
        get
        {
            Refresh();
            return _working ?? AssetClip;
        }
    }

    public string Title
    {
        get
        {
            var clip = Current;
            if (clip != null && !string.IsNullOrEmpty(clip.Name))
                return clip.Name;
            return _provider.Slot?.Name.Value ?? "Clip";
        }
    }

    public bool IsAlive => !_provider.IsDestroyed;

    public bool IsReady => Current != null;

    public long Version
    {
        get
        {
            var clip = Current;
            return ((long)(clip == null ? 0 : RuntimeHelpers.GetHashCode(clip)) << 32) ^ (uint)_workingVersion;
        }
    }

    public bool CanEdit => IsReady && !_provider.URL.IsBlockedByDrive && _provider.URL.IsWriteAllowed();

    public string ReadOnlyReason
    {
        get
        {
            if (!IsReady)
                return "the clip is loading";
            if (_provider.URL.IsBlockedByDrive)
                return "the clip is driven by something else";
            if (!_provider.URL.IsWriteAllowed())
                return "you may not change this clip here";
            return "";
        }
    }

    public IReadOnlyList<CurveTrackInfo> Tracks
    {
        get
        {
            Refresh();
            return _tracks;
        }
    }

    public float Duration
    {
        get
        {
            var clip = Current;
            if (clip == null)
                return 1f;
            float duration = clip.Duration > 0f ? clip.Duration : clip.GetMaxTrackDuration();
            return duration > 0f ? duration : 1f;
        }
    }

    public bool FixedTimeRange => false;

    public float SnapStep
    {
        get
        {
            var clip = Current;
            return clip != null && clip.Framerate > 0f ? 1f / clip.Framerate : 1f / 30f;
        }
    }

    public List<CurveKeyRow> ReadKeys(int track)
    {
        var clip = Current;
        if (clip == null || track < 0 || track >= clip.TrackCount || track >= _adapters.Length)
            return new List<CurveKeyRow>();
        return _adapters[track].Read(clip[track]);
    }

    public ICurveSampler CreateSampler(int track, IReadOnlyList<CurveKeyRow> rows)
    {
        var clip = Current;
        if (clip == null || track < 0 || track >= clip.TrackCount || track >= _adapters.Length)
            return EmptySampler.Instance;
        var adapter = _adapters[track];
        return adapter.Sampler(adapter.Build(clip[track], rows));
    }

    public void Preview(int track, IReadOnlyList<CurveKeyRow> rows)
    {
        if (!EnsureWorking() || _working == null || track < 0 || track >= _working.TrackCount || track >= _adapters.Length)
            return;
        _working.ReplaceTrack(track, _adapters[track].Build(_working[track], rows));
        _workingVersion++;
    }

    public void Commit(int track, IReadOnlyList<CurveKeyRow> before, IReadOnlyList<CurveKeyRow> after, LocaleText step, Action<string> report)
    {
        if (!CanEdit)
        {
            report(ReadOnlyReason);
            return;
        }
        Preview(track, after);
        if (_working == null)
            return;
        _step = step;
        _report = report;
        if (_saving)
        {
            _saveAgain = true;
            return;
        }
        _saving = true;
        _owner.StartTask(SaveLoop);
    }

    public void Dispose()
    {
    }

    // The working copy goes once the clip it was saved into, or whatever replaced it, has loaded and no
    // save is still on its way.
    private void Refresh()
    {
        var asset = AssetClip;
        if (!ReferenceEquals(asset, _seen))
        {
            _seen = asset;
            if (asset != null && !_saving && !_saveAgain && _working != null)
            {
                _working = null;
                _workingBase = null;
                _workingVersion++;
            }
        }

        var current = _working ?? asset;
        if (!ReferenceEquals(current, _described))
            Describe(current);
    }

    private void Describe(AnimationClip? clip)
    {
        _described = clip;
        _tracks.Clear();
        if (clip == null)
        {
            _adapters = Array.Empty<IClipTrackAdapter>();
            return;
        }
        _adapters = new IClipTrackAdapter[clip.TrackCount];
        for (int i = 0; i < clip.TrackCount; i++)
        {
            var track = clip[i];
            _adapters[i] = ClipTrackAdapters.For(track.ValueType);
            _tracks.Add(_adapters[i].Describe(track));
        }
    }

    private bool EnsureWorking()
    {
        if (_working != null)
            return true;
        var asset = AssetClip;
        if (asset == null)
            return false;
        _working = asset.Clone();
        _workingBase = _provider.URL.Value;
        _workingVersion++;
        Describe(_working);
        return true;
    }

    private void Drop()
    {
        _working = null;
        _workingBase = null;
        _workingVersion++;
    }

    private async Task SaveLoop()
    {
        try
        {
            do
            {
                _saveAgain = false;
                var working = _working;
                var baseUrl = _workingBase;
                var step = _step;
                var report = _report ?? (_ => { });
                if (working == null || baseUrl == null || _provider.IsDestroyed)
                    break;
                if (!_provider.URL.IsWriteAllowed() || _provider.URL.IsBlockedByDrive)
                {
                    report(ReadOnlyReason);
                    Drop();
                    break;
                }
                var store = Engine.Current?.LocalDB;
                if (store == null)
                {
                    report("there is no asset store to save into");
                    break;
                }

                // Encoded here, on the world thread, so a drag landing mid-save cannot tear the copy.
                byte[] bytes = working.ToBytes();
                report("Saving...");

                await WorldContext.ToBackground();
                string? saved = await store.SaveAssetAsync(bytes, ".lanim").ConfigureAwait(false);
                await WorldContext.ToWorld();

                if (_provider.IsDestroyed)
                    break;
                if (string.IsNullOrEmpty(saved))
                {
                    report("the clip could not be stored");
                    break;
                }
                if (!SameUrl(_provider.URL.Value, baseUrl))
                {
                    report("the clip changed while saving; this edit was dropped");
                    Drop();
                    break;
                }
                if (!_provider.URL.IsWriteAllowed() || _provider.URL.IsBlockedByDrive)
                {
                    report(ReadOnlyReason);
                    Drop();
                    break;
                }

                var edited = new Uri(saved);
                _provider.URL.Value = edited;
                if (!SameUrl(_provider.URL.Value, edited))
                {
                    report("the change was refused");
                    Drop();
                    break;
                }
                InspectorUndo.Record(_owner, new FieldEditUndoBatch(_provider.URL, baseUrl, edited, step));
                _workingBase = edited;
                report("Saved");
            }
            while (_saveAgain);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"ClipCurveSource: saving the clip threw: {ex}");
            _report?.Invoke($"saving failed: {ex.Message}");
        }
        finally
        {
            _saving = false;
            _saveAgain = false;
        }
    }

    // The machine id in a local:// address is case-sensitive and Uri equality lowercases it.
    private static bool SameUrl(Uri? a, Uri? b)
        => ReferenceEquals(a, b) || (a != null && b != null && string.Equals(a.OriginalString, b.OriginalString, StringComparison.Ordinal));
}
