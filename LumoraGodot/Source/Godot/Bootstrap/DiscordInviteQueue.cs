// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Source.Godot.Bootstrap;

// The latest clicked invite waits for startup/world loading. URI activation and RPC can both deliver
// the same click; only one join should reach the loader. All calls are made on the main thread.
public sealed class DiscordInviteQueue
{
    private readonly Func<DateTime> _now;
    private Uri? _pending;
    private Uri? _last;
    private DateTime _lastAt;

    public DiscordInviteQueue(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public void Enqueue(Uri uri)
    {
        if (uri == _last && _now() - _lastAt < TimeSpan.FromSeconds(5)) return;
        _pending = uri;
    }

    public void Dispatch(Func<Uri, bool>? join)
    {
        if (_pending is not { } uri || join == null) return;
        try
        {
            if (!join(uri)) return;
            _last = uri;
            _lastAt = _now();
        }
        catch
        {
            if (_pending == uri) _pending = null;
            throw;
        }
        if (_pending == uri) _pending = null;
    }

    public void Clear()
    {
        _pending = null;
        _last = null;
    }
}
