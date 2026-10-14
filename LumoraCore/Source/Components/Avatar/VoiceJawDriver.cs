// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// A mouth that only opens and shuts, as wide as the wearer talks loud: a jaw bone turned from its shut pose
// toward its open one (rotations in the bone's parent), one shape that opens the mouth, or both. For faces
// with no shape per sound. Every peer works it from the voice it hears and the writes stay local, like the
// visemes. -xlinka
[ComponentCategory("Users/Avatar/Face")]
public sealed class VoiceJawDriver : Component
{
    public readonly SyncRef<LipSyncAnalyzer> Source = new();
    public readonly SyncRef<Slot> Jaw = new();
    public readonly Sync<floatQ> ShutRotation = new();
    public readonly Sync<floatQ> OpenRotation = new();
    public readonly SyncRef<SkinnedMeshRenderer> ShapeMesh = new();
    public readonly Sync<string> OpenShape = new();

    private Slot? _jaw;
    private floatQ _rest = floatQ.Identity;
    private SkinnedMeshRenderer? _mesh;
    private int _shape = -1;
    private bool _resolved;
    private float _retry;
    private readonly AvatarFaceWearer _wearer = new();

    // How open the mouth is this frame, 0..1.
    public float Open { get; private set; }

    public override void OnInit()
    {
        base.OnInit();
        ShutRotation.Value = floatQ.Identity;
        OpenRotation.Value = floatQ.Identity;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (_wearer.Update(Slot))
            Release();
        if (_wearer.Current == null)
            return;

        if (FaceHold.MouthHeld(Slot))
        {
            Release();
            return;
        }

        if (!_resolved)
        {
            _retry -= delta;
            if (_retry <= 0f)
                Resolve();
        }

        if ((Source.Target == null || Source.Target.IsDestroyed)
            && (Slot.GetComponentInChildren<LipSyncAnalyzer>() ?? Slot.GetComponent<LipSyncAnalyzer>()) is { } found)
            Source.Target = found;
        float open = Source.Target is { IsDestroyed: false } source ? source.Level : 0f;
        Open = open;

        if (_jaw != null)
        {
            if (_jaw.IsDestroyed)
            {
                _jaw = null;
                _resolved = false;
            }
            else
            {
                _jaw.LocalRotation.SetValueSilently(floatQ.Slerp(ShutRotation.Value, OpenRotation.Value, open), change: true);
            }
        }

        if (_mesh != null && _shape >= 0)
        {
            if (_mesh.IsDestroyed || !_mesh.OwnsBlendShape(_shape, this))
            {
                _mesh = null;
                _shape = -1;
                _resolved = false;
            }
            else
            {
                _mesh.DriveBlendShapeWeight(_shape, open);
            }
        }
    }

    private void Resolve()
    {
        Release();
        if (Jaw.Target is { IsDestroyed: false } jaw)
        {
            _jaw = jaw;
            _rest = jaw.LocalRotation.Value;
        }
        var mesh = ShapeMesh.Target;
        int index = FaceShapes.Find(mesh, OpenShape.Value);
        if (index >= 0 && mesh!.ClaimBlendShape(index, this, SkinnedMeshRenderer.BlendShapePriorityViseme))
        {
            _mesh = mesh;
            _shape = index;
        }
        // A named shape on a mesh still loading is looked for again in a moment.
        _resolved = string.IsNullOrEmpty(OpenShape.Value) || _shape >= 0 || mesh == null || mesh.IsDestroyed;
        _retry = 1f;
    }

    // The jaw back where it rests and the shape back to its own weight.
    private void Release()
    {
        if (_jaw != null && !_jaw.IsDestroyed)
            _jaw.LocalRotation.SetValueSilently(_rest, change: true);
        if (_mesh != null && !_mesh.IsDestroyed && _shape >= 0)
            _mesh.ReleaseBlendShape(_shape, this);
        _jaw = null;
        _mesh = null;
        _shape = -1;
        _resolved = false;
        _retry = 0f;
        Open = 0f;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveChanged += OnSlotActiveChanged;
    }

    private void OnSlotActiveChanged(Slot slot)
    {
        if (Slot != null && !Slot.IsActive)
            Release();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        Release();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        Release();
        base.OnDestroy();
    }
}
