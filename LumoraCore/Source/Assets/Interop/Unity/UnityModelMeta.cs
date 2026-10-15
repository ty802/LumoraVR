// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumora.Core.Input;

namespace Lumora.Core.Assets.Interop.Unity;

// What a model's .meta says about it: which project material stands in for each of the file's own
// materials (externalObjects), the names behind the object IDs older projects stored, and the scale.
//
// Newer projects store no ID table at all. Every object inside an imported model is then identified by
// xxHash64 of "Type:<type>->//RootNode/root/<node path>/<type><n>" (n counts repeats of the same string),
// so a scene that points at "the renderer on Body" carries that hash and nothing else. ObjectId computes
// it for a node path, with and without the "root/" level, and the importer matches both. -xlinka
public sealed class UnityModelMeta
{
    public readonly Dictionary<string, string> MaterialRemap = new(StringComparer.Ordinal);
    public readonly Dictionary<long, (int ClassId, string Name)> NamedIds = new();
    public float GlobalScale = 1f;
    public bool UseFileScale = true;
    // The file's own saved shape weights are the renderers' starting weights.
    public bool ShapeDefaults;
    // Humanoid rigs: "Right Middle Distal" -> the model's bone for it.
    public readonly Dictionary<string, string> HumanBones = new(StringComparer.Ordinal);

    // HumanBodyBones in enum order, as the names the humanoid table uses.
    private static readonly string[] HumanBoneNames =
    {
        "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
        "Spine", "Chest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm",
        "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftToes", "RightToes", "LeftEye", "RightEye", "Jaw",
        "Left Thumb Proximal", "Left Thumb Intermediate", "Left Thumb Distal",
        "Left Index Proximal", "Left Index Intermediate", "Left Index Distal",
        "Left Middle Proximal", "Left Middle Intermediate", "Left Middle Distal",
        "Left Ring Proximal", "Left Ring Intermediate", "Left Ring Distal",
        "Left Little Proximal", "Left Little Intermediate", "Left Little Distal",
        "Right Thumb Proximal", "Right Thumb Intermediate", "Right Thumb Distal",
        "Right Index Proximal", "Right Index Intermediate", "Right Index Distal",
        "Right Middle Proximal", "Right Middle Intermediate", "Right Middle Distal",
        "Right Ring Proximal", "Right Ring Intermediate", "Right Ring Distal",
        "Right Little Proximal", "Right Little Intermediate", "Right Little Distal",
        "UpperChest",
    };

    public string? HumanBone(int index)
        => index >= 0 && index < HumanBoneNames.Length && HumanBones.TryGetValue(HumanBoneNames[index], out var bone) ? bone : null;

    // The humanoid table as rig keys. The table's thumb starts at the joint off the wrist, which is our
    // metacarpal, so its three thumb names shift down one segment.
    public Dictionary<BodyNode, string> HumanoidMap()
    {
        var map = new Dictionary<BodyNode, string>();
        foreach (var (human, bone) in HumanBones)
        {
            var parts = human.Split(' ');
            if (parts.Length == 1)
            {
                if (!char.IsDigit(human[0]) && Enum.TryParse<BodyNode>(human, out var node) && node >= BodyNode.Hips && !node.IsFinger())
                    map[node] = bone;
                continue;
            }
            if (parts.Length != 3)
                continue;
            Chirality side;
            if (parts[0] == "Left") side = Chirality.Left;
            else if (parts[0] == "Right") side = Chirality.Right;
            else continue;
            FingerType finger;
            switch (parts[1])
            {
                case "Thumb": finger = FingerType.Thumb; break;
                case "Index": finger = FingerType.Index; break;
                case "Middle": finger = FingerType.Middle; break;
                case "Ring": finger = FingerType.Ring; break;
                case "Little": finger = FingerType.Pinky; break;
                default: continue;
            }
            bool thumb = finger == FingerType.Thumb;
            FingerSegmentType segment;
            switch (parts[2])
            {
                case "Proximal": segment = thumb ? FingerSegmentType.Metacarpal : FingerSegmentType.Proximal; break;
                case "Intermediate": segment = thumb ? FingerSegmentType.Proximal : FingerSegmentType.Intermediate; break;
                case "Distal": segment = FingerSegmentType.Distal; break;
                default: continue;
            }
            map[finger.ComposeFinger(segment, side)] = bone;
        }
        return map;
    }

    public static UnityModelMeta Parse(string? metaText)
    {
        var meta = new UnityModelMeta();
        if (string.IsNullOrEmpty(metaText))
            return meta;
        var root = UnityYaml.ParseMapping(metaText);
        var importer = root["ModelImporter"] ?? root;

        if (importer["externalObjects"] is UnitySeq external)
        {
            foreach (var item in external.Items)
            {
                var first = item["first"];
                if (first?["type"]?.Text != "UnityEngine:Material")
                    continue;
                var name = first["name"]?.Text;
                var guid = item["second"]?.Ref.Guid;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(guid))
                    meta.MaterialRemap[name] = guid;
            }
        }

