// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Assets.Interop.Unity;

// What the package's build-time tools would have done to the avatar at upload, gathered as its setup is read,
// so the import can do it instead: VRCFury's merged controllers, upload-time actions, shape links, first person
// keeps and constraint moves, and Modular Avatar's whole kit. Kept beside the candidate rather than on it.
//
// Modular Avatar ships no scripts in a package, only its components' saved fields, so each is told by its
// script's id, which the tool never changes, and failing that by the fields only it saves. -xlinka
public static class UnityBuildTools
{
    public enum Ma
    {
        MergeArmature, BoneProxy, MergeAnimator, MenuInstaller, MenuItem, MenuGroup, Parameters, ObjectToggle,
        ShapeChanger, BlendshapeSync, WorldFixedObject, VisibleHeadAccessory, ScaleAdjuster, ReplaceObject,
    }

    public sealed class Parts
    {
        public readonly List<UnityComponentRef> FullControllers = new();
        public readonly List<UnityComponentRef> UploadActions = new();
        public readonly List<UnityComponentRef> Deletions = new();
        public readonly List<UnityComponentRef> ShapeLinks = new();
        public readonly List<UnityComponentRef> FirstPerson = new();
        public readonly List<UnityComponentRef> Retargets = new();
        public readonly List<UnityComponentRef> Icons = new();
        public readonly List<UnityComponentRef> GlobalColliders = new();
        public readonly List<(Ma Kind, UnityComponentRef Part)> ModularAvatar = new();
        // Modular Avatar's menu items with their paths, worked out with the menu plan.
        public readonly List<UnityModularAvatarPlan.Item> Items = new();
        // Icon images the toggles and set-icons name, by GUID, as the menu plan keeps them.
        public readonly Dictionary<string, (string File, string Extension)> IconFiles = new(StringComparer.OrdinalIgnoreCase);
        public bool Any => FullControllers.Count + UploadActions.Count + Deletions.Count + ShapeLinks.Count + FirstPerson.Count
            + Retargets.Count + Icons.Count + GlobalColliders.Count + ModularAvatar.Count > 0;
    }

    private static readonly ConditionalWeakTable<UnityImportCandidate, Parts> Table = new();

    public static Parts Of(UnityImportCandidate candidate) => Table.GetValue(candidate, _ => new Parts());

    // Build-time tidying that changes nothing anyone sees here.
    private static readonly HashSet<string> Pointless = new(StringComparer.Ordinal)
    {
        "UnlimitedParameters", "AnchorOverrideFix", "AnchorOverrideFix2", "BoundingBoxFix", "BoundingBoxFix2", "TPSComponent",
    };

    // A VRCFury feature the menu and switch code does not take.
    internal static bool Collect(UnityImportCandidate candidate, UnityComponentRef part)
    {
        if (Pointless.Contains(part.Feature))
            return true;
        var parts = Of(candidate);
        switch (part.Feature)
        {
            case "FullController": parts.FullControllers.Add(part); return true;
            case "ApplyDuringUpload": parts.UploadActions.Add(part); return true;
            case "DeleteDuringUpload": parts.Deletions.Add(part); return true;
            case "BlendShapeLink": parts.ShapeLinks.Add(part); return true;
            case "ShowInFirstPerson": parts.FirstPerson.Add(part); return true;
            case "ConstraintRetarget": parts.Retargets.Add(part); return true;
            case "SetIcon": parts.Icons.Add(part); return true;
            default: return false;
        }
    }

