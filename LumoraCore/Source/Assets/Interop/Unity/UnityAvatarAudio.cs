// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Lumora.Audio;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Audio;
using Lumora.Core.Logging;
using GroupKind = Lumora.Core.Audio.AudioTypeGroup;
using SpaceKind = Lumora.Core.Audio.AudioDistanceSpace;

namespace Lumora.Core.Assets.Interop.Unity;

// The package's sounds: every AudioSource (class 82) becomes an AudioCuePlayer with an AudioOutput beside it on
// the same object, its clip copied into the local database. The avatar spatial audio script that usually sits
// with one decides how far it carries; without it the source's own distances and rolloff do.
//
// How the settings land:
// - m_Volume, m_Pitch, Loop, m_PlayOnAwake and m_Enabled go onto the cue player as they are: switched on it
//   plays from the top, switched off it stops, each machine on its own clock.
// - The spatial script's Gain is in decibels over a default of 10, which is where its sounds sit next to
//   voices, so 10 is our full volume and every decibel under it is taken off the output's volume.
// - Near and Far are the falloff: full inside Near, gone at Far. A Near of zero would be a point source
//   infinitely loud at its centre, so the fade starts a metre out (a quarter of a short Far).
// - With "use the source's volume curve" on, or no spatial script, the source's own MinDistance, MaxDistance
//   and rolloff mode are used; a custom curve is read for where it falls to half and fitted with the
//   inverse-distance falloff that halves there.
// - Distances are world metres, as the project wrote them, so a clip that animates them sets them as written.
// - EnableSpatialization off keeps the falloff and drops the panning.
// - Doppler is left off: a sound riding a tracked body would warble with every twitch of the tracking.
// -xlinka
public static partial class UnityAvatarBuilder
{
    private const int AudioSourceClass = 82;
    private const int ScriptClass = 114;

    // Gain the spatial script defaults to, which is our full volume.
    private const float DefaultSpatialGain = 10f;

    private sealed class AudioSet
    {
        public readonly List<(UnityNodeRef Node, UnityDocument Source)> Sources = new();
        public readonly List<(UnityNodeRef Node, UnityDocument Settings)> Spatial = new();
        // Package clip GUID -> where it is stored, and the file's name in the project.
        public readonly Dictionary<string, Uri> Stored = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<UnityImportCandidate, AudioSet> AudioSets = new();

    // Outputs whose distances follow the source's own MinDistance/MaxDistance, so curves that move those fields
    // move ours; true when the rolloff is a straight or inverse-distance one, false for a fitted custom curve,
    // which only answers to MaxDistance.
    private static readonly ConditionalWeakTable<AudioOutput, StrongBox<bool>> OwnDistances = new();

    // A document on one of the candidate's objects: an AudioSource or the spatial audio script beside one is
    // kept here, and true says it was.
    internal static bool CollectAudio(UnityImportCandidate candidate, UnityDocument d, UnityNodeRef node)
    {
        if (d.Stripped)
            return false;
        if (d.ClassId == AudioSourceClass)
        {
            AudioSets.GetOrCreateValue(candidate).Sources.Add((node, d));
            return true;
        }
        if (d.ClassId == ScriptClass && IsSpatialAudio(d))
        {
            AudioSets.GetOrCreateValue(candidate).Spatial.Add((node, d));
            return true;
        }
        return false;
    }

    private static bool IsSpatialAudio(UnityDocument d)
        => d["Gain"] != null && d["Far"] != null && d["Near"] != null && d["EnableSpatialization"] != null;

    // How many AudioSources the candidate carries.
    internal static int AudioSourceCount(UnityImportCandidate candidate)
        => AudioSets.TryGetValue(candidate, out var set) ? set.Sources.Count : 0;

    // Copies the package's playable clips into the local database, off the world thread, before anything is
    // built. All of them, not only the ones the sources name: an animator's sound states pick from clips no
    // source starts with. Nothing happens for a candidate with no sound source.
    internal static async Task StoreAudioAsync(UnityPackageArchive archive, UnityImportCandidate candidate, LocalDB? localDB)
    {
        if (!AudioSets.TryGetValue(candidate, out var set) || set.Sources.Count == 0)
            return;
        var wanted = archive.ByGuid.Values
            .Where(a => a.File != null && AudioDecoders.IsSupportedExtension(a.Extension) && !set.Stored.ContainsKey(a.Guid))
            .ToList();
        if (wanted.Count == 0)
            return;
        var stored = await Task.Run(async () =>
        {
            var done = new List<(string Guid, Uri Uri, string Name)>();
            foreach (var asset in wanted)
            {
                try
                {
                    string? uri = localDB != null ? await localDB.ImportLocalAssetAsync(asset.File!, LocalDB.ImportLocation.Copy).ConfigureAwait(false) : null;
                    done.Add((asset.Guid, new Uri(string.IsNullOrEmpty(uri) ? asset.File! : uri), asset.Name));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"UnityPackage: the clip {asset.Path} could not be stored: {ex.Message}");
                }
            }
            return done;
        }).ConfigureAwait(false);
        lock (set)
            foreach (var (guid, uri, name) in stored)
            {
                set.Stored[guid] = uri;
                set.Names[guid] = name;
            }
    }

