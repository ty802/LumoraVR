// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Godot.Hooks;

// Pattern: LumoraCore Component (data) -> Hook (bridge) -> Godot Node/Resource (implementation)
public abstract class Hook<D> : IHook<D> where D : IImplementable
{
	public D Owner { get; private set; } = default!;

	protected World World => Owner?.World!;

	IImplementable IHook.Owner => Owner;

	public void AssignOwner(IImplementable owner)
	{
		Owner = (D)owner;
	}

	public void RemoveOwner()
	{
		Owner = default(D)!;
	}

	public abstract void Initialize();

	public abstract void ApplyChanges();

	public abstract void Destroy(bool destroyingWorld);

	public virtual void Dispose()
	{
		Destroy(false);
	}
}

