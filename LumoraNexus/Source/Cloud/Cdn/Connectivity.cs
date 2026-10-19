// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// tracks if we have internet or if everything is fucked

using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Nexus.Cloud.Cdn;

public static class Connectivity
{
    private static volatile bool _isOnline = true;
    private static volatile bool _isChecking;
    private static DateTime _lastCheck = DateTime.MinValue;
    private static DateTime _lastFailure = DateTime.MinValue;
    private static int _consecutiveFailures;
    private static int _monitoring;
    private static string? _pingUrl;

    public static TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(5);
    public static TimeSpan BackoffDuration { get; set; } = TimeSpan.FromSeconds(30);
    public static int FailureThreshold { get; set; } = 3;

    // The accounts service's health check wherever the client is pointed, unless set.
    public static string PingUrl
    {
        get => _pingUrl ?? $"{ServiceConfig.Current.ApiBase}/health";
        set => _pingUrl = value;
    }

    // Round trip of the last successful health check, in milliseconds; -1 until one has succeeded.
    // The dash shows it as the ping to the services.
    public static int LastLatencyMs { get; private set; } = -1;

    public static bool IsOnline
    {
        get
        {
            if (_consecutiveFailures >= FailureThreshold)
            {
                if (DateTime.UtcNow - _lastFailure < BackoffDuration)
                    return false;
                _consecutiveFailures = 0;
            }
            // Offline, but long enough ago that the next call may go and find out. A success brings the
            // client back and a failure starts the wait again, so one bad minute can never leave it
            // offline for the rest of the run. -xlinka
            if (!_isOnline && DateTime.UtcNow - _lastFailure >= BackoffDuration)
                return true;
            return _isOnline;
        }
    }

    public static event Action<bool>? StatusChanged;

    public static void ReportSuccess()
    {
        _consecutiveFailures = 0;
        if (!_isOnline)
        {
            _isOnline = true;
            StatusChanged?.Invoke(true);
        }
    }

    public static void ReportFailure()
    {
        _lastFailure = DateTime.UtcNow;
        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if (failures >= FailureThreshold && _isOnline)
        {
            _isOnline = false;
            StatusChanged?.Invoke(false);
            EnsureMonitoring();
            _ = CheckConnectivityAsync();
        }
    }

    public static async Task<bool> CheckAsync() => await CheckConnectivityAsync(force: true);

    public static bool CheckSystemNetwork()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return true; // assume online if check fails
        }
    }

    private static async Task<bool> CheckConnectivityAsync(bool force = false)
    {
        if (_isChecking && !force)
            return _isOnline;

        if (!force && DateTime.UtcNow - _lastCheck < CheckInterval)
            return _isOnline;

        _isChecking = true;
        _lastCheck = DateTime.UtcNow;

        try
        {
            if (!CheckSystemNetwork())
            {
                SetOffline();
                return false;
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var response = await client.GetAsync(PingUrl);

            if (response.IsSuccessStatusCode)
            {
                LastLatencyMs = (int)watch.ElapsedMilliseconds;
                SetOnline();
                return true;
            }

            SetOffline();
            return false;
        }
        catch
        {
            SetOffline();
            return false;
        }
        finally
        {
            _isChecking = false;
        }
    }

    private static void SetOnline()
    {
        _consecutiveFailures = 0;
        if (!_isOnline)
        {
            _isOnline = true;
            StatusChanged?.Invoke(true);
        }
    }

    // Only going offline starts the wait. A health check that fails again while offline does not, or a
    // check pointed somewhere unreachable would hold real calls back for good.
    private static void SetOffline()
    {
        if (_isOnline)
        {
            _lastFailure = DateTime.UtcNow;
            _isOnline = false;
            StatusChanged?.Invoke(false);
            EnsureMonitoring();
        }
    }

    public static void StartMonitoring(TimeSpan interval)
    {
        if (Interlocked.Exchange(ref _monitoring, 1) == 0)
            _ = MonitorLoop(interval);
    }

    // Started the first time the client goes offline: something has to keep asking while it is, or
    // nothing waiting for the connection ever hears that it came back.
    private static void EnsureMonitoring() => StartMonitoring(CheckInterval);

    private static async Task MonitorLoop(TimeSpan interval)
    {
        while (true)
        {
            await Task.Delay(interval);
            if (!_isOnline)
                await CheckConnectivityAsync(force: true);
        }
    }
}
