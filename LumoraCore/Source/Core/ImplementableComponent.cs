// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

public abstract class ImplementableComponent : ImplementableComponent<IHook>
{
}

public abstract class ImplementableComponent<C> : Component, IImplementable<C> where C : class, IHook
{
    public C Hook { get; private set; } = null!;

    IHook IImplementable.Hook => Hook;

    // Hook init happens after Initialize(), not here.
    protected ImplementableComponent()
    {
    }

    private void InitializeHook()
    {
        Hook = InstantiateHook();
        Hook?.AssignOwner(this);
    }

    protected virtual C InstantiateHook()
    {
        if (World == null)
        {
            Logging.Logger.Warn($"ImplementableComponent.InstantiateHook: World is NULL for {GetType().Name}!");
            return null!;
        }

        Type componentType = GetType();
        Type hookType = World.HookTypes.GetHookType(componentType);

        if (hookType == null)
        {
            Logging.Logger.Warn($"ImplementableComponent.InstantiateHook: No hook registered for {componentType.FullName}!");
            return null!;
        }

        var hook = (C)Activator.CreateInstance(hookType)!;
        return hook!;
    }

    // Hook exists from OnAwake but only gets Initialize() at OnStart. A component
    // attached mid-update (menu build, text renderer spawn) hits ProcessHookUpdates
    // before its startup runs, so ApplyChanges must not fire until then. - xlinka
    private bool _hookReady;

    // Queue this component for hook ApplyChanges at the next ProcessHookUpdates
    // drain (end of frame, post-decode). Sync field setters call into this via
    // OnChanges, so we never fire Hook.ApplyChanges synchronously mid-decode.
    // - xlinka
    internal void RunApplyChanges()
    {
        if (Hook != null && _hookReady && World != null)
        {
            World.UpdateManager?.RegisterHookUpdate(this);
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();

        InitializeHook();
    }

    public override void OnStart()
    {
        base.OnStart();
        try
        {
            if (Hook != null)
            {
                // Its slot's platform node may still be sitting in a load scope's queue; a component
                // hook asks for that node in Initialize, so make sure it exists first.
                Slot?.EnsureHookCreated();
                Hook.Initialize();
                _hookReady = true;
                // Flush any state written before startup into the hook.
                World?.UpdateManager?.RegisterHookUpdate(this);
            }
            else
            {
                Logging.Logger.Warn($"ImplementableComponent.OnStart: Hook is NULL for {GetType().Name}!");
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Exception initializing hook for {GetType().Name}: {ex}");
            throw;
        }
    }

    // Sync field change handler. Queues the hook for ApplyChanges at the next
    // drain instead of firing it synchronously. That way hooks never see
    // partially-decoded sync state from a replication batch, and SlotHook /
    // any hook that reads multiple fields together sees a consistent snapshot.
    // - xlinka
    public override void OnChanges()
    {
        base.OnChanges();
        RunApplyChanges();
    }

    public override void OnDestroy()
    {
        DisposeHook();
        base.OnDestroy();
    }

    private void DisposeHook()
    {
        if (Hook != null)
        {
            _hookReady = false;
            Hook.Destroy(World?.IsDisposed ?? false);
            Hook.RemoveOwner();
            Hook = null!;
        }
    }
}
