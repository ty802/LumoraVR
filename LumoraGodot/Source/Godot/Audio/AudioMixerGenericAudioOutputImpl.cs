using System;
using Godot;
using System.Collections.Generic;
using Lumora.Core.External.GenericAudioOutputMixer;
namespace Aquamarine.Source.Godot;

public partial class AudioMixer : IAudioMixer
{
    IAudioBus IAudioMixer.GetAudioBusByName(string name)
    {
        if (buses.TryGetValue(name, out var b))
            return b;
        throw new Exception("I told you so");
    }
    bool IAudioMixer.TryGetAudioBusByName(string name, out IAudioBus bus)
    {
#if DEBUG
        void logfname1(string log, [System.Runtime.CompilerServices.CallerFilePath] string fp = "", [System.Runtime.CompilerServices.CallerLineNumber] int ln = 1)
        {
            Lumora.Core.Logging.Logger.Log($"[{fp}:{ln}]: {log}");
        }

        if (name == "Master")
        {
            logfname1("This is master");
        }
        var st = new System.Diagnostics.StackTrace(true);
        var fr = st.GetFrame(1);
        Lumora.Core.Logging.Logger.Log($"[Audio] {fr.GetMethod().Name} is trying to get bus {name}");
#endif
        if (!buses.TryGetValue(name, out var rawbus))
        {
            bus = default;
            return false;
        }
        if (rawbus.IsValid)
        {
            bus = (IAudioBus)rawbus;
        }
        else
        {
            buses.Remove(name);
            bus = default;
            return false;
        }
        return true;
    }
#nullable enable
    IAudioBus? IAudioMixer.GetAudioBusByNameOrNull(string name)
    {
        if (buses.TryGetValue(name, out var bus))
            return (IAudioBus)bus;
        return null;
    }
#nullable disable
    bool IAudioMixer.CreateAudioBus(string name, out IAudioBus bus)
    {
        lock (_writeLock)
        {
            if (buses.ContainsKey(name))
            {
                if (buses.TryGetValue(name, out var ebus) && ebus.IsValid)
                {
                    bus = default; return false;
                }
                buses.Remove(name);
            }
            int newBusId = AudioServer.BusCount;
            AudioServer.AddBus(newBusId);
            AudioServer.SetBusName(newBusId, name);
            bus = new AudioBus(newBusId);
            return true;
        }
    }
    IAudioBus[] IAudioMixer.GetAllBuses()
    {
        IAudioBus[] res = new IAudioBus[buses.Count];
        HashSet<int> indexs = new();
        foreach (var kv in buses)
        {
            if (!kv.Value.IsValid)
            {
                buses.Remove(kv.Key);
                continue;
            }
            int id = kv.Value.BusId;
            if (indexs.Add(id) && res.Length >= id)
            {
                res[id] = kv.Value;
            }
        }
        return res;
    }
}