    // For clip curves on an AudioSource: what m_Enabled switches.
    internal static Component? AudioEnabledTarget(Slot slot) => slot.GetComponent<AudioCuePlayer>();

    // For clip curves on an AudioSource: the field a float curve moves, or null where nothing here answers.
    internal static Sync<float>? AudioField(Slot slot, string attribute)
    {
        var cue = slot.GetComponent<AudioCuePlayer>();
        if (cue == null)
            return null;
        switch (attribute)
        {
            case "m_Volume":
                return cue.Volume;
            case "m_Pitch":
                return cue.Pitch;
            case "MinDistance":
            case "MaxDistance":
            {
                var output = slot.GetComponent<AudioOutput>();
                if (output == null || !OwnDistances.TryGetValue(output, out var straight))
                    return null;
                if (attribute == "MaxDistance")
                    return output.MaxDistance;
                return straight.Value ? output.MinDistance : null;
            }
            default:
                return null;
        }
    }

    private sealed partial class FeatureBuilder
    {
        private Slot? _audioClips;
        private readonly Dictionary<string, StaticAudioClip?> _clipProviders = new(StringComparer.OrdinalIgnoreCase);

        // Every sound source of the candidate, before the menu and the animator so their curves find them.
        public void Audio()
        {
            if (!AudioSets.TryGetValue(_candidate, out var set) || set.Sources.Count == 0)
                return;
            int built = 0, silent = 0;
            foreach (var (node, doc) in set.Sources)
            {
                try
                {
                    var cue = BuildSound(set, node, doc);
                    if (cue == null)
                        continue;
                    built++;
                    if (cue.Clip.Target == null)
                        silent++;
                }
                catch (Exception ex)
                {
                    Skip("broken sound sources");
                    Logger.Warn($"UnityPackage: the sound on '{node.Name}' could not be built: {ex.Message}");
                }
            }
            if (built > 0)
                _result.Notes.Add((built == 1 ? "1 sound source on its object" : $"{built} sound sources on their objects") + (silent > 0 ? $" ({silent} without a clip in the package)" : "") + ".");
        }

        // The package's clip by its GUID, made on first ask under the avatar's "Audio Clips"; null when the package
        // does not have it.
        public IAssetProvider<AudioClip>? AudioClipFor(string? guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;
            if (_clipProviders.TryGetValue(guid, out var known))
                return known is { IsDestroyed: false } ? known : null;
            StaticAudioClip? provider = null;
            if (AudioSets.TryGetValue(_candidate, out var set))
            {
                Uri? uri;
                string? name;
                lock (set)
                {
                    set.Stored.TryGetValue(guid, out uri);
                    set.Names.TryGetValue(guid, out name);
                }
                if (uri != null)
                {
                    _audioClips ??= _model.FindChild("Audio Clips", recursive: false) ?? _model.AddSlot("Audio Clips");
                    var holder = _audioClips.AddSlot(string.IsNullOrEmpty(name) ? guid : name);
                    provider = holder.AttachComponent<StaticAudioClip>();
                    provider.URL.Value = uri;
                }
            }
            _clipProviders[guid] = provider;
            return provider;
        }

        // The cue player built for the AudioSource at a path below the avatar's root, as clips and sound states
        // name it. A state from a controller merged in under some object names the path from that object, so a
        // path that does not resolve from the root is matched against the end of each source's own path.
        public AudioCuePlayer? AudioCueAt(string path)
        {
            if (_nodes.AtPath(path)?.GetComponent<AudioCuePlayer>() is { } cue)
                return cue;
            if (string.IsNullOrEmpty(path))
                return null;
            AudioCuePlayer? found = null;
            foreach (var candidate in _model.GetComponentsInChildren<AudioCuePlayer>())
            {
                if (!EndsWithPath(candidate.Slot, path))
                    continue;
                if (found != null)
                    return null;
                found = candidate;
            }
            return found;
        }

        // Whether the slot's names, read upward, end with the path's names.
        private static bool EndsWithPath(Slot slot, string path)
        {
            var names = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = slot;
            for (int i = names.Length - 1; i >= 0; i--)
            {
                if (current == null || current.SlotName.Value != names[i])
                    return false;
                current = current.Parent;
            }
            return true;
        }

