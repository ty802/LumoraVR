// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// The rest of what a descriptor says about the face. How the mouth moves: a shape per sound, a jaw bone or one
// open shape by how loud the voice is, or nothing but the viseme value the animator reads. Whether the eyes
// look at all, which bones are the eyes, and how far each one turns each way. And what the lids are: none,
// bones with a pose for open, shut, looking up and looking down, or exactly which shape blinks and which two
// follow the eyes up and down. An avatar that names its lids gets those and nothing found by name, so one
// that names no blink does not blink.
//
// Shapes are named by their number on the mesh as the project counts them, which is the file's own order and
// not the order the model reader hands them back in; the file says which name each number is. Lid and jaw
// poses are the bone's rotation in its parent, mirrored like every other rotation. -xlinka
public static partial class UnityAvatarBuilder
{
    private enum LipSyncMode { Default = 0, JawBone = 1, JawShape = 2, Visemes = 3, VisemeValue = 4 }

    private enum EyelidKind { None = 0, Bones = 1, Shapes = 2 }

    private static void ApplyFace(UnityImportCandidate candidate, Slot model, NodeIndex nodes, Assembly? assembly, UnityImportResult result)
    {
        var descriptor = candidate.Descriptor;
        if (descriptor == null)
            return;
        try
        {
            ApplyMouth(candidate, descriptor, model, nodes, result);
        }
        catch (Exception ex)
        {
            Logger.Warn($"UnityPackage: the descriptor's lip sync could not be set: {ex.Message}");
        }
        try
        {
            ApplyEyes(candidate, descriptor, model, nodes, assembly, result);
        }
        catch (Exception ex)
        {
            Logger.Warn($"UnityPackage: the descriptor's eyes could not be set: {ex.Message}");
        }
    }

    // ---- mouth ----

    private static void ApplyMouth(UnityImportCandidate candidate, UnityDocument descriptor, Slot model, NodeIndex nodes, UnityImportResult result)
    {
        var mode = (LipSyncMode)Number(descriptor["lipSync"]);
        var voice = model.GetComponent<LipSyncAnalyzer>() ?? model.AttachComponent<LipSyncAnalyzer>();
        var visemes = model.GetComponent<VisemeWeightDriver>() ?? model.AttachComponent<VisemeWeightDriver>();
        visemes.Source.Target = voice;
        if (mode is LipSyncMode.Default or LipSyncMode.Visemes)
            return;

        // Every other mode moves no shape per sound; the driver stays as the voice the animator reads.
        visemes.ShapeNames.Clear();
        visemes.ShapeMesh.Target = null!;
        visemes.ExactShapes.Value = true;
        result.VisemesMapped = false;

        if (mode == LipSyncMode.JawBone)
        {
            var jaw = Bone(candidate, nodes, descriptor["lipSyncJawBone"]);
            if (jaw == null)
            {
                result.Notes.Add("The descriptor opens the mouth with a jaw bone this import does not have; the mouth stays still.");
                return;
            }
            var driver = model.GetComponent<VoiceJawDriver>() ?? model.AttachComponent<VoiceJawDriver>();
            driver.Source.Target = voice;
            driver.Jaw.Target = jaw;
            driver.ShutRotation.Value = ProjectRotation(jaw, Rotation(descriptor["lipSyncJawClosed"]) ?? floatQ.Identity);
            driver.OpenRotation.Value = ProjectRotation(jaw, Rotation(descriptor["lipSyncJawOpen"]) ?? floatQ.Identity);
            result.Notes.Add($"The mouth opens the jaw bone '{jaw.SlotName.Value}' as the wearer talks.");
        }
        else if (mode == LipSyncMode.JawShape)
        {
            var mesh = Renderer(candidate, nodes, descriptor["VisemeSkinnedMesh"]);
            var shape = descriptor["MouthOpenBlendShapeName"]?.Text;
            if (mesh == null || string.IsNullOrEmpty(shape) || shape == "-none-")
            {
                result.Notes.Add("The descriptor opens the mouth with a shape this import does not have; the mouth stays still.");
                return;
            }
            var driver = model.GetComponent<VoiceJawDriver>() ?? model.AttachComponent<VoiceJawDriver>();
            driver.Source.Target = voice;
            driver.ShapeMesh.Target = mesh;
            driver.OpenShape.Value = shape;
            result.Notes.Add($"The mouth opens with the shape '{shape}' as the wearer talks.");
        }
        else if (mode == LipSyncMode.VisemeValue)
        {
            result.Notes.Add("Lip sync moves no shapes itself; the avatar's animator reads the viseme value.");
        }
    }

    // ---- eyes ----

