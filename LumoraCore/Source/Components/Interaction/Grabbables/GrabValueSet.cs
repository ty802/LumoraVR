// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Utility;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Interaction;

// Writes a fixed value into a field when the object it sits on is picked up, and optionally a
// second value when it is let go. The grip equivalent of the press-a-button setters.
//
// A direct write, not a drive: the field keeps whatever it is given afterwards, so something else
// can change it mid-carry and the release value still lands. Use a drive when the field should be
// owned for the length of the grip instead.
//
// Runs on the peer whose hand did it, and the write replicates from there like any other edit -
// including being refused when that user may not touch the target. Which is the point: a grip is
// not a licence to write fields the grabber has no right to. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class GrabValueSet<T> : GrabEventBehaviour
{
    public readonly SyncRef<IField<T>> Target;

    public readonly Sync<T> GrabbedValue;

    public readonly Sync<T> ReleasedValue;

    public readonly Sync<bool> SetOnGrab;

    public readonly Sync<bool> SetOnRelease;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public GrabValueSet()
    {
        Target = new SyncRef<IField<T>>(this);
        GrabbedValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        ReleasedValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        SetOnGrab = new Sync<bool>(this, true);
        SetOnRelease = new Sync<bool>(this, false);
    }

    protected override void OnGrabbed(Grabbable carrier)
    {
        if (SetOnGrab.Value)
            Write(GrabbedValue.Value);
    }

    protected override void OnReleased(Grabbable carrier)
    {
        if (SetOnRelease.Value)
            Write(ReleasedValue.Value);
    }

    private void Write(T value)
    {
        var target = Target.Target;
        if (target != null && target.CanWrite)
            target.Value = value;
    }
}

// Points a reference field at a fixed element when the object it sits on is picked up, and
// optionally at a second element when it is let go.
[ComponentCategory("Interaction/Grabbables")]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class GrabReferenceSet<T> : GrabEventBehaviour where T : class, IWorldElement
{
    public readonly SyncRef<SyncRef<T>> Target;

    // Empty clears the field.
    public readonly SyncRef<T> GrabbedTarget;

    // Empty clears the field.
    public readonly SyncRef<T> ReleasedTarget;

    public readonly Sync<bool> SetOnGrab;

    public readonly Sync<bool> SetOnRelease;

    public GrabReferenceSet()
    {
        Target = new SyncRef<SyncRef<T>>(this);
        GrabbedTarget = new SyncRef<T>(this);
        ReleasedTarget = new SyncRef<T>(this);
        SetOnGrab = new Sync<bool>(this, true);
        SetOnRelease = new Sync<bool>(this, false);
    }

    protected override void OnGrabbed(Grabbable carrier)
    {
        if (SetOnGrab.Value)
            Write(GrabbedTarget.Target);
    }

    protected override void OnReleased(Grabbable carrier)
    {
        if (SetOnRelease.Value)
            Write(ReleasedTarget.Target);
    }

    private void Write(T? value)
    {
        var target = Target.Target;
        if (target != null && target.CanWrite)
            target.Target = value!;
    }
}
