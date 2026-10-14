// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Lumora.Core.Logging;

namespace Lumora.Core.Components.Avatar;

// Wear a garment that was rigged to its own copy of the armature.
//
// Drop the avatar in one slot and the clothing in the other, press Link, and every bone in the
// garment whose name matches a bone on the avatar is reparented onto that bone. The garment keeps its
// own skeleton and its own skinning; those bones now simply ride the avatar's, so the mesh deforms
// with the body without anything being re-rigged or re-weighted.
//
// Bone-for-bone, not garment-root-to-hips: parenting the whole garment under one bone only works for
// something rigid. A skirt rigged to the legs has to have ITS legs follow YOUR legs, and that is one
// reparent per matched bone. The hierarchy comes out the same shape because the avatar's hierarchy
// mirrors the garment's - each pair is one bone deep, so nothing is transformed twice.
//
// Names are matched loosely because exporters mangle them: "mixamorig:Hips", "Armature|Hips" and
// "Hips.001" all have to land on "Hips". Anything unmatched is left exactly where it is and counted,
// so a garment that only half fits says so instead of half moving. -xlinka
[ComponentCategory("Avatar")]
public class ArmatureLink : Component
{
    // The worn avatar. Its bones are the ones being matched against.
    public readonly SyncRef<Slot> Avatar;

    // The garment root. Everything under it is searched for bones to link.
    public readonly SyncRef<Slot> Clothing;

    // Keep each bone's world pose when it is reparented. On for a garment authored on this avatar,
    // which is the normal case: the bones already coincide, so preserving the pose changes nothing
    // visible. Off snaps every garment bone exactly onto its avatar bone, which is what you want when
    // the garment was authored on a different rest pose and you would rather it conform than float.
    public readonly Sync<bool> KeepPose;

    // Garments that tag their bones with their own name ("Hoodie_Hips", "Hips_Hoodie") match once the tag is
    // taken off: a bone that starts with NamePrefix or ends with NameSuffix is matched without it.
    public readonly Sync<string> NamePrefix;
    public readonly Sync<string> NameSuffix;

    // Readouts, so the result is visible without digging through the hierarchy.
    public readonly Sync<int> LinkedBones;
    public readonly Sync<int> UnmatchedBones;

    public ArmatureLink()
    {
        Avatar = new SyncRef<Slot>(this);
        Clothing = new SyncRef<Slot>(this);
        KeepPose = new Sync<bool>(this, true);
        NamePrefix = new Sync<string>(this, "");
        NameSuffix = new Sync<string>(this, "");
        LinkedBones = new Sync<int>(this, 0);
        UnmatchedBones = new Sync<int>(this, 0);
    }

    [SyncMethod]
    public void Link()
    {
        var avatar = Avatar.Target;
        var clothing = Clothing.Target;
        if (avatar == null || avatar.IsDestroyed || clothing == null || clothing.IsDestroyed)
        {
            Logger.Warn("ArmatureLink: set both an avatar and a clothing slot before linking");
            return;
        }
        if (clothing.IsDescendantOf(avatar) && clothing != avatar)
        {
            // Already worn. Re-linking is fine, but say so - a second press is usually a mistake.
            Logger.Debug("ArmatureLink: clothing is already under the avatar, re-linking its bones");
        }

        // The avatar's bones by loose name. First one wins: a rig with duplicates (a mirrored prop
        // reusing a bone name) would otherwise scatter the garment across both.
        // A leaf (a fingertip, a prop socket) has nothing to match; the clothing then hangs on it whole.
        var byName = new Dictionary<string, Slot>(StringComparer.Ordinal);
        CollectBones(avatar, byName);

        // Snapshot before reparenting: moving a slot mid-walk would have us descend into the avatar.
        var candidates = new List<Slot>();
        CollectDescendants(clothing, candidates);

        int linked = 0;
        int unmatched = 0;
        bool keepPose = KeepPose.Value;

        foreach (var bone in candidates)
        {
            if (bone == null || bone.IsDestroyed)
                continue;

            var key = NormalizeBoneName(Untagged(bone.Name.Value));
            if (key.Length == 0 || !byName.TryGetValue(key, out var target) || target == null || target.IsDestroyed)
            {
                unmatched++;
                continue;
            }
            // A bone cannot be parented to itself or into its own subtree.
            if (ReferenceEquals(bone, target) || target.IsDescendantOf(bone))
            {
                unmatched++;
                continue;
            }

            bone.SetParent(target, preserveGlobalTransform: keepPose);
            if (!keepPose)
            {
                bone.LocalPosition.Value = Math.float3.Zero;
                bone.LocalRotation.Value = Math.floatQ.Identity;
            }
            linked++;
        }

        // Whatever did not match - the mesh slots, the garment's own root - travels with the avatar so
        // the garment moves as one thing rather than being left behind at the origin.
        if (!clothing.IsDestroyed && !clothing.IsDescendantOf(avatar))
            clothing.SetParent(avatar, preserveGlobalTransform: true);

        LinkedBones.Value = linked;
        if (linked > 0)
            Lumora.Core.Logging.Logger.Debug($"POSEWRITE ArmatureLink: relinked {linked} bone(s), keepPose={keepPose}, unmatched={unmatched}");
        UnmatchedBones.Value = unmatched;
        Logger.Debug($"ArmatureLink: linked {linked} bones, {unmatched} unmatched");
    }

    private string? Untagged(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var prefix = NamePrefix.Value;
        var suffix = NameSuffix.Value;
        if (!string.IsNullOrEmpty(prefix) && name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
            name = name[prefix.Length..];
        if (!string.IsNullOrEmpty(suffix) && name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            name = name[..^suffix.Length];
        return name;
    }

    private static void CollectBones(Slot root, Dictionary<string, Slot> into)
    {
        foreach (var child in root.Children)
        {
            var key = NormalizeBoneName(child.Name.Value);
            if (key.Length > 0 && !into.ContainsKey(key))
                into[key] = child;
            CollectBones(child, into);
        }
    }

    private static void CollectDescendants(Slot root, List<Slot> into)
    {
        foreach (var child in root.Children)
        {
            into.Add(child);
            CollectDescendants(child, into);
        }
    }

    // Shared with skeleton binding so a rig that links here also binds there. See BoneNameMatcher.
    internal static string NormalizeBoneName(string? name) => Lumora.Core.Assets.BoneNameMatcher.Normalize(name);
}
