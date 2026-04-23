// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Lumora.Core.Logging;
using Lumora.Core.Management;
using Lumora.Core.Helpers;
using Lumora.Core.Assets;
using Lumora.Core.Coroutines;
using Lumora.Core.Physics;
using Lumora.Core.Persistence;
using Lumora.Core.Templates;
using Lumora.CDN;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Engine initialization state.
/// </summary>
public enum EngineState
{
    /// <summary>Not yet initialized.</summary>
    NotInitialized,
    /// <summary>Currently initializing subsystems.</summary>
    Initializing,
    /// <summary>Fully initialized and running.</summary>
    Running,
    /// <summary>Currently shutting down.</summary>
    ShuttingDown,
    /// <summary>Fully disposed.</summary>
    Disposed,
    /// <summary>Initialization failed.</summary>
    Failed
}

/// <summary>
/// Status of a subsystem initialization.
/// </summary>
public enum SubsystemStatus
{
    /// <summary>Not yet initialized.</summary>
    Pending,
    /// <summary>Currently initializing.</summary>
    Initializing,
    /// <summary>Successfully initialized.</summary>
    Ready,
    /// <summary>Initialization failed.</summary>
    Failed,
    /// <summary>Disposed.</summary>
    Disposed
}

/// <summary>
/// Engine version information.
/// </summary>
public static class EngineVersion
{
    public const int Major = 0;
    public const int Minor = 1;
    public const int Patch = 0;
    public const string Suffix = "alpha";
    public static readonly string VersionString = $"{Major}.{Minor}.{Patch}-{Suffix}";
    public static readonly DateTime BuildDate = new DateTime(2024, 1, 1); // Update with actual build
}

/// <summary>
/// Engine performance metrics.
/// </summary>
public class EngineMetrics
{
    private readonly Stopwatch _frameTimer = new Stopwatch();
    private readonly Queue<double> _frameTimes = new Queue<double>();
    private const int FrameTimeHistorySize = 120;

    /// <summary>Total frames processed.</summary>
    public long TotalFrames { get; private set; }

    /// <summary>Total time engine has been running in seconds.</summary>
    public double TotalTime { get; private set; }

    /// <summary>Time of the last frame in seconds.</summary>
    public double LastFrameTime { get; private set; }

    /// <summary>Average frame time over recent history.</summary>
    public double AverageFrameTime { get; private set; }

    /// <summary>Current frames per second.</summary>
    public double CurrentFPS => LastFrameTime > 0 ? 1.0 / LastFrameTime : 0;

    /// <summary>Average frames per second.</summary>
    public double AverageFPS => AverageFrameTime > 0 ? 1.0 / AverageFrameTime : 0;

    /// <summary>Peak memory usage in bytes.</summary>
    public long PeakMemoryUsage { get; private set; }

    /// <summary>Current memory usage in bytes.</summary>
    public long CurrentMemoryUsage => GC.GetTotalMemory(false);

    /// <summary>Number of garbage collections (Gen 0).</summary>
    public int GCCollections => GC.CollectionCount(0);

    internal void BeginFrame()
    {
        _frameTimer.Restart();
    }

    internal void EndFrame(double delta)
    {
        _frameTimer.Stop();
        TotalFrames++;
        TotalTime += delta;
        LastFrameTime = delta;

        _frameTimes.Enqueue(delta);
        while (_frameTimes.Count > FrameTimeHistorySize)
            _frameTimes.Dequeue();

        double sum = 0;
        foreach (var t in _frameTimes) sum += t;
        AverageFrameTime = sum / _frameTimes.Count;

        long currentMem = CurrentMemoryUsage;
        if (currentMem > PeakMemoryUsage)
            PeakMemoryUsage = currentMem;
    }

    public override string ToString()
    {
        return $"FPS: {CurrentFPS:F1} (avg {AverageFPS:F1}), Frames: {TotalFrames}, Memory: {CurrentMemoryUsage / 1024 / 1024}MB";
    }
}

/// <summary>
/// Core engine singleton managing all engine subsystems.
/// Provides initialization, update loop, and shutdown coordination.
/// </summary>
public class Engine : IDisposable
{
    private static Engine _instance = null!;
    private static readonly object _instanceLock = new object();

    private EngineState _state = EngineState.NotInitialized;
    private readonly object _stateLock = new object();
    private bool _hostingLocalHome;
    private int? _localHomePort;
    private World _pendingUserSpaceSetup = null!;
    private readonly Dictionary<string, SubsystemStatus> _subsystemStatus = new Dictionary<string, SubsystemStatus>();
    private Exception _initializationError = null!;

