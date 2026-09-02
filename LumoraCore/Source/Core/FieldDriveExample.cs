// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Examples;

// Reference for how a drive is wired up. Illustrative, not production code.
//
// A drive is a MEMBER. Declare it readonly on the worker that does the driving and let the normal
// member machinery own it: it gets a RefID, its target replicates, and it comes back from a save. The
// only thing written in code per peer is the value being pushed.
public class FieldDriveExample
{
    // The shape every driver should have: declared links, a default target assigned only when nothing
    // already named one, and a push per update.
    [ComponentCategory("Hidden")]
    public class IKDriverComponent : Component
    {
        // Declared members. Discovered by WorkerInitializer, initialized with the component.
        public readonly FieldDrive<float3> PositionDrive = new();
        public readonly FieldDrive<floatQ> RotationDrive = new();

        public override void OnStart()
        {
            base.OnStart();

            // ShouldApplyDefault is the guard. It refuses when the link already names a field, and
            // also when the save it came back from held an empty link on purpose - a drive someone
            // deliberately broke stays broken instead of being rebuilt on every load.
            if (PositionDrive.ShouldApplyDefault)
                PositionDrive.DriveTarget(Slot.LocalPosition);
            if (RotationDrive.ShouldApplyDefault)
                RotationDrive.DriveTarget(Slot.LocalRotation);
        }

        public override void OnUpdate(float delta)
        {
            base.OnUpdate(delta);

            // SetValue is a no-op unless the link was granted, so there is nothing to check first.
            PositionDrive.SetValue(ComputeIKPosition());
            RotationDrive.SetValue(ComputeIKRotation());
        }

        private float3 ComputeIKPosition() => float3.Zero;

        private floatQ ComputeIKRotation() => floatQ.Identity;
    }

    public static void DriveFromSource(FieldDrive<float3> drive, Sync<float3> bonePosition, Func<float3> source)
    {
        drive.DriveFrom(source);
        if (drive.ShouldApplyDefault)
            drive.DriveTarget(bonePosition);

        // Then once per frame:
        drive.UpdateDrive();
    }

    // Drives whose count and targets are discovered per peer at runtime - a rig's finger bones, say -
    // have no business replicating. Those are built detached with a local RefID and disposed by hand.
    public static FieldDrive<float3> DetachedDrive(Component owner, Sync<float3> target, Func<float3> source)
    {
        var drive = FieldDrive<float3>.CreateLocal(owner);
        drive.LocalValueOnly = true;
        drive.DriveFrom(source);
        drive.DriveTarget(target);
        return drive;
    }

    // Reading link state off a field. A driven value is DERIVED; releasing the link hands the field back.
    public static void InspectAndRelease(Sync<float3> position)
    {
        if (position.IsDriven)
        {
            Console.WriteLine($"Driven by: {position.ActiveLink?.ParentHierarchyToString()}");
            Console.WriteLine($"Is hooked: {position.IsHooked}");

            // Clears the driver's ref, which replicates and persists like any other reference write.
            position.ActiveLink?.ReleaseLink();
        }

        position.Value = new float3(1, 2, 3);
    }
}
