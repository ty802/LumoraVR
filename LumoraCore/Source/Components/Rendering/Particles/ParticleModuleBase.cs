// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// Datamodel wrapper around one simulation module. The component owns synced tunables and nothing
// else: it creates a plain simulation module, registers it with the system it points at, and pushes
// its field values into that module each frame. No per-particle work happens in a component.
//
// Registration order in the system decides pipeline order within a phase, so attaching two forces
// gives the same result every time rather than depending on which one the datamodel happened to
// enumerate first. -xlinka
[ComponentCategory("Rendering/Particles")]
public abstract class ParticleModuleBase : Component
{
    public readonly SyncRef<ParticleSystem> System = new();

    private ParticleSystem? _registered;

    // Null while the component is unbound.
    internal ParticleSimModule? SimModule { get; set; }

    // True when this module drives colour or size, which retires the system's built-in envelope.
    internal virtual bool OverridesAppearance => false;

    // True when this module needs the rotation integrator running.
    internal virtual bool RequiresRotationSimulation => false;

    // True when this module puts a meaningful rotation on particles WITHOUT needing per-frame
    // integration - a birth-time roll, for instance. The renderer has to be told to honour rotations
    // either way, or a static roll is computed and then quietly thrown away at draw time. -xlinka
    internal virtual bool ProducesRotation => RequiresRotationSimulation;

    internal abstract ParticleSimModule CreateSimModule();

    internal abstract void PushParameters(ParticleSimModule module);

    public override void OnStart()
    {
        base.OnStart();
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
        _registered?.UnregisterModule(this);
        _registered = target;
        _registered?.RegisterModule(this);
    }

    public override void OnDestroy()
    {
        _registered?.UnregisterModule(this);
        _registered = null;
        base.OnDestroy();
    }
}
