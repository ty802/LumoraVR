using System;
using System.IO;
using Lumora.Core;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Network replicator for Components on a Slot.
/// Creates components by type when decoding Full/Delta batches.
/// </summary>
public class ComponentReplicator : ReplicatedDictionary<RefID, Component>
{
	private readonly Slot _slot;

	public ComponentReplicator(Slot slot)
	{
		_slot = slot ?? throw new ArgumentNullException(nameof(slot));
		OnElementRemoved += HandleComponentRemoved;
	}

	protected override void EncodeKey(BinaryWriter writer, RefID key)
	{
		writer.Write7BitEncoded((ulong)key);
	}

	protected override void EncodeElement(BinaryWriter writer, Component element)
	{
		ComponentTypeRegistry.Encode(writer, element.GetType());
	}

	protected override RefID DecodeKey(BinaryReader reader)
	{
		return new RefID(reader.Read7BitEncoded());
	}

	protected override Component DecodeElement(BinaryReader reader)
	{
		throw new InvalidOperationException("ComponentReplicator requires CreateElementWithKey - DecodeElement should not be called");
	}

	protected override Component CreateElementWithKey(RefID key, BinaryReader reader)
	{
		if (World == null)
			return null;

		var type = ComponentTypeRegistry.Decode(reader);
		if (type == null || !typeof(Component).IsAssignableFrom(type))
		{
			AquaLogger.Error($"ComponentReplicator: Unknown component type for {key}");
			return null;
		}

		return _slot.AttachComponentFromNetwork(type, key);
	}

	public void RegisterExistingComponents()
	{
		if (_slot == null)
			return;

		foreach (var component in _slot.GetAllComponents())
		{
			if (component == null)
				continue;
			if (component.ReferenceID.IsNull)
				continue;

			if (!ContainsKey(component.ReferenceID))
			{
				Add(component.ReferenceID, component, isNewlyCreated: false, skipSync: true);
			}
		}
	}

	private void HandleComponentRemoved(ReplicatedDictionary<RefID, Component> dict, RefID key, Component component)
	{
		if (component == null)
			return;

		_slot.RemoveComponentInternal(component, fromReplicator: true);
	}

	public override void Dispose()
	{
		OnElementRemoved -= HandleComponentRemoved;
		base.Dispose();
	}
}
