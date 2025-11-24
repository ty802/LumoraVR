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
	public C Hook { get; private set; }

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
		Logging.Logger.Log($"ImplementableComponent.InitializeHook: Called for {GetType().Name}");
		Logging.Logger.Log($"ImplementableComponent.InitializeHook: World = {World != null}");
		Hook = InstantiateHook();
		Logging.Logger.Log($"ImplementableComponent.InitializeHook: Hook = {Hook != null}, Type = {Hook?.GetType().Name ?? "NULL"}");
		Hook?.AssignOwner(this);
	}

	/// <summary>
	/// Instantiate the hook for this component.
	/// Override this to create custom hooks.
	/// </summary>
	protected virtual C InstantiateHook()
	{
		Logging.Logger.Log($"ImplementableComponent.InstantiateHook: Called for {GetType().Name}");
		if (World == null)
		{
			Logging.Logger.Warn($"ImplementableComponent.InstantiateHook: World is NULL for {GetType().Name}!");
			return null;
		}

		Type componentType = GetType();
		Logging.Logger.Log($"ImplementableComponent.InstantiateHook: Looking up hook for type {componentType.FullName}");
		Type hookType = World.HookTypes.GetHookType(componentType);
		Logging.Logger.Log($"ImplementableComponent.InstantiateHook: Found hook type = {hookType?.FullName ?? "NULL"}");

		if (hookType == null)
		{
			Logging.Logger.Warn($"ImplementableComponent.InstantiateHook: No hook registered for {componentType.FullName}!");
			return null;
		}

		Logging.Logger.Log($"ImplementableComponent.InstantiateHook: Creating hook instance of type {hookType.Name}");
		var hook = (C)Activator.CreateInstance(hookType);
		Logging.Logger.Log($"ImplementableComponent.InstantiateHook: Hook created successfully");
		return hook;
	}

	/// <summary>
	/// Register this component for hook update.
	/// </summary>
	internal void RunApplyChanges()
	{
		if (Hook != null && World != null)
		{
			World.UpdateManager?.RegisterHookUpdate(this);
		}
	}

	/// <summary>
	/// Apply changes from this component to the hook.
	/// </summary>
	internal void UpdateHook()
	{
		Hook?.ApplyChanges();
	}

	/// <summary>
	/// Create the hook when component awakens (after Initialize() sets World).
	/// </summary>
	public override void OnAwake()
	{
		base.OnAwake();

		// Now that Initialize() has been called and World is set, create the hook
		Logging.Logger.Log($"ImplementableComponent.OnAwake: Creating hook for {GetType().Name}");
		InitializeHook();
	}

	/// <summary>
	/// Initialize the hook when the component starts.
	/// </summary>
	public override void OnStart()
	{
		Logging.Logger.Log($"ImplementableComponent.OnStart: Called for {GetType().Name} on slot '{Slot?.SlotName?.Value ?? "NULL"}'");
		Logging.Logger.Log($"ImplementableComponent.OnStart: Hook = {Hook != null}, Hook type = {Hook?.GetType().Name ?? "NULL"}");
		base.OnStart();
		try
		{
			if (Hook != null)
			{
				Logging.Logger.Log($"ImplementableComponent.OnStart: Calling Hook.Initialize() for {GetType().Name}");
				Hook.Initialize();
				Logging.Logger.Log($"ImplementableComponent.OnStart: Hook.Initialize() completed for {GetType().Name}");
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
			Hook = null;
		}
	}
}
