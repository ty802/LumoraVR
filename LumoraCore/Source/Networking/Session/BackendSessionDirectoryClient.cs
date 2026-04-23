// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Publishes host-owned session metadata to the backend session directory.
/// </summary>
public sealed class BackendSessionDirectoryClient : IDisposable
{
    private readonly string _apiBaseUrl;
    private readonly Func<string?> _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private SessionMetadata? _metadata;
    private Func<string[]>? _getUserList;
    private bool _registered;

    public BackendSessionDirectoryClient(string apiBaseUrl, Func<string?> tokenProvider)
    {
        _apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:5178/api"
            : apiBaseUrl.TrimEnd('/');
        _tokenProvider = tokenProvider;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<bool> StartAsync(SessionMetadata metadata, Func<string[]> getUserList)
    {
        if (_registered)
            return true;

        _metadata = metadata;
        _getUserList = getUserList;

        if (!TryApplyAuthorization())
        {
            LumoraLogger.Warn("BackendSessionDirectoryClient: Cannot register public session without authentication");
            return false;
        }

        if (!await RegisterAsync())
            return false;

        _registered = true;
        _cts = new CancellationTokenSource();
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        return true;
    }

    public void SendHeartbeat(int activeUsers, string[] userList)
    {
        if (!_registered || _metadata == null)
            return;

        _metadata.ActiveUsers = activeUsers;
        _ = SendHeartbeatAsync(userList);
    }

    private async Task<bool> RegisterAsync()
    {
        if (_metadata == null)
            return false;

        try
        {
            using var content = CreateJsonContent(CreateRegistrationRequest());
            using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/sessions/register", content);
            if (!response.IsSuccessStatusCode)
            {
                LumoraLogger.Warn($"BackendSessionDirectoryClient: Register failed ({(int)response.StatusCode})");
                return false;
            }

            LumoraLogger.Log($"BackendSessionDirectoryClient: Registered session {_metadata.SessionId}");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"BackendSessionDirectoryClient: Register failed - {ex.Message}");
            return false;
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;

                await SendHeartbeatAsync(_getUserList?.Invoke() ?? Array.Empty<string>(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"BackendSessionDirectoryClient: Heartbeat loop failed - {ex.Message}");
            }
        }
    }

    private async Task SendHeartbeatAsync(string[] userList, CancellationToken cancellationToken = default)
    {
        if (_metadata == null || !TryApplyAuthorization())
            return;

        try
        {
            var request = new SessionHeartbeatRequestDto
            {
                ActiveUsers = _metadata.ActiveUsers,
                UserList = NormalizeUsers(userList),
                HasDirect = _metadata.SessionURLs.Count > 0,
                HasNat = true,
                HasRelay = false,
                Endpoints = CreateEndpoints(_metadata)
            };

            using var content = CreateJsonContent(request);
            using var response = await _httpClient.PatchAsync(
                $"{_apiBaseUrl}/sessions/{Uri.EscapeDataString(_metadata.SessionId)}/heartbeat",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LumoraLogger.Warn($"BackendSessionDirectoryClient: Heartbeat failed ({(int)response.StatusCode})");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"BackendSessionDirectoryClient: Heartbeat failed - {ex.Message}");
        }
    }

    private async Task RemoveAsync()
    {
        if (_metadata == null || !_registered || !TryApplyAuthorization())
            return;

        try
        {
            using var response = await _httpClient.DeleteAsync(
                $"{_apiBaseUrl}/sessions/{Uri.EscapeDataString(_metadata.SessionId)}");
            if (!response.IsSuccessStatusCode)
            {
                LumoraLogger.Warn($"BackendSessionDirectoryClient: Remove failed ({(int)response.StatusCode})");
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"BackendSessionDirectoryClient: Remove failed - {ex.Message}");
        }
    }

