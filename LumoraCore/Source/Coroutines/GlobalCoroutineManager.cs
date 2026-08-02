// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Logging;

namespace Lumora.Core.Coroutines;

public class GlobalCoroutineManager : IDisposable
{
    private readonly List<Coroutine> _activeCoroutines = new List<Coroutine>();
    private readonly Queue<Coroutine> _coroutinesToAdd = new Queue<Coroutine>();
    private readonly Queue<Coroutine> _coroutinesToRemove = new Queue<Coroutine>();
    private bool _isUpdating = false;

    public int ActiveCoroutineCount => _activeCoroutines.Count;
    public int TotalCoroutinesStarted { get; private set; }
    public int TotalCoroutinesCompleted { get; private set; }

    public Coroutine StartCoroutine(IEnumerator routine, string name = null!)
    {
        if (routine == null)
            throw new ArgumentNullException(nameof(routine));

        var coroutine = new Coroutine(routine, name ?? "GlobalCoroutine");

        if (_isUpdating)
        {
            _coroutinesToAdd.Enqueue(coroutine);
        }
        else
        {
            _activeCoroutines.Add(coroutine);
        }

        TotalCoroutinesStarted++;
        Logger.Log($"GlobalCoroutineManager: Started coroutine '{coroutine.Name}'");

        return coroutine;
    }

    public void StopCoroutine(Coroutine coroutine)
    {
        if (coroutine == null)
            return;

        coroutine.Stop();

        if (_isUpdating)
        {
            _coroutinesToRemove.Enqueue(coroutine);
        }
        else
        {
            _activeCoroutines.Remove(coroutine);
            TotalCoroutinesCompleted++;
        }
    }

    public void StopAllCoroutines()
    {
        foreach (var coroutine in _activeCoroutines)
        {
            coroutine.Stop();
        }

        _activeCoroutines.Clear();
        _coroutinesToAdd.Clear();
        _coroutinesToRemove.Clear();

        Logger.Log("GlobalCoroutineManager: Stopped all coroutines");
    }

    public void Update(float deltaTime)
    {
        _isUpdating = true;

        while (_coroutinesToAdd.Count > 0)
        {
            _activeCoroutines.Add(_coroutinesToAdd.Dequeue());
        }

        for (int i = _activeCoroutines.Count - 1; i >= 0; i--)
        {
            var coroutine = _activeCoroutines[i];

            if (!coroutine.Update(deltaTime))
            {
                _activeCoroutines.RemoveAt(i);
                TotalCoroutinesCompleted++;
                Logger.Log($"GlobalCoroutineManager: Coroutine '{coroutine.Name}' completed");
            }
        }

        while (_coroutinesToRemove.Count > 0)
        {
            var coroutine = _coroutinesToRemove.Dequeue();
            _activeCoroutines.Remove(coroutine);
            TotalCoroutinesCompleted++;
        }

        _isUpdating = false;
    }

    public void Dispose()
    {
        StopAllCoroutines();
        Logger.Log("GlobalCoroutineManager: Disposed");
    }
}

public class Coroutine
{
    private IEnumerator _routine;
    private object _current = null!;
    private bool _isRunning;
    private float _waitTimer;

    public string Name { get; }
    public bool IsRunning => _isRunning;
    public object Current => _current;

    public Coroutine(IEnumerator routine, string name)
    {
        _routine = routine ?? throw new ArgumentNullException(nameof(routine));
        Name = name;
        _isRunning = true;
        _waitTimer = 0f;
    }

    public bool Update(float deltaTime)
    {
        if (!_isRunning)
            return false;

        if (_waitTimer > 0)
        {
            _waitTimer -= deltaTime;
            return true;
        }

        if (_current is WaitForSeconds waitForSeconds)
        {
            _waitTimer = waitForSeconds.Seconds;
            _current = null!;
            return true;
        }
        else if (_current is WaitForEndOfFrame)
        {
            _current = null!;
            return true;
        }
        else if (_current is WaitUntil waitUntil)
        {
            if (!waitUntil.Condition())
                return true;
            _current = null!;
        }
        else if (_current is WaitWhile waitWhile)
        {
            if (waitWhile.Condition())
                return true;
            _current = null!;
        }

        try
        {
            if (_routine.MoveNext())
            {
                _current = _routine.Current;
                return true;
            }
            else
            {
                _isRunning = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Coroutine '{Name}' exception: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _routine = null!;
        _current = null!;
    }
}

public class WaitForSeconds
{
    public float Seconds { get; }

    public WaitForSeconds(float seconds)
    {
        Seconds = seconds > 0 ? seconds : 0;
    }
}

public class WaitForEndOfFrame
{
}

public class WaitUntil
{
    public Func<bool> Condition { get; }

    public WaitUntil(Func<bool> condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}

public class WaitWhile
{
    public Func<bool> Condition { get; }

    public WaitWhile(Func<bool> condition)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
    }
}

public static class CoroutineExtensions
{
    public static Coroutine StartGlobalCoroutine(this IEnumerator routine, string name = null!)
    {
        if (Engine.Current?.CoroutineManager != null)
        {
            return Engine.Current.CoroutineManager.StartCoroutine(routine, name);
        }

        Logger.Warn("Cannot start global coroutine - Engine or CoroutineManager not initialized");
        return null!;
    }
}
