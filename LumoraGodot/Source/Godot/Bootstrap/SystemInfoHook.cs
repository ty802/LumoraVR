// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.Bootstrap;

public partial class SystemInfoHook : Node
{
    // PERFORMANCE TRACKING
    private double _lastFrameTime = 0;
    private double _deltaTimeAccumulator = 0;
    private int _frameCount = 0;
    private const int FPS_SAMPLE_FRAMES = 60;

    // PUBLIC STATS
    public float CurrentFPS { get; private set; } = 0f;
    public float AverageFPS { get; private set; } = 0f;
    public float GPUTimeMs { get; private set; } = 0f;
    public string OutputDevice { get; private set; } = "Unknown";

    // SYSTEM INFO
    public string GPUName { get; private set; } = "Unknown";
    public string CPUName { get; private set; } = "Unknown";
    public long TotalMemoryMB { get; private set; } = 0;
    public string OSName { get; private set; } = "Unknown";

    public override void _Ready()
    {
        GatherSystemInfo();

        _lastFrameTime = Time.GetTicksUsec() / 1000000.0;

        LumoraLogger.Log("SystemInfoHook: Initialized");
        LumoraLogger.Log($"  GPU: {GPUName}");
        LumoraLogger.Log($"  CPU: {CPUName}");
        LumoraLogger.Log($"  OS: {OSName}");
        LumoraLogger.Log($"  Memory: {TotalMemoryMB} MB");
    }

    private void GatherSystemInfo()
    {
        GPUName = RenderingServer.GetVideoAdapterName();

        CPUName = OS.GetProcessorName();

        OSName = OS.GetName() + " " + OS.GetVersion();

        var memInfo = OS.GetMemoryInfo();
        if (memInfo.ContainsKey("physical"))
        {
            TotalMemoryMB = (long)memInfo["physical"] / (1024 * 1024);
        }

        var xrInterface = XRServer.FindInterface("OpenXR");
        if (xrInterface != null && xrInterface.IsInitialized())
        {
            OutputDevice = "OpenXR VR Headset";
        }
        else
        {
            OutputDevice = "Screen";
        }
    }

    public override void _Process(double delta)
    {
        double currentTime = Time.GetTicksUsec() / 1000000.0;
        double frameDelta = currentTime - _lastFrameTime;
        _lastFrameTime = currentTime;

        if (frameDelta > 0)
        {
            CurrentFPS = (float)(1.0 / frameDelta);
        }

        // rolling average over FPS_SAMPLE_FRAMES
        _deltaTimeAccumulator += frameDelta;
        _frameCount++;

        if (_frameCount >= FPS_SAMPLE_FRAMES)
        {
            AverageFPS = (float)(FPS_SAMPLE_FRAMES / _deltaTimeAccumulator);
            _deltaTimeAccumulator = 0;
            _frameCount = 0;
        }

        // GPU time (Godot 4.x doesn't expose direct GPU time easily)
        // Approximate from frame time
        GPUTimeMs = (float)(frameDelta * 1000.0);
    }

    public string GetDebugString()
    {
        return $"FPS: {CurrentFPS:F1} (Avg: {AverageFPS:F1}) | GPU: {GPUTimeMs:F2}ms | Device: {OutputDevice}";
    }
}

