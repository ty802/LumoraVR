// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// Datamodel wrapper around one simulation emitter. Attach anywhere - the component's OWN slot is the
// emission space, so an emitter can ride a hand, a wheel or a door and its shape follows - point
// System at the target system, set a Rate.
//
// Multiple emitters feed one system. While ANY emitter component is registered the system's built-in
// disc emission is switched off, so adding a first emitter replaces the default spray rather than
// stacking on top of it. -xlinka
[ComponentCategory("Rendering/Particles")]
public abstract class ParticleEmitterBase : Component
{
    public readonly SyncRef<ParticleSystem> System = new();

    // Particles per second.
    public readonly Sync<float> Rate = new();

    // One-shot burst emitted when the component starts.
    public readonly Sync<int> BurstOnStart = new();

    // Blend every emitted direction toward a random one. 1 is fully random.
    public readonly Sync<float> RandomDirectionWeight = new();

    private ParticleSystem? _registered;
    private float _accumulator;

    // Null while the component is unbound.
    internal ParticleSimEmitter? SimEmitter { get; set; }

    internal abstract ParticleSimEmitter CreateSimEmitter();

    // toSystemSpace takes a point from this component's slot into the owning system's simulation
    // space.
    internal abstract void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace);

    public override void OnInit()
    {
        base.OnInit();
        Rate.Value = 10f;
    }

    public override void OnStart()
    {
        base.OnStart();
        // Both 'System' (our SyncRef field) and 'Math' (Lumora.Core.Math) are shadowed here, hence no Clamp/Max.
        if (BurstOnStart.Value > 0)
            _accumulator += BurstOnStart.Value;
        Reregister();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Reregister();
    }

    private void Reregister()
    {
        var target = System.Target;
        if (ReferenceEquals(target, _registered))
            return;
        _registered?.UnregisterEmitter(this);
        _registered = target;
        _registered?.RegisterEmitter(this);
    }

    public override void OnDestroy()
    {
        _registered?.UnregisterEmitter(this);
        _registered = null;
        base.OnDestroy();
    }

    // The fractional remainder is carried, so a rate of 0.5 emits one particle every other second
    // instead of rounding down to nothing forever.
    public int ConsumeEmissionCount(float dt)
    {
        if (!Enabled || Slot?.IsActive != true)
        {
            _accumulator = 0f;
            return 0;
        }
        float rate = Rate.Value;
        if (rate > 0f)
            _accumulator += rate * dt;
        int count = (int)_accumulator;
        _accumulator -= count;
        return count;
    }
}
