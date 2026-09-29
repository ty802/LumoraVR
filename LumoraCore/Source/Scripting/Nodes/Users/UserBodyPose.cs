// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Where a user's body is, in world space, from their tracking and the avatar they wear. Facing is the way
// their head looks with its rise taken out, level with their user root. Feet is between their feet, tracked
// or the avatar's, else under the head on their floor. Hips is the avatar's hips bone, else partway up from
// the feet to the head. The rotations turn only about the user's up: the feet across the line between them,
// the hips where the avatar's body faces, and either one the head's facing with nothing to tell it by.
// Zeros and identity for a user with no body in the world yet.
[Node("Users/Body Pose", Capability.UsersRead)]
public sealed class UserBodyPose : ReadNode
{
    [return: Port("facing")]
    public static float3 Run(ScriptContext ctx, User? user, out floatQ facingRotation, out float3 feet, out floatQ feetRotation,
        out float3 hips, out floatQ hipsRotation)
    {
        facingRotation = feetRotation = hipsRotation = floatQ.Identity;
        feet = hips = default;
        if (!ScriptReads.Visible(ctx, user) || ScriptReads.RootOf(user) is not { } root || !ScriptReads.Visible(ctx, root.Slot))
            return default;
        var rig = ScriptBodyReads.RigOf(root);
        var facing = ScriptBodyReads.Facing(root);
        facingRotation = ScriptBodyReads.Level(root, facing);
        feet = ScriptBodyReads.Feet(root, rig, out var left, out var right);
        feetRotation = ScriptBodyReads.FeetRotation(root, left, right, facing);
        hips = ScriptBodyReads.Hips(root, rig, feet);
        hipsRotation = ScriptBodyReads.HipsRotation(root, rig, facing);
        return facing;
    }
}
