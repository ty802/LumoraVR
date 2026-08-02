// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Core;

public class HookManager : IDisposable
{
    public enum LockOwner
    {
        None,
        DataModel,  // Main sync thread
        Implementer // Godot main thread
    }

    private readonly object _lockObj = new object();
    private Thread _lockingThread = null!;

    public World Owner { get; private set; }
    public LockOwner Lock { get; private set; }

    // Whether the current thread can modify world state.
    // Modifications are allowed when:
    // - World is not running (initialization phase)
    // - No lock is held (between update cycles)
    // - Current thread holds the lock
    public bool CanCurrentThreadModify
    {
        get
        {
            if (Owner.State != World.WorldState.Running)
                return true;

            if (Lock == LockOwner.None)
                return true;

            return Thread.CurrentThread == _lockingThread;
        }
    }

    public HookManager(World owner)
    {
        Owner = owner;
    }

    // Throws when modification is not allowed.
    public void ThreadCheck()
    {
        if (!CanCurrentThreadModify)
        {
            throw new Exception($"Modifications from non-locking thread disallowed! Current lock: {Lock}");
        }
    }

    // Waits if another thread holds the lock.
    public void DataModelLock(Thread ownerThread)
    {
        Monitor.Enter(_lockObj);
        _lockingThread = ownerThread;
        Lock = LockOwner.DataModel;
    }

    public void DataModelUnlock()
    {
        if (Lock != LockOwner.DataModel)
        {
            return; // Silently ignore if not locked by DataModel
        }
        _lockingThread = null!;
        Lock = LockOwner.None;
        Monitor.Exit(_lockObj);
    }

    // Waits if another thread holds the lock.
    public void ImplementerLock(Thread ownerThread)
    {
        Monitor.Enter(_lockObj);
        _lockingThread = ownerThread;
        Lock = LockOwner.Implementer;
    }

    public void ImplementerUnlock()
    {
        if (Lock != LockOwner.Implementer)
        {
            return; // Silently ignore if not locked by Implementer
        }
        _lockingThread = null!;
        Lock = LockOwner.None;
        Monitor.Exit(_lockObj);
    }

    public void Dispose()
    {
        Owner = null!;
        Lock = LockOwner.None;
        _lockingThread = null!;
    }
}
