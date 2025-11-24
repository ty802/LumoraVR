using System.Collections.Generic;

namespace Lumora.Core;

/// <summary>
/// Manages hook updates for components.
/// Hooks are updated in batches after component changes are processed.
/// </summary>
public class UpdateManager
{
	private World _world;
	private HashSet<IImplementable> _pendingHookUpdates = new HashSet<IImplementable>();

	public UpdateManager(World world)
	{
		_world = world;
	}

	/// <summary>
	/// Register a component for hook update.
	/// Called when component properties change.
	/// </summary>
	public void RegisterHookUpdate(IImplementable component)
	{
		if (component != null && component.Hook != null)
		{
			_pendingHookUpdates.Add(component);
		}
	}

	/// <summary>
	/// Process all pending hook updates.
	/// Called by the world renderer after component updates.
	/// </summary>
	public void ProcessHookUpdates()
	{
		if (_pendingHookUpdates.Count == 0)
			return;

		foreach (var component in _pendingHookUpdates)
		{
			if (component is ImplementableComponent<IHook> impl)
			{
				impl.UpdateHook();
			}
		}

		_pendingHookUpdates.Clear();
	}

	/// <summary>
	/// Clear all pending updates.
	/// </summary>
	public void Clear()
	{
		_pendingHookUpdates.Clear();
	}
}
