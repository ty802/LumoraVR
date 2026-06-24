// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumora.Core;

/// <summary>
/// Manages component updates with bucketed ordering.
/// Provides deterministic update execution with ordered buckets.
/// </summary>
public class UpdateManager
{
    private World _world;
    private readonly Queue<IImplementable> _pendingHookUpdates = new Queue<IImplementable>();
    private readonly HashSet<IImplementable> _queuedHookUpdates = new HashSet<IImplementable>();
    private readonly object _hookUpdatesLock = new object();

    private readonly HashSet<Slot> _pendingMovedSlots = new HashSet<Slot>();
    private readonly object _movedSlotsLock = new object();
    private float _currentDeltaTime = 0f;

    // Bucketed update system for ordered execution
    private SortedDictionary<int, List<IUpdatable>> _updateBuckets = new SortedDictionary<int, List<IUpdatable>>();
    private Queue<IUpdatable> _startupQueue = new Queue<IUpdatable>();
    private Queue<IUpdatable> _destructionQueue = new Queue<IUpdatable>();
    private SortedDictionary<int, Queue<IUpdatable>> _changeBuckets = new SortedDictionary<int, Queue<IUpdatable>>();
    private int _changeUpdateIndex = 0;
    private Dictionary<IInitializable, List<IInitializable>> _initializableChildren = new Dictionary<IInitializable, List<IInitializable>>();

    // Currently updating component (for debugging)
    public IUpdatable CurrentlyUpdating { get; private set; } = null!;

    // PER-FRAME UPDATE PROFILER. Opt-in (zero overhead when off) - the host turns it on only while the debug
    // console is attached. When on, each updatable's OnUpdate is timed and the cost is aggregated BOTH by
    // component type AND by the slot it lives on, so the profiler can show "which slots are expensive", not just
    // which component types. Holds the LATEST frame only (cleared at the start of each RunUpdates), so a reader
    // gets an instantaneous sample. Engine update + telemetry read are both on the main loop, so no lock. -xlinka
    public static bool ProfilingEnabled;
    private sealed class ProfBucket { public string Name = string.Empty; public long Ticks; public int Count; }
    private readonly Dictionary<string, ProfBucket> _profByType = new(StringComparer.Ordinal);
    private readonly Dictionary<Slot, ProfBucket> _profBySlot = new();

    /// <summary>One profiler row: a name (component type or slot), its measured CPU time, and instance count.</summary>
    public readonly struct ProfileEntry
    {
        public readonly string Name;
        public readonly double Ms;
        public readonly int Count;
        public ProfileEntry(string name, double ms, int count) { Name = name; Ms = ms; Count = count; }
    }

    public UpdateManager(World world)
    {
        _world = world;
    }

    /// <summary>
    /// Gets the current delta time for this frame.
    /// Used by hooks during ApplyChanges.
    /// </summary>
    public float DeltaTime => _currentDeltaTime;

    // Registration Methods

    /// <summary>
    /// Register a component for startup (runs before first update).
    /// </summary>
    public void RegisterForStartup(IUpdatable updatable)
    {
        if (updatable != null && !updatable.IsDestroyed)
        {
            _startupQueue.Enqueue(updatable);
        }
    }

    /// <summary>
    /// Register a component for updates (runs every frame).
    /// </summary>
    public void RegisterForUpdates(IUpdatable updatable)
    {
        if (updatable == null || updatable.IsDestroyed)
            return;

        int order = updatable.UpdateOrder;
        if (!_updateBuckets.TryGetValue(order, out var bucket))
        {
            bucket = new List<IUpdatable>();
            _updateBuckets[order] = bucket;
        }
        if (!bucket.Contains(updatable))
        {
            bucket.Add(updatable);
        }
    }

    /// <summary>
    /// Unregister a component from updates.
    /// </summary>
    public void UnregisterFromUpdates(IUpdatable updatable)
    {
        if (updatable == null)
            return;

        int order = updatable.UpdateOrder;
        if (_updateBuckets.TryGetValue(order, out var bucket))
        {
            bucket.Remove(updatable);
        }
    }

    /// <summary>
    /// Called when a component's UpdateOrder changes.
    /// </summary>
    public void UpdateBucketChanged(IUpdatable updatable)
    {
        if (updatable == null)
            return;

        // Remove from all buckets and re-add to correct one
        foreach (var bucket in _updateBuckets.Values)
        {
            bucket.Remove(updatable);
        }

        if (!updatable.IsDestroyed && updatable.IsStarted)
        {
            RegisterForUpdates(updatable);
        }
    }

    /// <summary>
    /// Register a component for change application.
    /// </summary>
    public void RegisterForChanges(IUpdatable updatable)
    {
        if (updatable == null || updatable.IsDestroyed)
            return;

        int order = updatable.UpdateOrder;
        if (!_changeBuckets.TryGetValue(order, out var queue))
        {
            queue = new Queue<IUpdatable>();
            _changeBuckets[order] = queue;
        }
        queue.Enqueue(updatable);
    }