        // 2019.3+: "- first: {<class>: <id>} second: <name>"
        if (importer["internalIDToNameTable"] is UnitySeq table)
        {
            foreach (var item in table.Items)
            {
                if (item["first"] is not UnityMap first || first.Entries.Count == 0)
                    continue;
                var entry = first.Entries[0];
                if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
                    continue;
                var name = item["second"]?.Text;
                if (name != null)
                    meta.NamedIds[entry.Value.Long()] = (classId, name);
            }
        }

        // Older: "<id>: <name>" where the class is the id's leading digits (id / 100000).
        if (importer["fileIDToRecycleName"] is UnityMap recycle)
        {
            foreach (var pair in recycle.Entries)
            {
                if (long.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && pair.Value.Text is { } name)
                    meta.NamedIds[id] = ((int)(id / 100000), name);
            }
        }

        if (importer["humanDescription"]?["human"] is UnitySeq human)
        {
            foreach (var item in human.Items)
            {
                var bone = item["boneName"]?.Text;
                var name = item["humanName"]?.Text;
                if (!string.IsNullOrEmpty(bone) && !string.IsNullOrEmpty(name))
                    meta.HumanBones[name] = bone;
            }
        }

        meta.ShapeDefaults = (importer["importBlendShapeDeformPercent"]?.Int() ?? 0) != 0;
        var meshes = importer["meshes"];
        if (meshes != null)
        {
            meta.GlobalScale = meshes["globalScale"]?.Float(1f) ?? 1f;
            meta.UseFileScale = (meshes["useFileScale"]?.Int(1) ?? 1) != 0;
        }
        return meta;
    }

    // An empty path is the model's own root: "//RootNode/root/Transform0", "//RootNode/root0".
    public static long ObjectId(string type, string nodePath, bool withRootLevel, int repeat = 0)
    {
        var key = "Type:" + type + "->" + NodeKey(nodePath, withRootLevel) + "/" + type + repeat.ToString(CultureInfo.InvariantCulture);
        return unchecked((long)XxHash64(Encoding.UTF8.GetBytes(key)));
    }

    public static long GameObjectId(string nodePath, bool withRootLevel, int repeat = 0)
    {
        var key = "Type:GameObject->" + NodeKey(nodePath, withRootLevel) + repeat.ToString(CultureInfo.InvariantCulture);
        return unchecked((long)XxHash64(Encoding.UTF8.GetBytes(key)));
    }

    private static string NodeKey(string nodePath, bool withRootLevel)
    {
        var key = withRootLevel ? "//RootNode/root" : "//RootNode";
        return nodePath.Length > 0 ? key + "/" + nodePath : key;
    }

    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    // Standard xxHash64, seed 0.
    public static ulong XxHash64(ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        int i = 0;
        ulong h;
        if (length >= 32)
        {
            ulong v1 = unchecked(Prime1 + Prime2), v2 = Prime2, v3 = 0, v4 = unchecked(0 - Prime1);
            while (i + 32 <= length)
            {
                v1 = Round(v1, Read64(data, i)); i += 8;
                v2 = Round(v2, Read64(data, i)); i += 8;
                v3 = Round(v3, Read64(data, i)); i += 8;
                v4 = Round(v4, Read64(data, i)); i += 8;
            }
            h = unchecked(Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18));
            h = Merge(h, v1);
            h = Merge(h, v2);
            h = Merge(h, v3);
            h = Merge(h, v4);
        }
        else
        {
            h = Prime5;
        }
        h = unchecked(h + (ulong)length);
        while (i + 8 <= length)
        {
            h ^= Round(0, Read64(data, i));
            h = unchecked(Rotl(h, 27) * Prime1 + Prime4);
            i += 8;
        }
        if (i + 4 <= length)
        {
            h ^= unchecked(BitConverter.ToUInt32(data.Slice(i, 4)) * Prime1);
            h = unchecked(Rotl(h, 23) * Prime2 + Prime3);
            i += 4;
        }
        while (i < length)
        {
            h ^= unchecked(data[i] * Prime5);
            h = unchecked(Rotl(h, 11) * Prime1);
            i++;
        }
        h ^= h >> 33;
        h = unchecked(h * Prime2);
        h ^= h >> 29;
        h = unchecked(h * Prime3);
        h ^= h >> 32;
        return h;
    }

    private static ulong Read64(ReadOnlySpan<byte> data, int i) => BitConverter.ToUInt64(data.Slice(i, 8));
    private static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
    private static ulong Round(ulong acc, ulong input) => unchecked(Rotl(acc + input * Prime2, 31) * Prime1);
    private static ulong Merge(ulong acc, ulong val) => unchecked((acc ^ Round(0, val)) * Prime1 + Prime4);
}
