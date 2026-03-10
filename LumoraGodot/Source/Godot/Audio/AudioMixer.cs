// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core.External.GenericAudioOutputMixer;

namespace Lumora.Source.Godot;

public partial class AudioMixer : IAudioMixer
{
    private static readonly AudioMixer _instance = new();
    public static AudioMixer GetMixer() => _instance;

    private readonly object _sync = new();
    private readonly Dictionary<string, AudioBus> _buses = new(StringComparer.Ordinal);

    private AudioMixer()
    {
        ForceSync();
    }

    public string[] GetAvailableAudioEffects() => Array.Empty<string>();

    public void ForceSync()
    {
        lock (_sync)
        {
            foreach (var bus in _buses.Values)
                bus.Invalidate();

            _buses.Clear();

            int busCount = AudioServer.BusCount;
            for (int i = 0; i < busCount; i++)
            {
                string name = AudioServer.GetBusName(i);
                _buses[name] = new AudioBus(i);
            }
        }
    }

    public bool CreateAudioBus(string name, out IAudioBus? bus)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                bus = null;
                return false;
            }

            if (TryGetValidBusNoLock(name, out _))
            {
                bus = null;
                return false;
            }

            int newBusId = AudioServer.BusCount;
            AudioServer.AddBus(newBusId);
            AudioServer.SetBusName(newBusId, name);

            var created = new AudioBus(newBusId);
            _buses[name] = created;
            bus = created;
            return true;
        }
    }

    public IAudioBus GetAudioBusByName(string name)
    {
        if (TryGetAudioBusByName(name, out var bus) && bus != null)
            return bus;

        throw new KeyNotFoundException($"Audio bus '{name}' was not found.");
    }

    public IAudioBus? GetAudioBusByNameOrNull(string name)
    {
        return TryGetAudioBusByName(name, out var bus) ? bus : null;
    }

    public bool TryGetAudioBusByName(string name, out IAudioBus? bus)
    {
        lock (_sync)
        {
            if (TryGetValidBusNoLock(name, out var found))
            {
                bus = found;
                return true;
            }

            bus = null;
            return false;
        }
    }

    public IAudioBus[] GetAllBuses()
    {
        lock (_sync)
        {
            var result = new List<IAudioBus>(_buses.Count);
            var staleKeys = new List<string>();

            foreach (var kv in _buses)
            {
                if (!kv.Value.IsValid)
                {
                    staleKeys.Add(kv.Key);
                    continue;
                }

                result.Add(kv.Value);
            }

            foreach (string key in staleKeys)
                _buses.Remove(key);

            return result.ToArray();
        }
    }

    private bool TryGetValidBusNoLock(string name, out AudioBus? bus)
    {
        bus = null;
        if (!_buses.TryGetValue(name, out var raw))
            return false;

        if (!raw.IsValid)
        {
            _buses.Remove(name);
            return false;
        }

        bus = raw;
        return true;
    }
}
