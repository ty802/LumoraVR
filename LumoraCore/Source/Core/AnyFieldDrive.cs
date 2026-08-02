// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

// A drive whose target field type is not known at compile time.
//
// FieldDrive{T} is the right tool almost everywhere: the driving worker knows it drives
// a float3, so it declares one. But a driver whose channel list is DATA - an animation clip decides
// at load time that track 7 is a floatQ and track 8 is a float - cannot declare a typed member per
// channel, and cannot put typed drives in one list either because a list holds a single element type.
//
// So this claims the field the same way a typed drive does (the target is a replicated, persisted
// ref; the link manager grants it; the field reports as driven and drops out of value sync) and
// leaves the WRITE to the caller, which recovers the type once and caches a typed writer. The
// alternative - going through IField.BoxedValue - would box on every field on every frame, which for
// a skeletal clip is a few thousand allocations per frame. -xlinka
public class AnyFieldDrive : LinkBase<IField>
{
    public AnyFieldDrive()
    {
    }

    public AnyFieldDrive(IWorldElement? owner) : base(owner)
    {
    }

    public override bool IsDriving => true;

    public override bool IsHooking => false;

    public override bool IsModificationAllowed => true;

    public IField? Field => IsLinkValid ? Target : null;

    // Null when nothing is resolved.
    public System.Type? DrivenType => Target?.ValueType;

    // Passing null releases the current target.
    public void DriveTarget(IField? target)
    {
        Target = target!;
    }

    // The member itself stays alive, it is disposed with its owner.
    public void Release()
    {
        ReleaseLink();
    }
}
