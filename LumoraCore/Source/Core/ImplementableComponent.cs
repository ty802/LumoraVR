// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

/// <summary>
/// Base class for components that can be implemented by engine-specific hooks.
/// Non-generic version that uses IHook.
/// </summary>
public abstract class ImplementableComponent : ImplementableComponent<IHook>
{
}

/// <summary>
/// Generic base class for components with typed hooks.
/// </summary>
public abstract class ImplementableComponent<C> : Component, IImplementable<C> where C : class, IHook
{
    /// <summary>
    /// The hook that implements this component in the engine.
    /// </summary>
    public C Hook { get; private set; } = null!;

    /// <summary>
    /// Explicit interface implementation for non-generic IImplementable.
    /// </summary>
    IHook IImplementable.Hook => Hook;

    /// <summary>
    /// Constructor - hook initialization happens after Initialize() is called.
    /// </summary>
    protected ImplementableComponent()
    {
        // Hook will be created in OnAwake() after Initialize() sets up World reference
    }

    /// <summary>
    /// Create and assign the hook for this component.
    /// Called during component construction.
    /// </summary>
    private void InitializeHook()
    {
        Hook = InstantiateHook();
        Hook?.AssignOwner(this);
    }

    /// <summary>
    /// Instantiate the hook for this component.
    /// Override this to create custom hooks.
    /// </summary>
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

    // Queue this component for hook ApplyChanges at the next ProcessHookUpdates
    // drain (end of frame, post-decode). Sync field setters call into this via
    // OnChanges, so we never fire Hook.ApplyChanges synchronously mid-decode.
    // - xlinka
    internal void RunApplyChanges()
    {
        if (Hook != null && World != null)
        {
            World.UpdateManager?.RegisterHookUpdate(this);
        }
    }

    /// <summary>
    /// Create the hook when component awakens (after Initialize() sets World).
    /// </summary>
    public override void OnAwake()
    {
        base.OnAwake();

        // Now that Initialize() has been called and World is set, create the hook
        InitializeHook();
    }

    /// <summary>
    /// Initialize the hook when the component starts.
    /// </summary>
    public override void OnStart()
    {
        base.OnStart();
        try
        {
            if (Hook != null)
            {
                Hook.Initialize();
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

    /// <summary>
    /// Destroy the hook when the component is destroyed.
    /// </summary>
    public override void OnDestroy()
    {
        DisposeHook();
        base.OnDestroy();
    }

    /// <summary>
    /// Clean up the hook.
    /// </summary>
    private void DisposeHook()
    {
        if (Hook != null)
        {
            Hook.Destroy(World?.IsDisposed ?? false);
            Hook.RemoveOwner();
            Hook = null!;
        }
    }
}
