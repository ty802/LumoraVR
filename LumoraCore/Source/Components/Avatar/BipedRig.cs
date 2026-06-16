// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Component that stores bone slot references for a biped humanoid rig.
/// Used by the IK and other avatar systems to drive skeleton bones.
/// </summary>
[ComponentCategory("Rendering")]
public class BipedRig : Component
{
    /// <summary>
    /// Optional forward axis hint for the rig.
    /// </summary>
    public readonly Sync<float3?> ForwardAxis = new();

    /// <summary>
    /// Dictionary mapping BodyNode types to their corresponding Slot references.
    /// </summary>
    public SyncRefDictionary<BodyNode, Slot> Bones { get; private set; } = null!;

    /// <summary>
    /// Minimal set of bones required for a valid biped rig.
    /// </summary>
    public static readonly BodyNode[] MinimalBiped = new BodyNode[]
    {
        BodyNode.Hips,
        BodyNode.Spine,
        BodyNode.Head,
        BodyNode.LeftUpperArm,
        BodyNode.LeftLowerArm,
        BodyNode.LeftHand,
        BodyNode.RightUpperArm,
        BodyNode.RightLowerArm,
        BodyNode.RightHand,
        BodyNode.LeftUpperLeg,
        BodyNode.LeftLowerLeg,
        BodyNode.LeftFoot,
        BodyNode.RightUpperLeg,
        BodyNode.RightLowerLeg,
        BodyNode.RightFoot
    };

    /// <summary>
    /// Get or set a bone slot by body node type.
    /// </summary>
    public Slot this[BodyNode boneType]
    {
        get => TryGetBone(boneType);
        set
        {
            if (value != null)
                Bones.Add(boneType, value);
            else
                Bones.Remove(boneType);
        }
    }