    // Engine timing
    private readonly Stopwatch _engineTimer = new Stopwatch();
    private readonly EngineMetrics _metrics = new EngineMetrics();
    private double _fixedTimeAccumulator;
    private const double DefaultFixedTimestep = 1.0 / 60.0;

    // Core subsystems
    public WorldManager WorldManager { get; private set; } = null!;
    public WorldLoadingService WorldLoadingService { get; private set; } = null!;
    public FocusManager FocusManager { get; private set; } = null!;
    public Input.InputInterface InputInterface { get; private set; } = null!;
    public AssetManager AssetManager { get; private set; } = null!;
    public GlobalCoroutineManager CoroutineManager { get; private set; } = null!;
    public RemoteAudioManager AudioManager { get; private set; } = null!;

    // CDN / Content delivery
    public LumoraClient? CDNClient { get; private set; }
    public ContentCache? ContentCache { get; private set; }

    // Local asset storage
    public LocalDB? LocalDB { get; set; }

    /// <summary>
    /// The asset transferer for the currently active session.
    /// Set automatically when a session is created or joined; cleared on dispose.
    /// AssetFetcher uses this to pull remote local:// assets from peers.
    /// </summary>
    public Networking.Session.SessionAssetTransferer? ActiveSessionTransferer { get; set; }

    /// <summary>
    /// Root directory for lumres:// and res:// URI resolution.
    /// </summary>
    public string ResourceRoot { get; set; } = null!;

    // Events
    /// <summary>Fired when engine state changes.</summary>
    public event Action<EngineState> OnStateChanged = null!;

    /// <summary>Fired before each update cycle.</summary>
    public event Action<double> OnPreUpdate = null!;

    /// <summary>Fired after each update cycle.</summary>
    public event Action<double> OnPostUpdate = null!;

    /// <summary>Fired when a subsystem status changes.</summary>
    public event Action<string, SubsystemStatus> OnSubsystemStatusChanged = null!;

    #region Static Properties

    /// <summary>
    /// Get the engine instance, throws if not initialized.
    /// </summary>
    public static Engine Instance
    {
        get
        {
            if (_instance == null)
                throw new InvalidOperationException("Engine not initialized. Call Engine.InitializeAsync() first.");
            return _instance;
        }
    }

    /// <summary>
    /// Current engine instance (null if not initialized).
    /// </summary>
    public static Engine Current => _instance;

    /// <summary>
    /// Check if the engine is initialized and running.
    /// </summary>
    public static bool IsInitialized => _instance?._state == EngineState.Running;

    /// <summary>
    /// Check if an engine instance exists.
    /// </summary>
    public static bool HasInstance => _instance != null;

    /// <summary>
    /// Enable debug output and features.
    /// </summary>
    public static bool ShowDebug { get; set; } = true;

    /// <summary>
    /// Whether running as a dedicated server (no rendering).
    /// </summary>
    public static bool IsDedicatedServer { get; set; } = false;

    /// <summary>
    /// Platform identifier string.
    /// </summary>
    public static string Platform => Environment.OSVersion.Platform.ToString();

    /// <summary>
    /// Whether running in editor mode.
    /// </summary>
    public static bool IsEditor { get; set; } = false;

    #endregion

    #region Instance Properties

