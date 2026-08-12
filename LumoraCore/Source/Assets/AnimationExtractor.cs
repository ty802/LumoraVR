// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

// Turns a parsed model's animation channels into clips.
//
// TRANSFORM CONVENTION. Channel keys are node-LOCAL translation, rotation and scale - the exact same
// space the importer writes into each node slot's LocalPosition/LocalRotation/LocalScale - so they
// copy across with no conversion at all. Two traps this deliberately avoids:
//
//  - The node walk TRANSPOSES the node matrix before decomposing it. That transpose is about how the
//    row-major matrix lands in the interop struct, NOT a change of coordinate system. Animation keys
//    arrive as separate vectors and quaternions, never as a matrix, so nothing here transposes. Doing
//    it "for consistency" would scramble every rotation.
//  - The import-time rescale and centering are written onto the MODEL ROOT slot, an ancestor of every
//    animated node. A child's local position is untouched by an ancestor's scale, so position keys
//    must NOT be pre-scaled; scaling them would double-apply the model scale and make the skeleton
//    fly apart the moment the clip starts.
//
// Get either wrong and the animation plays mirrored or exploded while the rest pose still looks
// right, which is a miserable thing to debug. -xlinka
internal static class AnimationExtractor
{
    // Assimp's documented fallback when a file states no tick rate.
    private const double DefaultTicksPerSecond = 25.0;

    // Pure CPU over already-parsed data: safe to call off the world thread, and it touches no data model.
    // Animations that carry no usable channel are dropped rather than emitted empty.
    public static List<AnimationClip> Extract(Assimp.Scene scene)
    {
        var clips = new List<AnimationClip>();
        if (scene == null || !scene.HasAnimations)
        {
            return clips;
        }

        IReadOnlyList<Assimp.Animation> animations = scene.Animations;
        var morphNamesByNode = BuildMorphNameMap(scene);

        for (int a = 0; a < animations.Count; a++)
        {
            var anim = animations[a];
            if (anim == null)
            {
                continue;
            }

            double tps = anim.TicksPerSecond > 0.0 ? anim.TicksPerSecond : DefaultTicksPerSecond;
            string name = string.IsNullOrEmpty(anim.Name) ? $"Animation{a}" : anim.Name;

            // Framerate stays 0 (unspecified): a tick rate is not an authoring frame rate - glTF
            // reports 1000 ticks/s regardless of how the clip was keyed - and claiming otherwise would
            // put a fabricated number in front of anyone reading the clip.
            var clip = new AnimationClip(name);

            AddNodeChannels(clip, anim, tps);
            AddMorphChannels(clip, anim, tps, morphNamesByNode);

            if (clip.TrackCount == 0)
            {
                continue;
            }

            float declared = (float)(anim.DurationInTicks / tps);
            clip.Duration = declared > 0f ? declared : clip.GetMaxTrackDuration();
            clips.Add(clip);
        }

        return clips;
    }

    private static void AddNodeChannels(AnimationClip clip, Assimp.Animation anim, double tps)
    {
        if (!anim.HasNodeAnimations)
        {
            return;
        }

        IReadOnlyList<Assimp.NodeAnimationChannel> channels = anim.NodeAnimationChannels;
        for (int c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            if (channel == null || string.IsNullOrEmpty(channel.NodeName))
            {
                continue;
            }

            if (channel.HasPositionKeys)
            {
                IReadOnlyList<Assimp.VectorKey> keys = channel.PositionKeys;
                if (keys.Count > 0)
                {
                    var track = clip.AddCurveTrack<float3>(channel.NodeName, "Position");
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k];
                        track.AddKey((float)(key.Time / tps),
                            new float3(key.Value.X, key.Value.Y, key.Value.Z),
                            MapInterpolation(key.Interpolation));
                    }
                }
            }

