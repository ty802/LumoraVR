// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

// Trash system for temporarily holding deleted objects until authority confirms deletion.
// Prevents permanent data loss from rejected delete operations.
public class TrashBin
{
	private readonly Dictionary<RefID, TrashEntry> _trashedElements = new();
	private readonly World _world;

    public double TrashRetentionTime { get; set; } = 60.0; // 1 minute default

    public TrashBin(World world)
    {
        _world = world;
    }

    public void MoveToTrash(IWorldElement element)
    {
        if (element == null || element.IsDestroyed)
            return;

		var entry = new TrashEntry
		{
			Element = element,
			TrashedTime = _world.TotalTime,
			RefID = element.ReferenceID
		};

		_trashedElements[element.ReferenceID] = entry;

		// Mark as destroyed to prevent further use, but don't actually destroy yet
		// This allows recovery if deletion is rejected
		LumoraLogger.Debug($"Moved element {element.ReferenceID} to trash");
	}

	// For a deletion the authority rejected.
	public bool RestoreFromTrash(RefID refID)
	{
		if (!_trashedElements.TryGetValue(refID, out var entry))
		{
			LumoraLogger.Warn($"Cannot restore {refID} - not in trash");
			return false;
		}

        _trashedElements.Remove(refID);

        if (entry.Element is Slot slot)
        {
            _world.RegisterSlot(slot);
        }
        else if (entry.Element is Component component)
        {
            _world.RegisterComponent(component);
        }

        LumoraLogger.Log($"Restored element {refID} from trash");
        return true;
    }

	// Authority confirmed the deletion.
	public void PermanentlyDelete(RefID refID)
	{
		if (!_trashedElements.TryGetValue(refID, out var entry))
		{
			return; // Already deleted
		}

        _trashedElements.Remove(refID);

        if (entry.Element is Slot slot)
        {
            slot.Destroy();
        }
        else if (entry.Element is Component component)
        {
            component.Destroy();
        }

        LumoraLogger.Debug($"Permanently deleted element {refID}");
    }

	// Call periodically from World._Process.
	public void Update()
	{
		var currentTime = _world.TotalTime;
		var toRemove = new List<RefID>();

        foreach (var kvp in _trashedElements)
        {
            var entry = kvp.Value;
            var timeInTrash = currentTime - entry.TrashedTime;

            if (timeInTrash > TrashRetentionTime)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var refID in toRemove)
        {
            PermanentlyDelete(refID);
            LumoraLogger.Debug($"Auto-deleted expired trash entry {refID}");
        }
    }

	public bool IsInTrash(RefID refID)
	{
		return _trashedElements.ContainsKey(refID);
	}

    public void Clear()
    {
        foreach (var entry in _trashedElements.Values)
        {
            if (entry.Element is Slot slot)
            {
                slot.Destroy();
            }
            else if (entry.Element is Component component)
            {
                component.Destroy();
            }
        }

        _trashedElements.Clear();
        LumoraLogger.Log("Cleared trash bin");
    }

    public (int count, int slots, int components) GetStatistics()
    {
        int slots = 0;
        int components = 0;

        foreach (var entry in _trashedElements.Values)
        {
            if (entry.Element is Slot) slots++;
            else if (entry.Element is Component) components++;
        }

        return (_trashedElements.Count, slots, components);
    }
}

internal class TrashEntry
{
	public IWorldElement Element { get; set; } = null!;
	public RefID RefID { get; set; }
	public double TrashedTime { get; set; }
}

