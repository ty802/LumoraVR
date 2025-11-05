using System;
using Godot;

namespace Aquamarine.Source.Core;

/// <summary>
/// Base class for all Components that can be attached to Slots.
/// Components provide behavior and functionality to Slots.
/// 
/// </summary>
public abstract partial class Component : IWorldElement
{
	private static ulong _nextRefID = 1;
	private bool _isDestroyed;

	/// <summary>
	/// Unique reference ID for network synchronization.
	/// </summary>
	public ulong RefID { get; private set; }

	/// <summary>
	/// The Slot this Component is attached to.
	/// </summary>
	public Slot Slot { get; private set; }

	/// <summary>
	/// The World this Component belongs to.
	/// </summary>
	public World World => Slot?.World;

	/// <summary>
	/// Whether this Component has been destroyed.
	/// </summary>
	public bool IsDestroyed => _isDestroyed;

	/// <summary>
	/// Whether this Component has been initialized.
	/// </summary>
	public bool IsInitialized { get; private set; }

	/// <summary>
	/// Whether this Component has been started.
	/// </summary>
	public bool IsStarted { get; private set; }

	/// <summary>
	/// Whether this Component is enabled and should update.
	/// </summary>
	public Sync<bool> Enabled { get; private set; }

	/// <summary>
	/// Display name for this Component type.
	/// </summary>
	public virtual string ComponentName => GetType().Name;

	protected Component()
	{
		RefID = _nextRefID++;
		Enabled = new Sync<bool>(null, true); // Will be assigned proper owner on Initialize
	}

	/// <summary>
	/// Initialize this Component with a Slot.
	/// Called when the Component is created.
	/// </summary>
	internal void Initialize(Slot slot)
	{
		Slot = slot;
		Enabled = new Sync<bool>(this, true);
		IsInitialized = true;
	}

	/// <summary>
	/// Called when the Component is first created (before any other lifecycle methods).
	/// Use this to initialize references and set up the Component.
	/// </summary>
	public virtual void OnAwake() { }

	/// <summary>
	/// Called after OnAwake, before OnStart.
	/// Use this for initialization that requires other Components to be awake.
	/// </summary>
	public virtual void OnInit() { }

	/// <summary>
	/// Called when the Component is ready to start functioning.
	/// All Components have been initialized at this point.
	/// </summary>
	public virtual void OnStart()
	{
		IsStarted = true;
	}

	/// <summary>
	/// Called every frame when the Component is enabled.
	/// </summary>
	/// <param name="delta">Time elapsed since the last frame in seconds.</param>
	public virtual void OnUpdate(float delta) { }

	/// <summary>
	/// Called when the Component is being destroyed.
	/// Clean up resources and references here.
	/// </summary>
	public virtual void OnDestroy()
	{
		_isDestroyed = true;
	}

	/// <summary>
	/// Destroy this Component and remove it from its Slot.
	/// </summary>
	public void Destroy()
	{
		if (_isDestroyed) return;

		OnDestroy();
		Slot?.RemoveComponent(this);
	}

	/// <summary>
	/// Mark this Component as having changed (for networking).
	/// </summary>
	protected void MarkDirty()
	{
		World?.MarkElementDirty(this);
	}
}