    private SessionRegistrationRequestDto CreateRegistrationRequest()
    {
        if (_metadata == null)
            throw new InvalidOperationException("Session metadata is not assigned");

        var users = _getUserList?.Invoke() ?? Array.Empty<string>();
        return new SessionRegistrationRequestDto
        {
            SessionIdentifier = _metadata.SessionId,
            WorldIdentifier = _metadata.SessionId,
            Name = _metadata.Name ?? "Unnamed Session",
            Description = _metadata.Description ?? "",
            HostUserId = _metadata.HostUserId ?? "",
            HostUsername = _metadata.HostUsername ?? Environment.UserName,
            ActiveUsers = global::System.Math.Max(1, _metadata.ActiveUsers),
            MaxUsers = global::System.Math.Max(1, _metadata.MaxUsers),
            AccessLevel = _metadata.Visibility.ToString(),
            Tags = _metadata.Tags?.ToArray() ?? Array.Empty<string>(),
            UserList = NormalizeUsers(users),
            IsHeadless = _metadata.IsHeadless,
            Version = "Lumora",
            VersionHash = _metadata.VersionHash ?? "",
            Direct = _metadata.SessionURLs.Count > 0,
            HasDirect = _metadata.SessionURLs.Count > 0,
            HasNat = true,
            HasRelay = false,
            Region = "default",
            ThumbnailUrl = _metadata.ThumbnailUrl,
            Endpoints = CreateEndpoints(_metadata)
        };
    }

    private bool TryApplyAuthorization()
    {
        var token = _tokenProvider.Invoke();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    private StringContent CreateJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string[] NormalizeUsers(string[]? users)
    {
        if (users == null || users.Length == 0)
            return Array.Empty<string>();

        return users
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .Select(user => user.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();
    }

    private static SessionConnectionEndpointDto[] CreateEndpoints(SessionMetadata metadata)
    {
        return metadata.SessionURLs
            .Where(uri => uri != null)
            .Select(uri => new SessionConnectionEndpointDto
            {
                Kind = uri.Scheme == "lnl" ? "direct" : uri.Scheme,
                Url = uri.ToString(),
                Priority = uri.Scheme == "lnl" ? 10 : 100,
                Region = "default"
            })
            .Take(8)
            .ToArray();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _heartbeatTask?.Wait(TimeSpan.FromMilliseconds(1000));
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
        _heartbeatTask = null;

        if (_registered)
        {
            try
            {
                RemoveAsync().Wait(TimeSpan.FromMilliseconds(1500));
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"BackendSessionDirectoryClient: Remove during dispose failed - {ex.Message}");
            }
        }

        _httpClient.Dispose();
    }

    private sealed class SessionRegistrationRequestDto
    {
        public string? SessionIdentifier { get; set; }
        public string? WorldIdentifier { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? HostUserId { get; set; }
        public string? HostUsername { get; set; }
        public int ActiveUsers { get; set; }
        public int MaxUsers { get; set; }
        public string AccessLevel { get; set; } = "Public";
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string[] UserList { get; set; } = Array.Empty<string>();
        public bool IsHeadless { get; set; }
        public string? Version { get; set; }
        public string? VersionHash { get; set; }
        public bool Direct { get; set; }
        public bool HasDirect { get; set; }
        public bool HasNat { get; set; } = true;
        public bool HasRelay { get; set; }
        public string? Region { get; set; }
        public string? ThumbnailUrl { get; set; }
        public SessionConnectionEndpointDto[] Endpoints { get; set; } = Array.Empty<SessionConnectionEndpointDto>();
    }

    private sealed class SessionHeartbeatRequestDto
    {
        public int? ActiveUsers { get; set; }
        public string[]? UserList { get; set; }
        public bool? HasDirect { get; set; }
        public bool? HasNat { get; set; }
        public bool? HasRelay { get; set; }
        public SessionConnectionEndpointDto[]? Endpoints { get; set; }
    }

    private sealed class SessionConnectionEndpointDto
    {
        public string Kind { get; set; } = "direct";
        public string Url { get; set; } = "";
        public int Priority { get; set; } = 100;
        public string? Region { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