    private static void ApplyEyes(UnityImportCandidate candidate, UnityDocument descriptor, Slot model, NodeIndex nodes, Assembly? assembly, UnityImportResult result)
    {
        var gaze = model.GetComponent<EyeGazeDriver>() ?? model.AttachComponent<EyeGazeDriver>();
        var blink = model.GetComponent<BlinkDriver>() ?? model.AttachComponent<BlinkDriver>();
        if (descriptor["enableEyeLook"] is { } eyeLook && Number(eyeLook) == 0)
        {
            gaze.Enabled.Value = false;
            result.Notes.Add("Eye look is off in the descriptor, so the eyes stay still.");
            return;
        }

        var settings = descriptor["customEyeLookSettings"];
        if (settings == null)
            return;

        gaze.LeftEye.Target = Bone(candidate, nodes, settings["leftEye"])!;
        gaze.RightEye.Target = Bone(candidate, nodes, settings["rightEye"])!;
        gaze.LeftEyeLimits.Value = Limits(settings, "left");
        gaze.RightEyeLimits.Value = Limits(settings, "right");
        // Eye look turns the eye bones only; look shapes on these faces belong to face tracking.
        gaze.ExactEyes.Value = true;

        switch ((EyelidKind)Number(settings["eyelidType"]))
        {
            case EyelidKind.None:
                blink.ExactLids.Value = true;
                result.Notes.Add("The descriptor gives the eyes no lids, so the avatar does not blink.");
                break;
            case EyelidKind.Bones:
                LidBones(candidate, nodes, settings, blink);
                break;
            case EyelidKind.Shapes:
                LidShapes(candidate, nodes, assembly, settings, blink, result);
                break;
        }
    }

    // Degrees each way for one eye: up, down, left, right, each the turn from looking straight to looking that
    // way. A way the descriptor leaves blank stays zero, which leaves it to the gaze's own cone; a way it sets
    // to no turn at all keeps a sliver above zero so it still reads as set.
    private static float4 Limits(UnityNode settings, string eye)
    {
        var straight = Rotation(settings["eyesLookingStraight"]?[eye]) ?? floatQ.Identity;
        float Way(string key)
        {
            var to = Rotation(settings[key]?[eye]);
            if (to == null)
                return 0f;
            float dot = MathF.Min(1f, MathF.Abs(floatQ.Dot(straight.Normalized, to.Value.Normalized)));
            return MathF.Max(2f * MathF.Acos(dot) * (180f / MathF.PI), 0.01f);
        }
        return new float4(Way("eyesLookingUp"), Way("eyesLookingDown"), Way("eyesLookingLeft"), Way("eyesLookingRight"));
    }

    private static void LidShapes(UnityImportCandidate candidate, NodeIndex nodes, Assembly? assembly, UnityNode settings,
        BlinkDriver blink, UnityImportResult result)
    {
        long id = settings["eyelidsSkinnedMesh"]?.Ref.FileId ?? 0;
        var node = id != 0 ? candidate.ResolveComponent(id) : null;
        var slot = node != null ? nodes.Resolve(node) : null;
        var mesh = slot?.GetComponent<SkinnedMeshRenderer>() ?? slot?.GetComponentInChildren<SkinnedMeshRenderer>();
        var numbers = Numbers(settings["eyelidsBlendshapes"]);
        if (mesh == null || slot == null)
        {
            result.Notes.Add("The descriptor's eyelid mesh is not in this import; blinks are found by name.");
            return;
        }
        var order = ShapeOrder(candidate, assembly, slot);
        if (order == null)
        {
            result.Notes.Add("The eyelid shapes could not be read from the model file; blinks are found by name.");
            return;
        }

        string Name(int at)
        {
            if (at < 0 || at >= numbers.Count || numbers[at] < 0 || numbers[at] >= order.Count)
                return "";
            var shape = order[numbers[at]];
            // The name the mesh carries, when its shapes are already in.
            var asset = mesh.MeshAsset.Asset;
            foreach (var candidateName in new[] { shape.Name, shape.ShapeName, shape.Name + "." + shape.ShapeName })
                if (mesh.GetBlendShapeIndex(candidateName) >= 0 || (asset?.MeshData != null && asset.BlendShapeIndex(candidateName) >= 0))
                    return candidateName;
            return shape.Name;
        }

        blink.ExactLids.Value = true;
        blink.LidMesh.Target = mesh;
        blink.BlinkShape.Value = Name(0);
        blink.LookUpShape.Value = Name(1);
        blink.LookDownShape.Value = Name(2);
        if (string.IsNullOrEmpty(blink.BlinkShape.Value))
            result.Notes.Add("The descriptor names no blink shape, so the avatar does not blink.");
        Logger.Debug($"UnityPackage: lids on '{slot.SlotName.Value}': blink '{blink.BlinkShape.Value}', up '{blink.LookUpShape.Value}', down '{blink.LookDownShape.Value}'");
    }