    /// <summary>
    /// Register a component for destruction.
    /// </summary>
    public void RegisterForDestruction(IUpdatable updatable)
    {
        if (updatable != null)
        {
            _destructionQueue.Enqueue(updatable);
        }
    }

    /// <summary>
    /// Register a component for hook update.
    /// Called when component properties change.
    /// </summary>
    public void RegisterHookUpdate(IImplementable component)
    {
        if (component != null && component.Hook != null)
        {
            lock (_hookUpdatesLock)
            {
                if (_queuedHookUpdates.Add(component))
                {
                    _pendingHookUpdates.Enqueue(component);
                }
            }
        }
    }

    // Update Execution

    /// <summary>
    /// Run all startup callbacks.
    /// </summary>
    public void RunStartups()
    {
        while (_startupQueue.Count > 0)
        {
            var updatable = _startupQueue.Dequeue();
            if (!updatable.IsDestroyed)
            {
                try
                {
                    CurrentlyUpdating = updatable;
                    updatable.InternalRunStartup();
                }
                catch (Exception ex)
                {
                    Logging.Logger.Error($"UpdateManager: Error in startup for {updatable}: {ex.Message}");
                }
                finally
                {
                    CurrentlyUpdating = null!;
                }
            }
        }
    }

    /// <summary>
    /// Run all component updates in bucket order.
    /// </summary>
    public void RunUpdates(float deltaTime)
    {
        _currentDeltaTime = deltaTime;

        bool prof = ProfilingEnabled;
        if (prof)
        {
            _profByType.Clear();
            _profBySlot.Clear();
        }

        foreach (var kvp in _updateBuckets)
        {
            var bucket = kvp.Value;
            for (int i = 0; i < bucket.Count; i++)
            {
                var updatable = bucket[i];
                if (!updatable.IsDestroyed)
                {
                    try
                    {
                        CurrentlyUpdating = updatable;
                        if (prof)
                        {
                            long start = Stopwatch.GetTimestamp();
                            updatable.InternalRunUpdate();
                            RecordProfile(updatable, Stopwatch.GetTimestamp() - start);
                        }
                        else
                        {
                            updatable.InternalRunUpdate();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Logger.Error($"UpdateManager: Error in update for {updatable}: {ex.Message}");
                    }
                    finally
                    {
                        CurrentlyUpdating = null!;
                    }
                }
            }
        }
    }

    private void RecordProfile(IUpdatable updatable, long ticks)
    {
        if (updatable is not Component comp)
        {
            return;
        }

        var typeName = comp.GetType().Name;
        if (!_profByType.TryGetValue(typeName, out var tb))
        {
            tb = new ProfBucket { Name = typeName };
            _profByType[typeName] = tb;
        }
        tb.Ticks += ticks;
        tb.Count++;

        var slot = comp.Slot;
        if (slot != null)
        {
            if (!_profBySlot.TryGetValue(slot, out var sb))
            {
                sb = new ProfBucket { Name = string.IsNullOrEmpty(slot.SlotName.Value) ? "<unnamed slot>" : slot.SlotName.Value };
                _profBySlot[slot] = sb;
            }
            sb.Ticks += ticks;
            sb.Count++;
        }
    }

    /// <summary>
    /// Copy the latest frame's update profile into the caller's lists (by component type, and by slot), converted
    /// to milliseconds. Cheap and allocation-light; the host reads this for the debug console's profiler. -xlinka
    /// </summary>
    public void CollectProfile(List<ProfileEntry> byType, List<ProfileEntry> bySlot)
    {
        double tickToMs = 1000.0 / Stopwatch.Frequency;
        foreach (var kv in _profByType)
        {
            byType.Add(new ProfileEntry(kv.Value.Name, kv.Value.Ticks * tickToMs, kv.Value.Count));
        }
        foreach (var kv in _profBySlot)
        {
            bySlot.Add(new ProfileEntry(kv.Value.Name, kv.Value.Ticks * tickToMs, kv.Value.Count));
        }
    }

    /// <summary>
    /// Run all change application callbacks in bucket order.
    /// </summary>
    public void RunChangeApplications()
    {
        _changeUpdateIndex++;

        foreach (var kvp in _changeBuckets)
        {
            var queue = kvp.Value;
            while (queue.Count > 0)
            {
                var updatable = queue.Dequeue();
                if (!updatable.IsDestroyed && updatable.IsChangeDirty)
                {
                    try
                    {
                        CurrentlyUpdating = updatable;
                        updatable.InternalRunApplyChanges(_changeUpdateIndex);
                    }
                    catch (Exception ex)
                    {
                        Logging.Logger.Error($"UpdateManager: Error in change application for {updatable}: {ex.Message}");
                    }
                    finally
                    {
                        CurrentlyUpdating = null!;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Run all destruction callbacks.
    /// </summary>
    public void RunDestructions()
    {
        while (_destructionQueue.Count > 0)
        {
            var updatable = _destructionQueue.Dequeue();
            try
            {
                CurrentlyUpdating = updatable;
                updatable.InternalRunDestruction();
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"UpdateManager: Error in destruction for {updatable}: {ex.Message}");
            }
            finally
            {
                CurrentlyUpdating = null!;
            }
        }
    }

    /// <summary>
    /// Process all pending hook updates.
    /// Called by the world renderer after component updates.
    /// </summary>
    public void ProcessHookUpdates(float deltaTime)
    {
        _currentDeltaTime = deltaTime;

        const int maxUpdates = 100000;
        int processed = 0;

        // Per-frame wall-clock budget so a burst of hook work spreads across frames instead of freezing the one.
        // Without it, an import that dirties N skinned renderers at once builds all N Godot meshes (ArrayMesh +
        // per-blendshape arrays + Skin) back-to-back in a single frame - a multi-second stall. A single hook can't
        // be split, so one heavy mesh still costs its frame, but N meshes now spread over N frames and the main
        // thread (which also renders + paints the loading bar) stays responsive. Normal frames drain a tiny queue
        // far under budget, so this is a no-op except during bursts. GetTimestamp is allocation-free. -xlinka
        const double budgetMs = 6.0;
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        double ticksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        while (true)
        {
            IImplementable implementable;
            lock (_hookUpdatesLock)
            {
                if (_pendingHookUpdates.Count == 0)
                {
                    return;
                }

                implementable = _pendingHookUpdates.Dequeue();
                _queuedHookUpdates.Remove(implementable);
            }

            if (implementable == null || implementable.IsDestroyed ||
                (implementable is Worker worker && worker.IsRemoved) ||
                implementable.Hook == null)
            {
                continue;
            }

            // Contain hook failures like every other phase. A throwing hook used to
            // abort the whole world update mid-frame, which left queued startups,
            // changed-element processing and destructions undrained - destroyed UI
            // kept getting hover writes and the same hook re-threw every frame. - xlinka
            try
            {
                implementable.Hook.ApplyChanges();
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"UpdateManager: Error in hook update for {implementable}: {ex}");
            }

            processed++;
            if (processed >= maxUpdates)
            {
                Logging.Logger.Warn("UpdateManager: Hook update queue hit safety limit.");
                return;
            }

            // Out of frame budget: leave the rest of the queue for next frame so rendering isn't stalled. The
            // undrained items stay in _pendingHookUpdates/_queuedHookUpdates and get picked up next tick.
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * ticksToMs >= budgetMs)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Register a slot whose world transform changed this frame. Deduplicated; the queued slots
    /// fire their WorldTransformChanged event once, in <see cref="ProcessMovedSlots"/>.
    /// </summary>
    public void RegisterMovedSlot(Slot slot)
    {
        if (slot == null || slot.IsDestroyed)
            return;
        lock (_movedSlotsLock)
        {
            _pendingMovedSlots.Add(slot);
        }
    }

    /// <summary>
    /// Fire deferred WorldTransformChanged events for slots that moved this frame, parents before
    /// children. Runs before hook updates so a handler that re-drives a transform reaches the
    /// engine the same frame. Returns the number fired.
    /// </summary>
    public int ProcessMovedSlots()
    {
        List<Slot> batch;
        lock (_movedSlotsLock)
        {
            if (_pendingMovedSlots.Count == 0)
                return 0;
            batch = new List<Slot>(_pendingMovedSlots);
            _pendingMovedSlots.Clear();
        }

        // Parents before children, so a child handler reading parent state sees it updated.
        batch.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        foreach (var slot in batch)
        {
            if (slot == null || slot.IsDestroyed)
                continue;
            try
            {
                slot.FireWorldTransformChanged();
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"UpdateManager: Error in moved event for {slot}: {ex}");
            }
        }
        return batch.Count;
    }

    /// <summary>
    /// Clear all pending updates.
    /// </summary>
    public void Clear()
    {
        lock (_hookUpdatesLock)
        {
            _pendingHookUpdates.Clear();
            _queuedHookUpdates.Clear();
        }
        lock (_movedSlotsLock)
        {
            _pendingMovedSlots.Clear();
        }
        _updateBuckets.Clear();
        _startupQueue.Clear();
        _destructionQueue.Clear();
        _changeBuckets.Clear();
        _initializableChildren.Clear();
    }

    /// <summary>
    /// Track a child initializable so its init phase can be ended when the parent finishes.
    /// </summary>
    public void AddInitializableChild(IInitializable parent, IInitializable child)
    {
        if (parent == null || child == null)
            return;

        if (!_initializableChildren.TryGetValue(parent, out var list))
        {
            list = new List<IInitializable>();
            _initializableChildren[parent] = list;
        }

        list.Add(child);
    }

    /// <summary>
    /// End initialization phase on all tracked children of the given parent.
    /// </summary>
    public void EndInitPhaseInChildren(IInitializable parent)
    {
        if (parent == null)
            return;

        if (_initializableChildren.TryGetValue(parent, out var children))
        {
            foreach (var child in children)
            {
                try
                {
                    if (child.IsInInitPhase)
                    {
                        child.EndInitPhase();
                    }
                }
                catch (Exception ex)
                {
                    Logging.Logger.Error($"UpdateManager: Error ending init phase for {child}: {ex.Message}");
                }
            }

            _initializableChildren.Remove(parent);
        }
    }
}