            if (channel.HasRotationKeys)
            {
                IReadOnlyList<Assimp.QuaternionKey> keys = channel.RotationKeys;
                if (keys.Count > 0)
                {
                    var track = clip.AddCurveTrack<floatQ>(channel.NodeName, "Rotation");
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k];
                        track.AddKey((float)(key.Time / tps),
                            new floatQ(key.Value.X, key.Value.Y, key.Value.Z, key.Value.W),
                            MapInterpolation(key.Interpolation));
                    }
                }
            }

            if (channel.HasScalingKeys)
            {
                IReadOnlyList<Assimp.VectorKey> keys = channel.ScalingKeys;
                if (keys.Count > 0)
                {
                    var track = clip.AddCurveTrack<float3>(channel.NodeName, "Scale");
                    for (int k = 0; k < keys.Count; k++)
                    {
                        var key = keys[k];
                        track.AddKey((float)(key.Time / tps),
                            new float3(key.Value.X, key.Value.Y, key.Value.Z),
                            MapInterpolation(key.Interpolation));
                    }
                }
            }
        }
    }

    // Morph channels arrive as (time -> list of target index + weight) rather than one stream per
    // target, so they are transposed into a track per target. A key is only written for targets the
    // key actually mentions: a file that animates one shape does not get flat zero tracks for the
    // other fifty.
    private static void AddMorphChannels(AnimationClip clip, Assimp.Animation anim, double tps,
        Dictionary<string, List<string>> morphNamesByNode)
    {
        if (anim.MeshMorphAnimationChannelCount == 0)
        {
            return;
        }

        IReadOnlyList<Assimp.MeshMorphAnimationChannel> channels = anim.MeshMorphAnimationChannels;
        for (int c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            if (channel == null || !channel.HasMeshMorphKeys || string.IsNullOrEmpty(channel.Name))
            {
                continue;
            }

            morphNamesByNode.TryGetValue(channel.Name, out var shapeNames);

            IReadOnlyList<Assimp.MeshMorphKey> keys = channel.MeshMorphKeys;
            var tracks = new Dictionary<int, CurveAnimationTrack<float>>();

            for (int k = 0; k < keys.Count; k++)
            {
                var key = keys[k];
                IReadOnlyList<int> values = key.Values;
                IReadOnlyList<double> weights = key.Weights;
                if (values == null || weights == null)
                {
                    continue;
                }

                int pairs = values.Count < weights.Count ? values.Count : weights.Count;
                float time = (float)(key.Time / tps);

                for (int p = 0; p < pairs; p++)
                {
                    int targetIndex = values[p];
                    if (targetIndex < 0)
                    {
                        continue;
                    }

                    if (!tracks.TryGetValue(targetIndex, out var track))
                    {
                        // Same naming the mesh decoder gives blend shapes, so the property resolves
                        // against SkinnedMeshRenderer.BlendShapeNames by name.
                        string shapeName = (shapeNames != null && targetIndex < shapeNames.Count)
                            ? shapeNames[targetIndex]
                            : $"Morph{targetIndex}";
                        track = clip.AddCurveTrack<float>(channel.Name, $"BlendShape.{shapeName}");
                        tracks[targetIndex] = track;
                    }

                    track.AddKey(time, (float)weights[p]);
                }
            }
        }
    }

    // Node name -> blend shape names, mirroring the mesh decoder's naming (including its MorphN
    // fallback) so a morph track's property string matches what the renderer will report.
    private static Dictionary<string, List<string>> BuildMorphNameMap(Assimp.Scene scene)
    {
        var map = new Dictionary<string, List<string>>();
        if (!scene.HasMeshes)
        {
            return map;
        }
        CollectMorphNames(scene.RootNode, scene, map);
        return map;
    }

    private static void CollectMorphNames(Assimp.Node? node, Assimp.Scene scene,
        Dictionary<string, List<string>> map)
    {
        if (node == null)
        {
            return;
        }

        if (node.HasMeshes && !string.IsNullOrEmpty(node.Name) && !map.ContainsKey(node.Name))
        {
            IReadOnlyList<int> meshIndices = node.MeshIndices;
            for (int i = 0; i < meshIndices.Count; i++)
            {
                int meshIndex = meshIndices[i];
                if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                {
                    continue;
                }
                var mesh = scene.Meshes[meshIndex];
                if (mesh == null || !mesh.HasMeshAnimationAttachments)
                {
                    continue;
                }

                IReadOnlyList<Assimp.MeshAnimationAttachment> attachments = mesh.MeshAnimationAttachments;
                var names = new List<string>(attachments.Count);
                for (int a = 0; a < attachments.Count; a++)
                {
                    names.Add(string.IsNullOrEmpty(attachments[a].Name) ? $"Morph{a}" : attachments[a].Name);
                }
                map[node.Name] = names;
                break;
            }
        }

        foreach (var child in node.Children)
        {
            CollectMorphNames(child, scene, map);
        }
    }

    // Assimp exposes a per-key interpolation flag but no tangent data, so a cubic-spline key is
    // imported as linear: the shape is close for densely-keyed clips and honest for sparse ones,
    // whereas inventing tangents would put motion in the file that the artist never authored. Logged
    // once per clip build rather than per key so it is visible without flooding the console.
    private static KeyframeInterpolation MapInterpolation(Assimp.AnimationInterpolation interpolation)
    {
        switch (interpolation)
        {
            case Assimp.AnimationInterpolation.Step:
                return KeyframeInterpolation.Step;
            case Assimp.AnimationInterpolation.CubicSpline:
                if (!_warnedCubic)
                {
                    _warnedCubic = true;
                    Logger.Warn("AnimationExtractor: cubic-spline keys imported as linear - the model parser does not expose tangents");
                }
                return KeyframeInterpolation.Linear;
            default:
                return KeyframeInterpolation.Linear;
        }
    }

    private static bool _warnedCubic;
}
