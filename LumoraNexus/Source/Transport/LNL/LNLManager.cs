// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net;
using LiteNetLib;
using LumoraLogger = Lumora.Nexus.Diagnostics.NexusLog;

namespace Lumora.Nexus.Transport.LNL;

public class LNLManager
{
    public const int DEFAULT_PORT = 7777;
    public const string APP_ID = "LumoraVR-LNL2";

    private List<LNLListener> _listeners = new();
    private List<LNLConnection> _connections = new();

    public LNLConnection CreateConnection(Uri uri, bool dontRoute = false, IPAddress bindIP = null!)
    {
        bindIP ??= IPAddress.Any;

        var connection = new LNLConnection(APP_ID, uri, dontRoute, bindIP);
        _connections.Add(connection);

        LumoraLogger.Log($"[lnl] Created LNL connection to {uri}");
        return connection;
    }

    public List<LNLListener> CreateListeners(ushort port)
    {
        var listeners = new List<LNLListener>();

        var globalListener = new LNLListener(APP_ID, port, IPAddress.Any);
        if (globalListener.IsInitialized)
        {
            _listeners.Add(globalListener);
            listeners.Add(globalListener);
            LumoraLogger.Log($"[lnl] Created LNL listener on 0.0.0.0:{port}");
        }

        return listeners;
    }

    // Must be called every frame by the platform driver.
    public void PollEvents()
    {
        foreach (var listener in _listeners)
        {
            listener.Poll();
        }

        foreach (var connection in _connections)
        {
            connection.Poll();
        }
    }

    public void TransmitData(byte[] data, int length, List<IConnection> targets, bool reliable, bool background)
    {
        foreach (var target in targets)
        {
            target.Send(data, length, reliable, background);
        }
    }

    public void Stop()
    {
        foreach (var connection in _connections)
        {
            connection.Close();
        }
        _connections.Clear();

        foreach (var listener in _listeners)
        {
            listener.Stop();
        }
        _listeners.Clear();

        LumoraLogger.Log("[lnl] LNL Manager shut down");
    }
}
