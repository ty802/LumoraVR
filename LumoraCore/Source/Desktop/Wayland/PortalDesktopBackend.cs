// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Core.Desktop;

// A Wayland session, where no program may read the screen or move the pointer on its own: the desktop
// portal asks the user, then shares the chosen monitors over PipeWire and takes input for them.
//
// The monitors are only known once the user has answered, so the first look at the Desktop tab starts the
// question and the list fills in after. A refusal is not asked again until the tab is opened again. The
// share is let go of half a minute after the last capture stops, so the desktop's sharing indicator does
// not stay lit all evening; with a remembered approval, picking it up again asks nothing. -xlinka
[SupportedOSPlatform("linux")]
internal sealed class PortalDesktopBackend : IDesktopBackend
{
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly PortalDesktopInput _input;
    private PortalSession? _session;
    private bool _asking;
    private bool _refused;
    private string? _status;
    private int _captures;
    private int _idleGeneration;

    private PortalDesktopBackend()
    {
        _input = new PortalDesktopInput(this);
    }

    public static PortalDesktopBackend? TryCreate() => PipeWireNative.Available ? new PortalDesktopBackend() : null;

    public string Name => "Wayland";

    public IDesktopInput? Input => _session is { IsOpen: true } session && (session.CanPoint || session.CanType) ? _input : null;

    public string? Status => _status;

    internal PortalSession? Session => _session is { IsOpen: true } session ? session : null;

    public IReadOnlyList<DesktopDisplay> Displays()
    {
        lock (_gate)
        {
            if (_session is { IsOpen: true } session)
            {
                var displays = new List<DesktopDisplay>();
                foreach (var stream in session.Streams)
                    displays.Add(stream.Display);
                return displays;
            }
            if (!_asking && !_refused)
                Ask();
            return Array.Empty<DesktopDisplay>();
        }
    }

    public void Retry()
    {
        lock (_gate)
            _refused = false;
    }

    public DesktopCapture CreateCapture(DesktopDisplay display) => new PortalDesktopCapture(display, this);

    internal PortalSession.Stream? StreamOf(PortalSession session, DesktopDisplay display)
    {
        foreach (var stream in session.Streams)
        {
            if (stream.Display.Key == display.Key)
                return stream;
        }
        return null;
    }

    internal void CaptureStarted()
    {
        lock (_gate)
        {
            _captures++;
            _idleGeneration++;
        }
    }

    internal void CaptureStopped()
    {
        int generation;
        lock (_gate)
        {
            if (--_captures > 0)
                return;
            generation = ++_idleGeneration;
        }
        Task.Delay(IdleRelease).ContinueWith(_ =>
        {
            PortalSession? release = null;
            lock (_gate)
            {
                // Only a share that can be picked up again without asking is let go of.
                if (_captures == 0 && generation == _idleGeneration && !string.IsNullOrEmpty(EngineSettings.DesktopPortalToken))
                {
                    release = _session;
                    _session = null;
                }
            }
            release?.Dispose();
        }, TaskScheduler.Default);
    }

    private void Ask()
    {
        _asking = true;
        _status = "Allow screen sharing on your desktop. It is asked once.";
        var thread = new Thread(() =>
        {
            var session = PortalSession.Start(EngineSettings.DesktopPortalToken, token => EngineSettings.DesktopPortalToken = token, out var problem);
            lock (_gate)
            {
                _asking = false;
                _session = session;
                _refused = session == null;
                _status = session == null ? problem + " Open the Desktop tab again to ask again." : null;
                if (session != null)
                    session.Closed += OnClosed;
            }
        })
        { IsBackground = true, Name = "Desktop portal" };
        thread.Start();
    }

    // Runs on the session's own bus reader, which cannot wait for itself to stop, so the session is let
    // go of on another thread.
    private void OnClosed()
    {
        PortalSession? dead;
        lock (_gate)
        {
            dead = _session;
            _session = null;
            _refused = true;
            _status = "Screen sharing ended. Open the Desktop tab again to share again.";
        }
        if (dead != null)
            Task.Run(dead.Dispose);
    }
}