        private AudioCuePlayer? BuildSound(AudioSet set, UnityNodeRef node, UnityDocument doc)
        {
            var slot = _nodes.Resolve(node);
            if (slot == null)
            {
                Skip("sounds on objects not in the package");
                return null;
            }

            var cue = slot.AttachComponent<AudioCuePlayer>();
            var output = slot.AttachComponent<AudioOutput>();
            output.Source.Target = cue;

            var clip = AudioClipFor(doc["m_audioClip"]?.Ref.Guid);
            if (clip != null)
                cue.Clip.Target = clip;
            cue.Volume.Value = System.Math.Clamp(doc["m_Volume"]?.Float(1f) ?? 1f, 0f, 1f);
            float pitch = doc["m_Pitch"]?.Float(1f) ?? 1f;
            cue.Pitch.Value = System.Math.Clamp(MathF.Abs(pitch), 0.05f, 4f);
            cue.Loop.Value = (doc["Loop"]?.Int() ?? 0) != 0;
            cue.PlayOnEnable.Value = (doc["m_PlayOnAwake"]?.Int(1) ?? 1) != 0;
            cue.Enabled.Value = (doc["m_Enabled"]?.Int(1) ?? 1) != 0;

            var spatial = set.Spatial.FirstOrDefault(s => ReferenceEquals(s.Node, node) || SameObject(s.Settings, doc)).Settings;

            float blend = System.Math.Clamp(FirstKey(doc["panLevelCustomCurve"]) ?? 1f, 0f, 1f);
            bool spatialize = blend > 0f && (spatial == null || (spatial["EnableSpatialization"]?.Int(1) ?? 1) != 0);
            output.SpatialBlend.Value = blend;
            output.Spatialize.Value = spatialize;
            output.DopplerLevel.Value = 0f;
            output.AudioTypeGroup.Value = GroupKind.SoundEffect;
            output.DistanceSpace.Value = SpaceKind.Global;
            int unityPriority = System.Math.Clamp(doc["Priority"]?.Int(128) ?? 128, 0, 256);
            output.Priority.Value = 256 - unityPriority;

            float gain = spatial?["Gain"]?.Float(DefaultSpatialGain) ?? DefaultSpatialGain;
            float volume = MathF.Pow(10f, (gain - DefaultSpatialGain) / 20f);
            if ((doc["Mute"]?.Int() ?? 0) != 0)
                volume = 0f;
            output.Volume.Value = System.Math.Clamp(volume, 0f, 1f);

            bool ownCurve = spatial == null || (spatial["UseAudioSourceVolumeCurve"]?.Int() ?? 0) != 0;
            if (ownCurve)
            {
                float min = MathF.Max(doc["MinDistance"]?.Float(1f) ?? 1f, 0f);
                float max = MathF.Max(doc["MaxDistance"]?.Float(500f) ?? 500f, 0.01f);
                int mode = doc["rolloffMode"]?.Int() ?? 0;
                bool straight = mode != 2;
                if (mode == 2)
                    min = HalfDistance(doc["rolloffCustomCurve"], max) * 0.5f;
                output.RolloffMode.Value = mode switch
                {
                    1 => AudioRolloffCurve.Linear,
                    2 => AudioRolloffCurve.LogarithmicFadeOff,
                    _ => AudioRolloffCurve.Logarithmic,
                };
                output.MinDistance.Value = MathF.Max(min, 0.001f);
                output.MaxDistance.Value = max;
                OwnDistances.AddOrUpdate(output, new StrongBox<bool>(straight));
            }
            else
            {
                float near = MathF.Max(spatial!["Near"]?.Float() ?? 0f, 0f);
                float far = MathF.Max(spatial["Far"]?.Float(40f) ?? 40f, 0.01f);
                float radius = MathF.Max(spatial["VolumetricRadius"]?.Float() ?? 0f, 0f);
                float min = near + radius;
                if (min <= 0f)
                    min = MathF.Min(1f, far * 0.25f);
                output.RolloffMode.Value = AudioRolloffCurve.LogarithmicFadeOff;
                output.MinDistance.Value = MathF.Min(min, far);
                output.MaxDistance.Value = far;
            }
            return cue;
        }

        // Both documents sit on the same game object.
        private static bool SameObject(UnityDocument a, UnityDocument b)
        {
            long ga = a["m_GameObject"]?.Ref.FileId ?? 0;
            return ga != 0 && ga == (b["m_GameObject"]?.Ref.FileId ?? 0);
        }

        // An AudioSource keeps its spatial blend as the first key of this curve.
        private static float? FirstKey(UnityNode? curve)
            => curve?["m_Curve"] is UnitySeq { Items.Count: > 0 } keys ? keys.Items[0]["value"]?.Float(1f) : null;

        // Where a custom rolloff curve (0..1 of MaxDistance) first falls to half, in metres.
        private static float HalfDistance(UnityNode? curve, float max)
        {
            if (curve?["m_Curve"] is not UnitySeq { Items.Count: > 0 } keys)
                return max * 0.5f;
            float lastTime = 0f, lastValue = 1f;
            bool first = true;
            foreach (var key in keys.Items)
            {
                float time = key["time"]?.Float() ?? 0f;
                float value = key["value"]?.Float(1f) ?? 1f;
                if (value <= 0.5f)
                {
                    if (first || value >= lastValue)
                        return MathF.Max(time * max, 0.01f);
                    float t = lastTime + (time - lastTime) * (lastValue - 0.5f) / (lastValue - value);
                    return MathF.Max(t * max, 0.01f);
                }
                lastTime = time;
                lastValue = value;
                first = false;
            }
            return max * 0.5f;
        }
    }
}