    /// <summary>
    /// Current engine state.
    /// </summary>
    public EngineState State
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            lock (_stateLock)
            {
                if (_state != value)
                {
                    _state = value;
                    OnStateChanged?.Invoke(value);
                }
            }
        }
    }

    /// <summary>
    /// Whether the engine is currently shutting down.
    /// </summary>
    public bool IsShuttingDown => State == EngineState.ShuttingDown;

    /// <summary>
    /// Whether the engine is running.
    /// </summary>
    public bool IsRunning => State == EngineState.Running;

    /// <summary>
    /// Total time since engine start in seconds.
    /// </summary>
    public double TotalTime => _metrics.TotalTime;

    /// <summary>
    /// Total frames processed since engine start.
    /// </summary>
    public long FrameCount => _metrics.TotalFrames;

    /// <summary>
    /// Engine performance metrics.
    /// </summary>
    public EngineMetrics Metrics => _metrics;

    /// <summary>
    /// Fixed timestep for physics updates.
    /// </summary>
    public double FixedTimestep { get; set; } = DefaultFixedTimestep;

    /// <summary>
    /// Maximum fixed updates per frame (prevents spiral of death).
    /// </summary>
    public int MaxFixedUpdatesPerFrame { get; set; } = 8;

    /// <summary>
    /// Auto-host local home world on startup.
    /// </summary>
    public bool AutoHostLocalHome { get; set; } = true;

    /// <summary>
    /// Auto-connect to local home on startup.
    /// </summary>
    public bool AutoConnectLocalHome { get; set; } = true;

    /// <summary>
    /// Error that occurred during initialization (if any).
    /// </summary>
    public Exception InitializationError => _initializationError;

    /// <summary>
    /// Get the status of all subsystems.
    /// </summary>
    public IReadOnlyDictionary<string, SubsystemStatus> SubsystemStatuses => _subsystemStatus;

    #endregion

    #region Initialization

    /// <summary>
    /// Create a new engine instance without initializing.
    /// Use InitializeAsync() to initialize.
    /// </summary>
    public Engine()
    {
        lock (_instanceLock)
        {
            if (_instance != null)
                throw new InvalidOperationException("Engine instance already exists. Call Dispose() first.");
            _instance = this;
        }
    }

    /// <summary>
    /// Initialize the engine and all subsystems asynchronously.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (State != EngineState.NotInitialized)
        {
            LumoraLogger.Warn($"Engine already in state {State}, skipping initialization.");
            return;
        }

        State = EngineState.Initializing;
        _engineTimer.Start();

        LumoraLogger.Log("=====================================");
        LumoraLogger.Log($"Lumora Engine v{EngineVersion.VersionString}");
        LumoraLogger.Log($"Platform: {Platform}");
        LumoraLogger.Log("=====================================");
        LumoraLogger.Log("Engine Initialization Starting");
        LumoraLogger.Log("=====================================");

        try
        {
            LumoraLogger.Log("Initializing core systems...");

            await InitializeSubsystem("FocusManager", async () =>
            {
                FocusManager = new FocusManager();
                await Task.CompletedTask;
            }, cancellationToken);

            await InitializeSubsystem("InputInterface", async () =>
            {
                InputInterface = new Input.InputInterface();
                await InputInterface.InitializeAsync();
            }, cancellationToken);

            await InitializeSubsystem("CoroutineManager", async () =>
            {
                CoroutineManager = new GlobalCoroutineManager();
                await Task.CompletedTask;
            }, cancellationToken);

            LumoraLogger.Log("Initializing asset systems...");

            await InitializeSubsystem("AssetManager", async () =>
            {
                AssetManager = new AssetManager(this);
                await AssetManager.InitializeAsync();
            }, cancellationToken);

            await InitializeSubsystem("ContentCache", async () =>
            {
                var deviceId = Environment.MachineName;
                CDNClient = new LumoraClient(deviceId, "LumoraVR", EngineVersion.VersionString);
                var cachePath = Path.Combine(Lumora.Core.Persistence.PathResolver.CachePath, "LumoraVR", "Cache");
                ContentCache = new ContentCache(CDNClient, cachePath);
                await Task.CompletedTask;
            }, cancellationToken);

            // Initialize AudioSystem
            AudioManager = new();
            foreach (string name in new string[] { "Music", "Effects", "Voice" })
            {
                if (AudioManager.Mixer.TryCreateAudioBus(name, out var bus) && AudioManager.Mixer.TryGetAudioBusByName("Master", out var master))
                    bus.Target = master;
            }
            if(AudioManager.Mixer.TryGetAudioBusByName("Voice",out var voicebus)){
                voicebus.Mute = true;
            }


            // Physics is per-world and delegated to the platform engine (Godot/Jolt), accessed
            // through World.Physics - there is no engine-level physics subsystem to initialize.

            LumoraLogger.Log("Initializing world management...");

            await InitializeSubsystem("WorldManager", async () =>
            {
                WorldManager = new WorldManager();
                await WorldManager.InitializeAsync(this);
                WorldLoadingService = new WorldLoadingService(this);
            }, cancellationToken);

            LumoraLogger.Log("Post-initialization setup...");
            ProcessStartupArguments();

            State = EngineState.Running;

            var initTime = _engineTimer.Elapsed.TotalSeconds;
            LumoraLogger.Log("=====================================");
            LumoraLogger.Log($"Engine Initialized Successfully in {initTime:F2}s");
            LumoraLogger.Log($"Subsystems: {_subsystemStatus.Count} initialized");
            LumoraLogger.Log("=====================================");
        }
        catch (OperationCanceledException)
        {
            LumoraLogger.Warn("Engine initialization was cancelled.");
            State = EngineState.Failed;
            _initializationError = new OperationCanceledException("Initialization cancelled");
            throw;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"Engine initialization failed: {ex.Message}");
            LumoraLogger.Error(ex.StackTrace ?? ex.Message);
            State = EngineState.Failed;
            _initializationError = ex;
            lock (_instanceLock) { _instance = null!; }
            throw;
        }
    }

    private async Task InitializeSubsystem(string name, Func<Task> initializer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SetSubsystemStatus(name, SubsystemStatus.Initializing);

        try
        {
            var sw = Stopwatch.StartNew();
            await initializer();
            sw.Stop();
            SetSubsystemStatus(name, SubsystemStatus.Ready);
            LumoraLogger.Log($"  [{sw.ElapsedMilliseconds}ms] {name} initialized");
        }
        catch (Exception ex)
        {
            SetSubsystemStatus(name, SubsystemStatus.Failed);
            LumoraLogger.Error($"  [FAILED] {name}: {ex.Message}");
            throw;
        }
    }

    private void SetSubsystemStatus(string name, SubsystemStatus status)
    {
        _subsystemStatus[name] = status;
        OnSubsystemStatusChanged?.Invoke(name, status);
    }

    private void ProcessStartupArguments()
    {
        bool skipLocalHome = false;

        if (!skipLocalHome && AutoHostLocalHome)
        {
            StartLocalHome();

            if (AutoConnectLocalHome)
            {
                SwitchToLocalHome();
            }
        }
    }

    #endregion

    #region Local Home Management

    /// <summary>On-disk location of the persisted local home world.</summary>
    public static string LocalHomeSavePath => Path.Combine(
        Lumora.Core.Persistence.PathResolver.RoamingPath,
        "LumoraVR", "home.lworld");

    private void StartLocalHome()
    {
        if (_hostingLocalHome)
        {
            SwitchToLocalHome();
            return;
        }

        if (_localHomePort is null)
        {
            _localHomePort = SimpleIpHelpers.GetAvailablePortUdp(10) ?? 6000;
        }

        // Load the saved home if one exists (build it into a blank world so the template's default
        // content isn't duplicated); otherwise build the default from the LocalHome template.
        var savePath = LocalHomeSavePath;
        bool hasSave = File.Exists(savePath);
        string template = hasSave ? "" : "LocalHome";
        Action<World>? init = hasSave
            ? w =>
            {
                if (!WorldStorage.LoadFromFile(w, savePath))
                {
                    LumoraLogger.Warn("Engine: LocalHome save failed to load; falling back to template.");
                    WorldTemplates.ApplyTemplate(w, "LocalHome");
                }
            }
            : null!;

        var world = WorldManager?.StartSession("LocalHome", (ushort)_localHomePort.Value, GetHostUserName(), template, init!);
        if (world == null)
        {
            LumoraLogger.Error("Engine: Failed to start LocalHome session.");
            return;
        }

        _hostingLocalHome = true;
        LumoraLogger.Log($"Engine: LocalHome hosted on port {_localHomePort.Value}.");

        _pendingUserSpaceSetup = world;
    }

    private void SwitchToLocalHome()
    {
        var world = WorldManager?.GetWorldByName("LocalHome");
        if (world != null)
        {
            WorldManager!.SwitchToWorld(world);
            LumoraLogger.Log("Engine: Switched to LocalHome world.");
        }
        else
        {
            LumoraLogger.Warn("Engine: LocalHome world not found.");
        }
    }

    private string GetHostUserName()
    {
        return Environment.MachineName;
    }

    /// <summary>
    /// Get the world pending userspace setup.
    /// </summary>
    public World GetPendingUserSpaceSetup() => _pendingUserSpaceSetup;

    /// <summary>
    /// Clear the pending userspace setup.
    /// </summary>
    public void ClearPendingUserSpaceSetup() => _pendingUserSpaceSetup = null!;

    /// <summary>
    /// Join the local home world.
    /// </summary>
    public void JoinLocalHome() => SwitchToLocalHome();

    /// <summary>
    /// Join a remote server.
    /// </summary>
    public void JoinServer(string address, int port, string worldName = "RemoteWorld")
    {
        WorldManager?.JoinSession(worldName, address, (ushort)port);
    }

    /// <summary>
    /// Join via NAT punch-through: punch a hole to the session host through the relay
    /// server, then connect directly to the resolved endpoint.
    /// </summary>
    public void JoinNatServer(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            LumoraLogger.Warn("Engine: NAT join called with an empty session identifier.");
            return;
        }

        var addr = Networking.Session.Session.SessionServerAddress;
        var port = Networking.Session.Session.SessionServerPort;
        LumoraLogger.Log($"Engine: NAT punch join for session '{identifier}' via {addr}:{port}");

        var client = new Networking.Session.SessionServerClient(addr, port);
        var joined = false;

        client.OnNATPunchSuccess += ep =>
        {
            if (joined) return;
            joined = true;

            LumoraLogger.Log($"Engine: NAT punch succeeded -> {ep}; connecting directly.");
            var wm = WorldManager;

            // The punch callback runs on the client's poll task; hop to the main
            // update thread to create/join the world.
            if (wm?.FocusedWorld != null)
                wm.FocusedWorld.RunSynchronously(() => wm.JoinSession("RemoteWorld", ep.Address.ToString(), (ushort)ep.Port));
            else
                wm?.JoinSession("RemoteWorld", ep.Address.ToString(), (ushort)ep.Port);

            // Hold the punch socket open briefly so the NAT mapping stays warm while
            // the direct connection establishes, then release it. (Hard NATs that
            // can't reuse the hole for a fresh socket fall back to relay.)
            _ = Task.Delay(5000).ContinueWith(_ => client.Dispose(), TaskScheduler.Default);
        };

        _ = client.RequestNATPunchAsync(identifier).ContinueWith(t =>
        {
            if (t.IsFaulted)
                LumoraLogger.Warn($"Engine: NAT punch request failed: {t.Exception?.GetBaseException().Message}");
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Join via relay server: tunnel the session through the relay when a direct or
    /// punched path isn't available.
    /// </summary>
    public void JoinNatServerRelay(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            LumoraLogger.Warn("Engine: Relay join called with an empty session identifier.");
            return;
        }

        // Make sure the relay transport is available (Register dedups by type).
        Networking.NetworkManagerRegistry.Register(new Networking.RelayNetworkManager());

        var addr = Networking.Session.Session.SessionServerAddress;
        var port = Networking.Session.Session.SessionServerPort;
        LumoraLogger.Log($"Engine: relay join for session '{identifier}' via {addr}:{port}");

        var relayUri = new Uri($"{Networking.RelayNetworkManager.SCHEME}://relay/{Uri.EscapeDataString(identifier)}");
        WorldManager?.JoinSession("RemoteWorld", relayUri);
    }

    #endregion

    #region Update Loop

    /// <summary>
    /// Main update loop - call every frame.
    /// </summary>
    public void Update(double delta)
    {
        if (State != EngineState.Running)
            return;

        _metrics.BeginFrame();
        OnPreUpdate?.Invoke(delta);

        try
        {
            // Input processing
            InputInterface?.ProcessInput(delta);

            // Global coroutines
            CoroutineManager?.Update((float)delta);

            // World updates
            WorldManager?.Update(delta);

            // Fixed updates (physics timestep)
            ProcessFixedUpdates(delta);
            InputInterface?.SyncTrackingSpaceToFocusedLocalUser();

            // Asset processing
            AssetManager?.Update((float)delta);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"Engine Update error: {ex.Message}");
            if (ShowDebug) LumoraLogger.Error(ex.StackTrace ?? ex.Message);
        }

        OnPostUpdate?.Invoke(delta);
        _metrics.EndFrame(delta);
    }

    private void ProcessFixedUpdates(double delta)
    {
        _fixedTimeAccumulator += delta;

        int fixedUpdates = 0;
        while (_fixedTimeAccumulator >= FixedTimestep && fixedUpdates < MaxFixedUpdatesPerFrame)
        {
            FixedUpdateInternal(FixedTimestep);
            _fixedTimeAccumulator -= FixedTimestep;
            fixedUpdates++;
        }

        // Prevent spiral of death
        if (_fixedTimeAccumulator > FixedTimestep * MaxFixedUpdatesPerFrame)
        {
            _fixedTimeAccumulator = 0;
        }
    }

    private void FixedUpdateInternal(double fixedDelta)
    {
        WorldManager?.FixedUpdate(fixedDelta);
    }

    /// <summary>
    /// Fixed update for deterministic physics (called automatically by Update).
    /// Can also be called manually for custom fixed-step simulations.
    /// </summary>
    public void FixedUpdate(double fixedDelta)
    {
        if (State != EngineState.Running)
            return;

        FixedUpdateInternal(fixedDelta);
    }

    /// <summary>
    /// Late update for camera and final positioning.
    /// </summary>
    public void LateUpdate(double delta)
    {
        if (State != EngineState.Running)
            return;

        WorldManager?.LateUpdate(delta);
    }

    #endregion

    #region Shutdown

    /// <summary>
    /// Request graceful shutdown.
    /// </summary>
    public void RequestShutdown()
    {
        if (State != EngineState.Running)
            return;

        LumoraLogger.Log("Engine: Shutdown requested.");
        Dispose();
    }

    /// <summary>
    /// Raised by <see cref="RequestQuit"/>. The platform layer subscribes to close the app
    /// (which tears the engine down through the normal exit path).
    /// </summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Request the application quit. Fires <see cref="QuitRequested"/> rather than disposing
    /// inline, so it's safe to call from UI during a world update.
    /// </summary>
    public void RequestQuit() => QuitRequested?.Invoke();

    /// <summary>
    /// Dispose the engine and all subsystems.
    /// </summary>
    public void Dispose()
    {
        if (State == EngineState.Disposed || State == EngineState.NotInitialized)
            return;

        State = EngineState.ShuttingDown;

        LumoraLogger.Log("=====================================");
        LumoraLogger.Log("Engine Shutdown Starting...");
        LumoraLogger.Log("=====================================");

        // Dispose in reverse initialization order
        DisposeSubsystem("WorldManager", () => { WorldManager?.Dispose(); WorldManager = null!; });
        DisposeSubsystem("AudioManager", () => { AudioManager.Dispose(); });
        DisposeSubsystem("ContentCache", () => { ContentCache?.Dispose(); ContentCache = null; CDNClient?.Dispose(); CDNClient = null; });
        DisposeSubsystem("AssetManager", () => { AssetManager?.Dispose(); AssetManager = null!; });
        DisposeSubsystem("CoroutineManager", () => { CoroutineManager?.Dispose(); CoroutineManager = null!; });
        DisposeSubsystem("InputInterface", () => { InputInterface?.Dispose(); InputInterface = null!; });
        DisposeSubsystem("FocusManager", () => { FocusManager = null!; });

        _engineTimer.Stop();
        var runTime = _engineTimer.Elapsed.TotalSeconds;

        State = EngineState.Disposed;

        lock (_instanceLock)
        {
            if (_instance == this)
                _instance = null!;
        }

        LumoraLogger.Log("=====================================");
        LumoraLogger.Log($"Engine Shutdown Complete");
        LumoraLogger.Log($"Total runtime: {runTime:F2}s, Frames: {_metrics.TotalFrames}");
        LumoraLogger.Log("=====================================");
    }

    private void DisposeSubsystem(string name, Action disposer)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            disposer();
            sw.Stop();
            SetSubsystemStatus(name, SubsystemStatus.Disposed);
            LumoraLogger.Log($"  [{sw.ElapsedMilliseconds}ms] {name} disposed");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"  [ERROR] {name} disposal: {ex.Message}");
        }
    }

    #endregion

    #region Utility

    /// <summary>
    /// Get diagnostic information about the engine.
    /// </summary>
    public string GetDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Lumora Engine v{EngineVersion.VersionString}");
        sb.AppendLine($"State: {State}");
        sb.AppendLine($"Platform: {Platform}");
        sb.AppendLine($"Runtime: {TotalTime:F2}s");
        sb.AppendLine($"Frames: {FrameCount}");
        sb.AppendLine($"FPS: {_metrics.CurrentFPS:F1} (avg {_metrics.AverageFPS:F1})");
        sb.AppendLine($"Memory: {_metrics.CurrentMemoryUsage / 1024 / 1024}MB (peak {_metrics.PeakMemoryUsage / 1024 / 1024}MB)");
        sb.AppendLine($"GC Collections: {_metrics.GCCollections}");
        sb.AppendLine("Subsystems:");
        foreach (var kvp in _subsystemStatus)
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        if (WorldManager != null)
            sb.AppendLine($"Worlds: {WorldManager.WorldCount}");
        return sb.ToString();
    }

    #endregion
}

