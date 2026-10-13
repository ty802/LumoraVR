// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumora.Core.External.Audio.GenericOutputMixer;
#nullable enable
namespace Lumora.Source.Godot;

public partial class AudioMixer : IAudioMixer
{
    bool IAudioMixer.TryCreateAudioBus(string name, [NotNullWhen(true)] out IAudioBus? bus)
    {
        bool result = TryCreateAudioBus(name, out var newbus);
        if (result)
            bus = newbus;
        else
            bus = default;
        return result;
    }

    IAudioBus? IAudioMixer.GetAudioBusByNameOrNull(string name)
    {
        return ((IAudioMixer)this).TryGetAudioBusByName(name, out var bus) ? bus : null;
    }

    bool IAudioMixer.TryGetAudioBusByName(string name, [NotNullWhen(true)] out IAudioBus? bus)
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

    IAudioBus[] IAudioMixer.GetAllBuses()
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
}
