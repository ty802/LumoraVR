// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

// Seated body poses: the filters an anchor lays on a sitter's hips and feet, and the anchor's list of them.
// Every member converts by name in the generic pass, handedness included: the swing and twist axes and the
// position offset are directions and points and mirror, the rotation offset is a rotation, and the box size
// is a size and does not. The anchor already carries the half turn its references face by (ApplyAnchor),
// and the seat turns the poses its filters supply by that same offset. -xlinka
public static partial class PackageTypeMap
{
    static partial void AliasesK5(Dictionary<string, string> aliases)
    {
        aliases["AvatarPoseBoxConstraint"] = "PoseBoxClamp";
        aliases["AvatarPoseRotationConstraint"] = "PoseRotationLimit";
        aliases["AvatarPoseOffset"] = "PoseOffset";
    }
}

public static partial class PackageMemberMapper
{
    // Lands nowhere: our sockets have no simulated poses for it to tell apart, so every tracked pose is clamped.
    private const string IgnoredK5 = "\u0000k5";

    static partial void MemberAliasesK5(Dictionary<string, Dictionary<string, string>> table)
    {
        // An anchor's filter list is our seat's body poses. Each entry's node, required flag and filter list,
        // and each filter's reference and priority, land under the same names on SeatBodyPose and PoseFilterEntry.
        AliasK5(table, "Seat", "Filters", "BodyPoses");
        AliasK5(table, "PoseBoxClamp", "DefaultPoseReference", "FallbackPose");
        AliasK5(table, "PoseBoxClamp", "ProcessSimulatedPoses", IgnoredK5);
        AliasK5(table, "PoseRotationLimit", "TwistReferenceAxis", "TwistAxis");
    }

    private static void AliasK5(Dictionary<string, Dictionary<string, string>> table, string ours, string stored, string mine)
    {
        if (!table.TryGetValue(ours, out var names))
            table[ours] = names = new Dictionary<string, string>(StringComparer.Ordinal);
        names[stored] = mine;
    }

    static partial void ApplyK5(Component component, DataTreeDictionary data, Action<ISyncRef, string> queueReference,
        Action<object, string, IReadOnlyList<string>> queueReferenceList, Func<Uri, Uri?> remapUrl)
    {
    }
}