    // The renderer's shapes in the project's numbering: its model file's own order for the node it sits on.
    private static List<UnityFbxShapes.Shape>? ShapeOrder(UnityImportCandidate candidate, Assembly? assembly, Slot slot)
    {
        if (assembly != null)
        {
            foreach (var (_, file, _, names) in assembly.Models)
            {
                if (!names.TryGetValue(slot, out var fileName) || file.File == null)
                    continue;
                return UnityFbxShapes.Read(file.File).TryGetValue(fileName, out var shapes) ? shapes : null;
            }
            return null;
        }
        if (candidate.Model?.File == null)
            return null;
        return UnityFbxShapes.Read(candidate.Model.File).TryGetValue(slot.SlotName.Value ?? "", out var own) ? own : null;
    }

    private static void LidBones(UnityImportCandidate candidate, NodeIndex nodes, UnityNode settings, BlinkDriver blink)
    {
        // Upper left, upper right, lower left, lower right, as the blink driver keeps them.
        var lids = new (string Field, string Row, string Eye)[]
        {
            ("upperLeftEyelid", "upper", "left"), ("upperRightEyelid", "upper", "right"),
            ("lowerLeftEyelid", "lower", "left"), ("lowerRightEyelid", "lower", "right"),
        };
        blink.ExactLids.Value = true;
        blink.LidBones.Clear();
        blink.LidOpen.Clear();
        blink.LidShut.Clear();
        blink.LidUp.Clear();
        blink.LidDown.Clear();
        foreach (var (field, row, eye) in lids)
        {
            var bone = Bone(candidate, nodes, settings[field]);
            blink.LidBones.Add(bone);
            floatQ Pose(string state)
                => bone != null && Rotation(settings[state]?[row]?[eye]) is { } q ? ProjectRotation(bone, q) : new floatQ(0f, 0f, 0f, 0f);
            blink.LidOpen.Add(Pose("eyelidsDefault"));
            blink.LidShut.Add(Pose("eyelidsClosed"));
            blink.LidUp.Add(Pose("eyelidsLookingUp"));
            blink.LidDown.Add(Pose("eyelidsLookingDown"));
        }
    }

    // ---- reading ----

    private static Slot? Bone(UnityImportCandidate candidate, NodeIndex nodes, UnityNode? reference)
    {
        long id = reference?.Ref.FileId ?? 0;
        var node = id != 0 ? candidate.ResolveTransform(id) ?? candidate.ResolveObject(id) : null;
        return node != null ? nodes.Resolve(node) : null;
    }

    private static SkinnedMeshRenderer? Renderer(UnityImportCandidate candidate, NodeIndex nodes, UnityNode? reference)
    {
        long id = reference?.Ref.FileId ?? 0;
        var node = id != 0 ? candidate.ResolveComponent(id) : null;
        var slot = node != null ? nodes.Resolve(node) : null;
        return slot?.GetComponent<SkinnedMeshRenderer>() ?? slot?.GetComponentInChildren<SkinnedMeshRenderer>();
    }

    // A whole number, quoted or not.
    private static int Number(UnityNode? node)
        => int.TryParse(node?.Text?.Trim().Trim('\'', '"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // A list of whole numbers: written out as items, or packed as little-endian hex the way the project stores
    // a plain int array.
    private static List<int> Numbers(UnityNode? node)
    {
        var list = new List<int>();
        if (node is UnitySeq seq)
        {
            foreach (var item in seq.Items)
                list.Add(Number(item));
            return list;
        }
        var hex = node?.Text?.Trim() ?? "";
        for (int i = 0; i + 8 <= hex.Length; i += 8)
        {
            if (uint.TryParse(hex.AsSpan(i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
                list.Add((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(raw));
        }
        return list;
    }

    // A rotation as the project wrote it; null when it is all zeros (never set).
    private static floatQ? Rotation(UnityNode? node)
    {
        if (node == null)
            return null;
        var q = new floatQ(node["x"]?.Float() ?? 0f, node["y"]?.Float() ?? 0f, node["z"]?.Float() ?? 0f, node["w"]?.Float() ?? 0f);
        return MathF.Abs(q.x) + MathF.Abs(q.y) + MathF.Abs(q.z) + MathF.Abs(q.w) < 1e-6f ? null : q.Normalized;
    }

    // A rotation the project gave a bone in its parent, as the bone's local rotation here. The reader can slip
    // pivot helpers above a bone, so the project's parent is the nearest real one, not the helper.
    private static floatQ ProjectRotation(Slot bone, floatQ local)
    {
        var ours = MirrorRotation(local);
        var parent = bone.Parent;
        if (parent == null)
            return ours;
        var frame = parent;
        while (frame.Parent != null && (frame.SlotName.Value ?? "").Contains("$AssimpFbx$", StringComparison.Ordinal))
            frame = frame.Parent;
        return ReferenceEquals(frame, parent) ? ours : parent.GlobalRotationToLocal(frame.GlobalRotation * ours);
    }
}
