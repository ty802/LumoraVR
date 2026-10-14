// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Assets.Interop.Unity;

// A clip a VRCFury toggle, gesture or upload action plays names its objects from the object the feature sits
// on, not from the avatar's root: a warmers prefab's "turn the arm warmers on" clip says "Warmers", meaning the
// mesh inside the prefab. Each path is taken from the nearest object, starting at the feature's own and going
// up to the root, that has it, and the clip is read with those paths. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        // The feature whose actions are being built.
        private UnityNodeRef? _actionRoot;
        private readonly Dictionary<(string Guid, string From), UnityAnimationClip> _rebasedClips = new();

        private UnityAnimationClip? ActionClip(UnityNode? wrapped)
        {
            var guid = UnityVrcFury.Guid(wrapped);
            if (guid == null || _clipFor?.Invoke(guid) is not { } clip)
                return null;
            if (_actionRoot == null || UnityFullController.PathOf(_actionRoot) is not { Length: > 0 } from)
                return clip;
            if (_rebasedClips.TryGetValue((guid, from), out var known))
                return known;
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            string Near(string path)
            {
                if (paths.TryGetValue(path, out var found))
                    return found;
                found = path.Length == 0 ? from : path;
                for (var at = from; ; at = at[..at.LastIndexOf('/')])
                {
                    var whole = path.Length == 0 ? at : at + "/" + path;
                    if (_nodes.AtPath(whole) != null)
                    {
                        found = whole;
                        break;
                    }
                    if (!at.Contains('/'))
                        break;
                }
                paths[path] = found;
                return found;
            }
            var rebased = new UnityAnimationClip { Name = clip.Name, Length = clip.Length, Loop = clip.Loop, AdditiveReferenceTime = clip.AdditiveReferenceTime };
            foreach (var c in clip.Floats)
            {
                var copy = new UnityAnimationClip.FloatCurve { Path = Near(c.Path), ClassId = c.ClassId, Script = c.Script, Attribute = c.Attribute };
                copy.Keys.AddRange(c.Keys);
                rebased.Floats.Add(copy);
            }
            foreach (var (source, into) in new[] { (clip.Positions, rebased.Positions), (clip.Scales, rebased.Scales), (clip.Eulers, rebased.Eulers) })
                foreach (var c in source)
                {
                    var copy = new UnityAnimationClip.VectorCurve { Path = Near(c.Path), ClassId = c.ClassId, Script = c.Script };
                    copy.Keys.AddRange(c.Keys);
                    into.Add(copy);
                }
            foreach (var c in clip.Rotations)
            {
                var copy = new UnityAnimationClip.RotationCurve { Path = Near(c.Path), ClassId = c.ClassId, Script = c.Script };
                copy.Keys.AddRange(c.Keys);
                rebased.Rotations.Add(copy);
            }
            foreach (var c in clip.References)
            {
                var copy = new UnityAnimationClip.ReferenceCurve { Path = Near(c.Path), ClassId = c.ClassId, Script = c.Script, Attribute = c.Attribute };
                copy.Keys.AddRange(c.Keys);
                rebased.References.Add(copy);
            }
            _rebasedClips[(guid, from)] = rebased;
            return rebased;
        }
    }
}