    private static readonly Dictionary<string, Ma> Scripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2df373bf91cf30b4bbd495e11cb1a2ec"] = Ma.MergeArmature,
        ["42581d8044b64899834d3d515ab3a144"] = Ma.BoneProxy,
        ["1bb122659f724ebf85fe095ac02dc339"] = Ma.MergeAnimator,
        ["7ef83cb0c23d4d7c9d41021e544a1978"] = Ma.MenuInstaller,
        ["3b29d45007c5493d926d2cd45a489529"] = Ma.MenuItem,
        ["97e46a47dd8a425eb4ce9411defe313d"] = Ma.MenuGroup,
        ["71a96d4ea0c344f39e277d82035bf9bd"] = Ma.Parameters,
        ["a162bb8ec7e24a5abcf457887f1df3fa"] = Ma.ObjectToggle,
        ["2db441f589c3407bb6fb5f02ff8ab541"] = Ma.ShapeChanger,
        ["6fd7cab7d93b403280f2f9da978d8a4f"] = Ma.BlendshapeSync,
        ["0e2d9f1d69e34b92a96e6cc162770fad"] = Ma.WorldFixedObject,
        ["33dac8cfeaeb4c399ddd90597f849f70"] = Ma.VisibleHeadAccessory,
        ["09a660aa9d4e47d992adcac5a05dd808"] = Ma.ScaleAdjuster,
        ["7e949680c0864ee7b441d9b2c93b890b"] = Ma.ReplaceObject,
    };

    private const string GlobalColliderScript = "8028d33728ef49f5b9d44b564480aeff";

    // A Modular Avatar component, or VRCFury's global collider (a script of its own, not a feature block).
    internal static bool Collect(UnityImportCandidate candidate, UnityDocument d, UnityNodeRef node)
    {
        if (d.ClassId != 114)
            return false;
        var script = d["m_Script"]?.Ref.Guid ?? "";
        bool enabled = (d["m_Enabled"]?.Int(1) ?? 1) != 0;
        var part = new UnityComponentRef { FileId = d.FileId, Node = node, Doc = d, Enabled = enabled };
        if (string.Equals(script, GlobalColliderScript, StringComparison.OrdinalIgnoreCase)
            || d["content"] == null && d["vrcfuryVersion"] != null && d["radius"] != null && d["height"] != null && d["rootTransform"] != null)
        {
            part.Feature = "GlobalCollider";
            if (enabled)
                Of(candidate).GlobalColliders.Add(part);
            return true;
        }
        Ma? kind = Scripts.TryGetValue(script, out var known) ? known : ByFields(d);
        if (kind == null)
            return false;
        // A switched-off Modular Avatar component does nothing at upload, except the reactive ones, which
        // are only ever off to be switched on.
        if (!enabled && kind is not (Ma.ObjectToggle or Ma.ShapeChanger))
            return true;
        part.Feature = kind.Value.ToString();
        Of(candidate).ModularAvatar.Add((kind.Value, part));
        return true;
    }

    // Copies that left their scripts' ids behind: each component by the fields only it saves.
    private static Ma? ByFields(UnityDocument d)
    {
        if (d["mergeTarget"] != null && d["prefix"] != null && d["suffix"] != null) return Ma.MergeArmature;
        if (d["boneReference"] != null && d["subPath"] != null && d["attachmentMode"] != null) return Ma.BoneProxy;
        if (d["animator"] != null && d["layerType"] != null && d["pathMode"] != null) return Ma.MergeAnimator;
        if (d["menuToAppend"] != null && d["installTargetMenu"] != null) return Ma.MenuInstaller;
        if (d["Control"] != null && d["MenuSource"] != null) return Ma.MenuItem;
        if (d["parameters"] is UnitySeq { Items.Count: > 0 } list && list.Items[0]["nameOrPrefix"] != null) return Ma.Parameters;
        if (d["m_objects"] != null && d["m_inverted"] != null) return Ma.ObjectToggle;
        if (d["m_shapes"] != null && d["m_inverted"] != null) return Ma.ShapeChanger;
        if (d["Bindings"] is UnitySeq { Items.Count: > 0 } bindings && bindings.Items[0]["ReferenceMesh"] != null) return Ma.BlendshapeSync;
        if (d["targetObject"]?["referencePath"] != null) return Ma.ReplaceObject;
        if (d["m_Scale"] != null && d["legacyScaleProxy"] != null) return Ma.ScaleAdjuster;
        return null;
    }
}