    /// <summary>
    /// Whether this rig has all minimal biped bones.
    /// </summary>
    public bool IsBiped
    {
        get
        {
            foreach (var node in MinimalBiped)
            {
                if (!Bones.ContainsKey(node))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Whether this rig has left hand finger bones.
    /// </summary>
    public bool HasLeftHandFingers => HasMinimalHand(Chirality.Left);

    /// <summary>
    /// Whether this rig has right hand finger bones.
    /// </summary>
    public bool HasRightHandFingers => HasMinimalHand(Chirality.Right);

    public override void OnAwake()
    {
        base.OnAwake();

        Bones = new SyncRefDictionary<BodyNode, Slot>(this);
    }

    // ForwardAxis default is null (C# default for float3?, skip OnInit)

    /// <summary>
    /// Try to get a bone slot for a body node type.
    /// </summary>
    public Slot TryGetBone(BodyNode boneType)
    {
        if (Bones.TryGetTarget(boneType, out var target))
            return target;
        return null!;
    }

    /// <summary>
    /// Get the body node type for a given bone slot.
    /// </summary>
    public BodyNode GetBoneType(Slot bone)
    {
        foreach (var kvp in Bones)
        {
            if (kvp.Value.Target == bone)
                return kvp.Key;
        }
        return BodyNode.NONE;
    }

    /// <summary>
    /// Check if a body node is a required bone.
    /// </summary>
    public static bool IsRequiredBone(BodyNode node)
    {
        foreach (var required in MinimalBiped)
        {
            if (required == node)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if this rig has minimal hand bones for a chirality.
    /// </summary>
    public bool HasMinimalHand(Chirality chirality)
    {
        // Need at least 2 thumb segments
        if (FingerSegmentCount(FingerType.Thumb, chirality, excludeMetacarpal: false) < 2)
            return false;

        // Need at least 2 other fingers with 2+ segments
        int fingerCount = 0;
        for (var finger = FingerType.Index; finger <= FingerType.Pinky; finger++)
        {
            if (FingerSegmentCount(finger, chirality, excludeMetacarpal: true) >= 2)
                fingerCount++;
        }

        return fingerCount >= 2;
    }

    /// <summary>
    /// Count segments for a finger.
    /// </summary>
    public int FingerSegmentCount(FingerType finger, Chirality chirality, bool excludeMetacarpal)
    {
        int count = 0;
        foreach (var kvp in Bones)
        {
            var node = kvp.Key;
            if (!node.IsFinger())
                continue;
            if (node.GetChirality() != chirality)
                continue;
            if (excludeMetacarpal && node.GetFingerSegmentType() == FingerSegmentType.Metacarpal)
                continue;
            if (node.GetFingerType() == finger)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Get list of missing bones for a valid biped rig.
    /// </summary>
    public void GetMissingBipedBones(List<BodyNode> list)
    {
        foreach (var node in MinimalBiped)
        {
            if (!Bones.ContainsKey(node))
                list.Add(node);
        }
    }

    /// <summary>
    /// Populate bones from a SkeletonBuilder by matching bone names.
    /// </summary>
    public void PopulateFromSkeleton(SkeletonBuilder skeleton)
    {
        if (skeleton == null || !skeleton.IsBuilt.Value)
        {
            LumoraLogger.Warn("BipedRig: Cannot populate from null or unbuilt skeleton");
            return;
        }

        for (int i = 0; i < skeleton.BoneCount; i++)
        {
            var boneSlot = skeleton.BoneSlots[i];
            if (boneSlot == null)
                continue;

            var node = ClassifyBoneName(skeleton.BoneNames[i]);
            if (node == BodyNode.NONE || Bones.ContainsKey(node))
                continue;   // first match wins (skips e.g. Spine_02/03 clobbering Spine_01)
            Bones.Add(node, boneSlot);
        }

        LumoraLogger.Log($"BipedRig: Populated {Bones.Count} bones from skeleton, IsBiped={IsBiped}");
    }

    /// <summary>
    /// Map a bone name to its body node by heuristic, so real rigs work regardless of naming
    /// convention. Side is detected from the name (Left/Right, _L/_R, L_/R_) and the base name picks
    /// the node (UpperArm/Bicep, ForeArm/LowerArm/Elbow, Hand/Wrist, Thigh/UpLeg, Calf/Shin/Knee,
    /// Foot, Toe, ...). Returns <see cref="BodyNode.NONE"/> for bones it can't place.
    /// </summary>
    public static BodyNode ClassifyBoneName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return BodyNode.NONE;

        var names = SplitName(name);
        if (names.Contains("twist"))
            return BodyNode.NONE;   // twist/roll helper bones aren't body nodes

        string t = name.ToLowerInvariant().Replace("armature", "");

        // Center chain - no side needed.
        if (t.Contains("hips") || t.Contains("pelvis"))
            return BodyNode.Hips;
        if (t.Contains("upperchest"))
            return BodyNode.UpperChest;
        if (t.Contains("chest") || t.Contains("ribcage"))
            return BodyNode.Chest;
        if (t.Contains("spine") || t.Contains("torso"))
            return BodyNode.Spine;
        if (t.Contains("neck") && !t.Contains("necklace"))
            return BodyNode.Neck;
        if (t.Contains("head"))
            return BodyNode.Head;

        var chirality = DetectChirality(names);

        // Eyes (need a side, exclude brows/lids/blink shapes).
        bool eyeish = t.Contains("eye") || names.Contains("eye");
        if (eyeish && !t.Contains("eyebrow") && !t.Contains("eyelid")
            && !names.Contains("brow") && !names.Contains("lid") && !names.Contains("blink"))
        {
            return chirality == Chirality.Right ? BodyNode.RightEye
                : chirality == Chirality.Left ? BodyNode.LeftEye
                : BodyNode.NONE;
        }

        if (chirality == null)
            return BodyNode.NONE;   // limbs need a side
        bool right = chirality == Chirality.Right;

        if (t.Contains("shoulder") || t.Contains("clavicle") || t.Contains("collar"))
            return right ? BodyNode.RightShoulder : BodyNode.LeftShoulder;
        if (t.Contains("upperarm") || t.Contains("uparm") || t.Contains("uarm") || t.Contains("bicep"))
            return right ? BodyNode.RightUpperArm : BodyNode.LeftUpperArm;
        if (t.Contains("forearm") || t.Contains("lowerarm") || t.Contains("lowarm") || t.Contains("elbow"))
            return right ? BodyNode.RightLowerArm : BodyNode.LeftLowerArm;
        if (t.Contains("hand") || t.Contains("wrist") || t.Contains("palm"))
            return right ? BodyNode.RightHand : BodyNode.LeftHand;
        if (t.Contains("thigh") || t.Contains("upleg") || t.Contains("uleg") || t.Contains("upperleg")
            || (t.Contains("hip") && !t.Contains("hips")))
            return right ? BodyNode.RightUpperLeg : BodyNode.LeftUpperLeg;
        if (t.Contains("calf") || t.Contains("lowerleg") || t.Contains("lowleg") || t.Contains("knee") || t.Contains("shin"))
            return right ? BodyNode.RightLowerLeg : BodyNode.LeftLowerLeg;
        if (t.Contains("foot") || t.Contains("ankle") || names.Contains("feet"))
            return right ? BodyNode.RightFoot : BodyNode.LeftFoot;
        if (t.Contains("toe") || (t.Contains("ball") && !eyeish))
            return right ? BodyNode.RightToes : BodyNode.LeftToes;

        // Ambiguous bare "arm"/"leg" - assume the upper segment.
        if (t.Contains("arm"))
            return right ? BodyNode.RightUpperArm : BodyNode.LeftUpperArm;
        if (t.Contains("leg"))
            return right ? BodyNode.RightUpperLeg : BodyNode.LeftUpperLeg;

        return BodyNode.NONE;
    }

    // Split a bone name into lowercase word segments, breaking on non-letters and camelCase boundaries
    // ("UpperArm_L" -> upper, arm, l). Used for side detection and keyword matching.
    private static List<string> SplitName(string name)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        char prev = '\0';
        foreach (var c in name)
        {
            bool boundary = !char.IsLetter(c) || (char.IsUpper(c) && char.IsLower(prev));
            if (boundary)
                Flush(segments, current);
            if (char.IsLetter(c))
                current.Append(c);
            prev = c;
        }
        Flush(segments, current);
        return segments;
    }

    private static void Flush(List<string> segments, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
            return;
        var segment = current.ToString().ToLowerInvariant();
        if (!segments.Contains(segment))
            segments.Add(segment);
        current.Clear();
    }

    // Left/Right from explicit words or isolated L/R segments (covers Left/Right, _L/_R, L_/R_).
    private static Chirality? DetectChirality(List<string> names)
    {
        bool left = names.Contains("left") || names.Contains("l");
        bool right = names.Contains("right") || names.Contains("r");
        if (left ^ right)
            return left ? Chirality.Left : Chirality.Right;
        return null;
    }

    /// <summary>
    /// Log diagnostic info about this rig.
    /// </summary>
    public void LogDiagnosticInfo()
    {
        LumoraLogger.Log($"BipedRig Diagnostic Info:");
        LumoraLogger.Log($"  Total bones: {Bones.Count}");
        LumoraLogger.Log($"  IsBiped: {IsBiped}");
        LumoraLogger.Log($"  HasLeftHandFingers: {HasLeftHandFingers}");
        LumoraLogger.Log($"  HasRightHandFingers: {HasRightHandFingers}");

        var missing = new List<BodyNode>();
        GetMissingBipedBones(missing);
        if (missing.Count > 0)
        {
            LumoraLogger.Log($"  Missing bones: {string.Join(", ", missing)}");
        }

        foreach (var kvp in Bones)
        {
            LumoraLogger.Log($"  {kvp.Key}: {kvp.Value?.Target?.SlotName.Value ?? "null"}");
        }
    }
}

/// <summary>
/// A simple wrapper for slot references in dictionaries.
/// </summary>
public class SlotRef
{
    public Slot Target { get; set; }

    public SlotRef(Slot target = null!)
    {
        Target = target;
    }
}

/// <summary>
/// A dictionary that maps BodyNode keys to Slot values.
/// Simplified version that doesn't use SyncRef (which requires IWorldElement).
/// </summary>
public class SyncRefDictionary<TKey, TValue> : System.Collections.Generic.IEnumerable<KeyValuePair<TKey, SlotRef>>
    where TKey : notnull
    where TValue : Slot
{
    private readonly Dictionary<TKey, SlotRef> _dict = new();
    private readonly Component _owner;

    public SyncRefDictionary(Component owner)
    {
        _owner = owner;
    }

    public int Count => _dict.Count;

    public void Add(TKey key, TValue value)
    {
        if (_dict.TryGetValue(key, out var existing))
        {
            existing.Target = value;
        }
        else
        {
            _dict[key] = new SlotRef(value);
        }
    }

    public bool Remove(TKey key)
    {
        return _dict.Remove(key);
    }

    public bool ContainsKey(TKey key)
    {
        return _dict.ContainsKey(key);
    }

    public bool TryGetTarget(TKey key, out TValue target)
    {
        if (_dict.TryGetValue(key, out var slotRef) && slotRef.Target is TValue typedTarget)
        {
            target = typedTarget;
            return target != null;
        }
        target = default!;
        return false;
    }

    public TValue this[TKey key]
    {
        get => (_dict.TryGetValue(key, out var sr) && sr.Target is TValue t ? t : default) ?? null!;
        set => Add(key, value);
    }

    public System.Collections.Generic.IEnumerator<KeyValuePair<TKey, SlotRef>> GetEnumerator()
    {
        return _dict.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

