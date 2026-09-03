// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// handles all the cloud shit with compression and parallel uploads

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lumora.Nexus.Cloud.Cdn;

public sealed class LumoraClient : IDisposable
{
    private readonly HttpClient _api;
    private readonly HttpClient _content;
    private readonly JsonSerializerOptions _json;
    private readonly string _deviceId;
    private Session? _session;

    // Per-login ACCOUNT keypair for join verification. Generated fresh each sign-in, private half
    // stays in memory here, public half is published to the backend so a game host can fetch it and verify
    // we are who we claim. Cleared on sign-out. -xlinka
    private RSA? _accountKey;
    private string? _accountUserId;
    private string? _accountSessionId;

    public const int DefaultChunkSize = 4 * 1024 * 1024; // 4MB chunks
    public const int MaxParallelChunks = 4;

    public static TimeSpan ApiTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public static TimeSpan ContentTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public static bool EnableCompression { get; set; } = false;

    public string DeviceId => _deviceId;
    public Session? CurrentSession => _session;
    public bool IsAuthenticated => _session != null && !string.IsNullOrEmpty(_session.Token);

    // JWT jti once signed in, else null
    public string? AccountSessionId => _accountSessionId;
    public string? AccountUserId => _accountUserId;
    // DER SubjectPublicKeyInfo, else null
    public byte[]? AccountPublicKey => _accountKey?.ExportSubjectPublicKeyInfo();
    public bool HasAccountIdentity => _accountKey != null && !string.IsNullOrEmpty(_accountUserId) && !string.IsNullOrEmpty(_accountSessionId);

    // signs a host's join challenge nonce
    public byte[]? SignWithAccountKey(byte[] nonce)
        => _accountKey?.SignData(nonce, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    public event Action<Session>? Authenticated;
    public event Action? SignedOut;

    public LumoraClient(string deviceId, string appName = "LumoraVR", string version = "0.1.0")
    {
        _deviceId = deviceId;
        var userAgent = new ProductInfoHeaderValue(appName, version);

        var apiHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        _api = new HttpClient(apiHandler) { Timeout = ApiTimeout };
        _api.DefaultRequestHeaders.UserAgent.Add(userAgent);
        _api.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        _api.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _api.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

        var contentHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        _content = new HttpClient(contentHandler) { Timeout = ContentTimeout };
        _content.DefaultRequestHeaders.UserAgent.Add(userAgent);
        _content.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        _content.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public void Dispose()
    {
        _accountKey?.Dispose();
        _api.Dispose();
        _content.Dispose();
    }

    #region Auth

    public async Task<ApiResponse<Session>> SignIn(string username, string password, bool remember = false, string? twoFactorCode = null)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<Session>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        object payload = string.IsNullOrEmpty(twoFactorCode)
            ? new { Username = username, Password = password, Remember = remember, DeviceId = _deviceId }
            : new { Username = username, Password = password, Remember = remember, DeviceId = _deviceId, TwoFactorCode = twoFactorCode };

        var result = await PostAsync<Session>($"{ServiceConfig.Current.ApiBase}/api/user/login", payload);

        if (result.Success && result.Data != null)
        {
            ApplySession(result.Data);
            await EstablishAccountIdentityAsync(result.Data);
        }

        return result;
    }

    public async Task<ApiResponse<Session>> SignInWithToken(string userId, string token)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<Session>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        var payload = new { UserId = userId, Token = token, DeviceId = _deviceId };
        var result = await PostAsync<Session>($"{ServiceConfig.Current.ApiBase}/api/user/token", payload);

        if (result.Success && result.Data != null)
        {
            ApplySession(result.Data);
            await EstablishAccountIdentityAsync(result.Data);
        }

        return result;
    }

    public async Task<ApiResponse> SignOut()
    {
        if (!IsAuthenticated)
            return ApiResponse.Ok();

        if (!Connectivity.IsOnline)
        {
            ClearSession();
            return ApiResponse.Ok("Signed out locally (offline)");
        }

        var result = await PostAsync($"{ServiceConfig.Current.ApiBase}/api/user/logout", null);
        ClearSession();
        return result;
    }

    public void SetSession(Session session) => ApplySession(session);

    private void ApplySession(Session session)
    {
        _session = session;
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Authenticated?.Invoke(session);
    }

    private void ClearSession()
    {
        _session = null;
        _accountKey?.Dispose();
        _accountKey = null;
        _accountUserId = null;
        _accountSessionId = null;
        _api.DefaultRequestHeaders.Authorization = null;
        // Signing out takes the group card off this machine's nametag immediately rather than leaving the
        // last account's group hanging over whoever plays next. -xlinka
        SetRepresentedGroup(null);
        SignedOut?.Invoke();
    }

    // Generate this login's account keypair and publish the public half so a game host can fetch it and
    // verify our account when we join. Best-effort: if it can't complete (no session id, offline, etc.) we
    // just have no account identity and join falls back to machine-key only (guest). -xlinka
    private async Task EstablishAccountIdentityAsync(Session session)
    {
        try
        {
            if (string.IsNullOrEmpty(session.SessionId))
                return; // backend didn't hand us a session id, can't bind a key to it

            var me = await GetCurrentUser();
            if (me.Failed || me.Data == null || string.IsNullOrEmpty(me.Data.Id))
                return;

            _accountKey?.Dispose();
            _accountKey = RSA.Create(2048);
            _accountUserId = me.Data.Id;
            _accountSessionId = session.SessionId;

            var publicKey = Convert.ToBase64String(_accountKey.ExportSubjectPublicKeyInfo());
            var publish = await PutAsync(
                $"{ServiceConfig.Current.ApiBase}/api/user/{_accountUserId}/rsa/{_accountSessionId}",
                new { PublicKey = publicKey });

            if (publish.Failed)
            {
                // couldn't publish, so the key is useless to a host. Drop it so HasAccountIdentity is honest.
                _accountKey?.Dispose();
                _accountKey = null;
                _accountUserId = null;
                _accountSessionId = null;
            }
        }
        catch
        {
            _accountKey?.Dispose();
            _accountKey = null;
            _accountUserId = null;
            _accountSessionId = null;
        }

        // Pick up the group this account wears, so the very first world we join already carries the card.
        // Deliberately outside the try above: a failure here is not a reason to throw the account key away.
        try { await RefreshRepresentedGroupAsync(); } catch { /* no card is a fine outcome */ }
    }

    // DER SubjectPublicKeyInfo bytes; null if there's no key or the call fails
    public async Task<byte[]?> GetSessionPublicKeyAsync(string userId, string sessionId)
    {
        var result = await GetAsync<SessionKeyInfo>($"{ServiceConfig.Current.ApiBase}/api/user/{userId}/rsa/{sessionId}");
        if (result.Failed || result.Data == null || string.IsNullOrEmpty(result.Data.PublicKey))
            return null;
        try { return Convert.FromBase64String(result.Data.PublicKey); }
        catch { return null; }
    }

    // Fetch a user's public record, including PLATFORM moderation ban status, so a game host can gate a
    // join. (World bans are separate and live host-side in BanManager.) Null if the call fails. -xlinka
    public Task<ApiResponse<PublicUserInfo>> GetPlatformUserAsync(string userId)
        => GetAsync<PublicUserInfo>($"{ServiceConfig.Current.ApiBase}/api/user/{userId}");

    #endregion

    #region User Profile

    public Task<ApiResponse<UserProfile>> GetCurrentUser()
        => GetAsync<UserProfile>($"{ServiceConfig.Current.ApiBase}/api/user/me");

    // Fetch another user's public profile (bio, status, badges, ban status) by id. -xlinka
    public Task<ApiResponse<PublicUserInfo>> GetPublicUser(string userId)
        => GetAsync<PublicUserInfo>($"{ServiceConfig.Current.ApiBase}/api/user/{userId}");

    public async Task<ApiResponse> UpdateProfile(string bio, string status)
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        var payload = new { Bio = bio, Status = status };
        return await PutAsync($"{ServiceConfig.Current.ApiBase}/api/user/profile", payload);
    }

    public async Task<ApiResponse> ChangePassword(string currentPassword, string newPassword)
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        var payload = new { CurrentPassword = currentPassword, NewPassword = newPassword };
        return await PostAsync($"{ServiceConfig.Current.ApiBase}/api/user/reset-password", payload);
    }

    public async Task<ApiResponse<TwoFactorSetup>> Enable2FA()
    {
        if (!IsAuthenticated)
            return ApiResponse<TwoFactorSetup>.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        return await PostAsync<TwoFactorSetup>($"{ServiceConfig.Current.ApiBase}/api/user/enable2fa", null);
    }

    public async Task<ApiResponse> Verify2FA(string code)
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        var payload = new { Code = code };
        return await PostAsync($"{ServiceConfig.Current.ApiBase}/api/user/verify2fa", payload);
    }

    public async Task<ApiResponse> Disable2FA(string code)
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        var payload = new { Code = code };
        return await PostAsync($"{ServiceConfig.Current.ApiBase}/api/user/disable2fa", payload);
    }

    #endregion

    #region Inventory

    // Resolve our own user id for the inventory/asset calls. We learn it once at sign-in (cached as
    // _accountUserId), so normally this is free; only if that didn't run do we fall back to a single
    // /api/user/me round-trip and cache the result - instead of paying for that round-trip on EVERY call
    // like the old per-method copy-paste did. -xlinka
    private async Task<ApiResponse<string>> ResolveUserIdAsync()
    {
        if (!IsAuthenticated)
            return ApiResponse<string>.Fail(HttpStatusCode.Unauthorized, "Not authenticated");

        if (!string.IsNullOrEmpty(_accountUserId))
            return ApiResponse<string>.Ok(_accountUserId!);

        var me = await GetCurrentUser();
        if (me.Failed || me.Data == null || string.IsNullOrEmpty(me.Data.Id))
            return ApiResponse<string>.Fail(me.Status, me.Message);

        _accountUserId = me.Data.Id;
        return ApiResponse<string>.Ok(_accountUserId!);
    }

    public async Task<ApiResponse<InventoryResponse>> GetInventory()
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<InventoryResponse>.Fail(id.Status, id.Message);

        return await GetAsync<InventoryResponse>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}");
    }

    public async Task<ApiResponse<List<AssetRef>>> GetInventoryByType(AssetType type)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<List<AssetRef>>.Fail(id.Status, id.Message);

        var typePath = type switch
        {
            AssetType.Avatar => "avatars",
            AssetType.World => "worlds",
            AssetType.Prop => "props",
            _ => "props"
        };

        return await GetAsync<List<AssetRef>>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/{typePath}");
    }

    public async Task<ApiResponse<List<UserFolder>>> GetFolders()
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<List<UserFolder>>.Fail(id.Status, id.Message);

        return await GetAsync<List<UserFolder>>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders");
    }

    public async Task<ApiResponse<UserFolder>> GetFolder(string folderId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<UserFolder>.Fail(id.Status, id.Message);

        return await GetAsync<UserFolder>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders/{folderId}");
    }

    public async Task<ApiResponse<List<AssetInfo>>> GetUserAssets()
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<List<AssetInfo>>.Fail(id.Status, id.Message);

        return await GetAsync<List<AssetInfo>>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/assets");
    }

    public async Task<ApiResponse<UserQuotaResponse>> GetQuota()
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<UserQuotaResponse>.Fail(id.Status, id.Message);

        return await GetAsync<UserQuotaResponse>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/quota");
    }

    public async Task<ApiResponse<UserFolder>> CreateFolder(string name, string? parentFolderId = null)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<UserFolder>.Fail(id.Status, id.Message);

        var payload = new { Name = name, ParentFolderId = parentFolderId };
        return await PostAsync<UserFolder>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders", payload);
    }

    public async Task<ApiResponse> AddAsset(string assetId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);

        return await PostAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/assets/{assetId}", null);
    }

    public async Task<ApiResponse> MoveAsset(string assetId, string? sourceFolderId, string targetFolderId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);

        var payload = new { SourceFolderId = sourceFolderId, TargetFolderId = targetFolderId };
        return await PostAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/assets/{assetId}/move", payload);
    }

    public async Task<ApiResponse> UpdateAsset(string assetId, AssetRef assetRef)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);

        return await PutAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/assets/{assetId}", assetRef);
    }

    public async Task<ApiResponse> RemoveAsset(string assetId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);

        return await DeleteAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/assets/{assetId}");
    }

    // The folder browsing shape the inventory screen works with: one folder's direct children, or a
    // search across the whole tree. "root" names the top of the inventory.
    public const string InventoryRoot = "root";

    public async Task<ApiResponse<FolderContents>> GetFolderContents(string? folderId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<FolderContents>.Fail(id.Status, id.Message);
        var folder = string.IsNullOrEmpty(folderId) ? InventoryRoot : folderId;
        return await GetAsync<FolderContents>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders/{folder}/contents");
    }

    public async Task<ApiResponse<FolderContents>> SearchInventory(string query)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<FolderContents>.Fail(id.Status, id.Message);
        return await GetAsync<FolderContents>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/search?q={Uri.EscapeDataString(query)}");
    }

    // A blob already in the content store becomes an inventory item in one call. kind: Avatar | Object | World.
    public async Task<ApiResponse<AssetRef>> AddInventoryItem(string hash, string name, string kind, string? folderId,
        string? thumbnailHash, long sizeBytes, string? mode = null, List<string>? tags = null, List<AssetManifestEntry>? manifest = null)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse<AssetRef>.Fail(id.Status, id.Message);
        var payload = new
        {
            Hash = hash,
            Name = name,
            Kind = kind,
            FolderId = string.IsNullOrEmpty(folderId) || folderId == InventoryRoot ? null : folderId,
            ThumbnailHash = thumbnailHash,
            Tags = tags,
            SizeBytes = sizeBytes,
            Mode = mode,
            Manifest = manifest,
        };
        return await PostAsync<AssetRef>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/items", payload);
    }

    public async Task<ApiResponse> RenameInventoryItem(string assetId, string name)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);
        var result = await PatchAsync<AssetRef>($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/items/{assetId}", new { Name = name });
        return result.Success ? ApiResponse.Ok() : ApiResponse.Fail(result.Status, result.Message);
    }

    public async Task<ApiResponse> DeleteInventoryItem(string assetId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);
        return await DeleteAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/items/{assetId}");
    }

    public async Task<ApiResponse> RenameFolder(string folderId, string name)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);
        return await PostAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders/{folderId}/rename", new { Name = name });
    }

    public async Task<ApiResponse> MoveFolder(string folderId, string? targetFolderId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);
        var target = string.IsNullOrEmpty(targetFolderId) || targetFolderId == InventoryRoot ? null : targetFolderId;
        return await PostAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders/{folderId}/move", new { TargetFolderId = target });
    }

    public async Task<ApiResponse> DeleteFolder(string folderId)
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || id.Data == null)
            return ApiResponse.Fail(id.Status, id.Message);
        return await DeleteAsync($"{ServiceConfig.Current.ContentBase}/api/inventory/users/{id.Data}/folders/{folderId}");
    }

    // Moving an item into the root: the move route wants the root named, and the service calls it "root".
    public Task<ApiResponse> MoveItemToFolder(string assetId, string? sourceFolderId, string? targetFolderId)
        => MoveAsset(assetId,
            string.IsNullOrEmpty(sourceFolderId) || sourceFolderId == InventoryRoot ? null : sourceFolderId,
            string.IsNullOrEmpty(targetFolderId) ? InventoryRoot : targetFolderId);

    #endregion

    #region Variants

    public const string WorkerKeyHeader = "X-Lumora-Worker-Key";

    // What the service knows about one variant of one blob. Asking is what queues it, so a first ask
    // comes back Pending and a later one Ready. Skipped means the variant does not apply to that
    // source and the original is the answer. -xlinka
    public async Task<ApiResponse<VariantState>> GetVariantState(string hash, string variantId)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<VariantState>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");
        try
        {
            using var response = await _content.GetAsync($"{ServiceConfig.Current.ContentBase}/api/variants/{hash}/{variantId}");
            if (response.StatusCode == HttpStatusCode.NoContent)
                return ApiResponse<VariantState>.Ok(new VariantState { VariantId = variantId, State = "Skipped" });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Accepted)
                return ApiResponse<VariantState>.Fail(response.StatusCode, ErrorMessage(body));
            var state = JsonSerializer.Deserialize<VariantState>(body, _json) ?? new VariantState { VariantId = variantId };
            return ApiResponse<VariantState>.Ok(state);
        }
        catch (Exception ex)
        {
            return ApiResponse<VariantState>.Fail(HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    // WORKER SIDE: a shared key instead of an account.

    public async Task<ApiResponse<VariantJob>> WorkerNextVariant(string workerKey, string workerName)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{ServiceConfig.Current.ContentBase}/api/variants/queue/next?worker={Uri.EscapeDataString(workerName)}");
            request.Headers.Add(WorkerKeyHeader, workerKey);
            using var response = await _content.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NoContent)
                return ApiResponse<VariantJob>.Ok(null!);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return ApiResponse<VariantJob>.Fail(response.StatusCode, ErrorMessage(body));
            return ApiResponse<VariantJob>.Ok(JsonSerializer.Deserialize<VariantJob>(body, _json)!);
        }
        catch (Exception ex)
        {
            return ApiResponse<VariantJob>.Fail(HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    // bytes null with skipped = true when the variant does not apply; bytes null with an error when
    // it could not be made.
    public async Task<ApiResponse> WorkerFinishVariant(string workerKey, string hash, string variantId, byte[]? bytes, bool skipped = false, string? error = null)
    {
        try
        {
            var url = $"{ServiceConfig.Current.ContentBase}/api/variants/{hash}/{variantId}/finish";
            if (skipped)
                url += "?skipped=true";
            else if (!string.IsNullOrEmpty(error))
                url += "?error=" + Uri.EscapeDataString(error!);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add(WorkerKeyHeader, workerKey);
            if (bytes != null && !skipped && string.IsNullOrEmpty(error))
            {
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            using var response = await _content.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return ApiResponse.Ok();
            return ApiResponse.Fail(response.StatusCode, ErrorMessage(await response.Content.ReadAsStringAsync()));
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    // A raw blob into the store under the system owner, by worker key. The publisher uses it for the
    // engine's built-in assets.
    public async Task<ApiResponse<string>> WorkerStoreBlob(string workerKey, byte[] bytes, string extension)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{ServiceConfig.Current.ContentBase}/api/variants/blob?extension={Uri.EscapeDataString(extension)}");
            request.Headers.Add(WorkerKeyHeader, workerKey);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await _content.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return ApiResponse<string>.Fail(response.StatusCode, ErrorMessage(body));
            using var doc = JsonDocument.Parse(body);
            var hash = doc.RootElement.TryGetProperty("hash", out var h) ? h.GetString() : null;
            return string.IsNullOrEmpty(hash)
                ? ApiResponse<string>.Fail(HttpStatusCode.InternalServerError, "No hash in the answer")
                : ApiResponse<string>.Ok(hash!);
        }
        catch (Exception ex)
        {
            return ApiResponse<string>.Fail(HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    #endregion

    #region Groups

    // biggest groups first; optional name search
    public Task<ApiResponse<List<GroupInfo>>> GetGroups(string? query = null, int skip = 0, int take = 25)
    {
        var url = $"{ServiceConfig.Current.ApiBase}/api/groups?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        return GetAsync<List<GroupInfo>>(url);
    }

    public Task<ApiResponse<List<GroupInfo>>> GetMyGroups()
        => GetAsync<List<GroupInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/mine");

    // MyRole is null if the caller isn't a member
    public Task<ApiResponse<GroupInfo>> GetGroup(string groupId)
        => GetAsync<GroupInfo>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}");

    // visibility: Public | Private | Hidden; needs a free group allocation
    public async Task<ApiResponse<GroupInfo>> CreateGroup(string name, string description = "", string visibility = "Public")
    {
        if (!IsAuthenticated)
            return ApiResponse<GroupInfo>.Fail(HttpStatusCode.Unauthorized, "Not authenticated");
        var payload = new { Name = name, Description = description, Visibility = visibility };
        return await PostAsync<GroupInfo>($"{ServiceConfig.Current.ApiBase}/api/groups", payload);
    }

    // Public joins outright, private files a request, hidden needs an invite. An invite to either lets the
    // caller straight in, so the answer says which of the two happened rather than the page guessing from
    // the group's visibility. Accepting an invite IS this call. -xlinka
    public Task<ApiResponse<GroupJoinResult>> JoinGroup(string groupId)
        => PostAsync<GroupJoinResult>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/join", null);

    // owners must transfer or delete the group instead of leaving
    public Task<ApiResponse> LeaveGroup(string groupId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/membership");

    public Task<ApiResponse<List<GroupMemberInfo>>> GetGroupMembers(string groupId)
        => GetAsync<List<GroupMemberInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/members");

    // Admin+; role is one rung below the caller: Moderator | Builder | Member (the Owner may also set Admin).
    // Owner is not settable here, transfer is its own route.
    public Task<ApiResponse> SetGroupRole(string groupId, string userId, string role)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/members/{userId}/role", new { Role = role });

    // moderator+ only
    public Task<ApiResponse> KickGroupMember(string groupId, string userId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/members/{userId}");

    // moderator+ only
    public Task<ApiResponse<List<GroupJoinRequestInfo>>> GetGroupRequests(string groupId)
        => GetAsync<List<GroupJoinRequestInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/requests");

    // moderator+ only
    public Task<ApiResponse> ApproveGroupRequest(string groupId, string userId)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/requests/{userId}/approve", null);

    // moderator+ only
    public Task<ApiResponse> DenyGroupRequest(string groupId, string userId)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/requests/{userId}/deny", null);

    // owner only
    public Task<ApiResponse> TransferGroup(string groupId, string newOwnerUserId)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/transfer/{newOwnerUserId}", null);

    // owner only
    public Task<ApiResponse> DeleteGroup(string groupId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}");

    // Admin+. Send only what changed: every field is optional server-side and a null is left alone, so a
    // patch built from the edit form never has to carry the values nobody touched. -xlinka
    public Task<ApiResponse<GroupInfo>> UpdateGroup(string groupId, GroupPatch patch)
        => PatchAsync<GroupInfo>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}", patch);

    // Admin+
    public Task<ApiResponse<List<GroupBanInfo>>> GetGroupBans(string groupId)
        => GetAsync<List<GroupBanInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/bans");

    // Admin+; also drops the membership, the join request and any invite in the same call
    public Task<ApiResponse> BanGroupMember(string groupId, string userId, string? reason = null)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/bans/{userId}", new { Reason = reason ?? "" });

    // Admin+
    public Task<ApiResponse> UnbanGroupMember(string groupId, string userId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/bans/{userId}");

    // Admin+; the sum of every slice has to stay inside the pool or this comes back 409
    public Task<ApiResponse> SetGroupMemberStorage(string groupId, string userId, long bytes)
        => PutAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/members/{userId}/storage", new { Bytes = bytes });

    // moderator+ only
    public Task<ApiResponse<List<GroupInviteInfo>>> GetGroupInvites(string groupId)
        => GetAsync<List<GroupInviteInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/invites");

    // moderator+ only
    public Task<ApiResponse> InviteToGroup(string groupId, string userId)
        => PostAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/invites/{userId}", null);

    // Moderator+ withdrawing someone else's, or the invited user declining their own.
    public Task<ApiResponse> WithdrawGroupInvite(string groupId, string userId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/invites/{userId}");

    // A different shape from a group's own invite list: this one is about the groups, that one is about
    // the people.
    public Task<ApiResponse<List<GroupInviteSummary>>> GetMyGroupInvites()
        => GetAsync<List<GroupInviteSummary>>($"{ServiceConfig.Current.ApiBase}/api/groups/invites/mine");

    // members only; upcoming drops the ones that already ended
    public Task<ApiResponse<List<GroupEventInfo>>> GetGroupEvents(string groupId, bool upcoming = true)
        => GetAsync<List<GroupEventInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/events?upcoming={(upcoming ? "true" : "false")}");

    // builder+ only
    public Task<ApiResponse<GroupEventInfo>> CreateGroupEvent(string groupId, string title, string description,
        string worldName, DateTime startsAt, DateTime? endsAt = null)
    {
        var payload = new
        {
            Title = title,
            Description = description,
            WorldName = worldName,
            StartsAt = startsAt,
            EndsAt = endsAt,
        };
        return PostAsync<GroupEventInfo>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/events", payload);
    }

    // the event's host, or moderator+
    public Task<ApiResponse> DeleteGroupEvent(string groupId, string eventId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/events/{eventId}");

    // members only, newest first
    public Task<ApiResponse<List<GroupAnnouncementInfo>>> GetGroupAnnouncements(string groupId, int take = 25)
        => GetAsync<List<GroupAnnouncementInfo>>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/announcements?take={take}");

    // builder+ only
    public Task<ApiResponse<GroupAnnouncementInfo>> PostGroupAnnouncement(string groupId, string text)
        => PostAsync<GroupAnnouncementInfo>($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/announcements", new { Text = text });

    // the author, or moderator+
    public Task<ApiResponse> DeleteGroupAnnouncement(string groupId, string announcementId)
        => DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/announcements/{announcementId}");

    // REPRESENTATION
    //
    // The group a user wears. The account service stores one id per account; what the nametag needs is the
    // resolved card (tag, colour, icon, the wearer's role), and the only place that comes back assembled is
    // the caller's own public profile - so a successful represent call is followed by a profile read rather
    // than by guessing the card from whatever list row was on screen. -xlinka

    public RepresentedGroupInfo? RepresentedGroup { get; private set; }

    public event Action<RepresentedGroupInfo?>? RepresentedGroupChanged;

    // member only
    public async Task<ApiResponse> RepresentGroup(string groupId)
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");
        var result = await PutAsync($"{ServiceConfig.Current.ApiBase}/api/groups/{groupId}/represent", null);
        if (result.Success)
            await RefreshRepresentedGroupAsync();
        return result;
    }

    public async Task<ApiResponse> ClearRepresentedGroup()
    {
        if (!IsAuthenticated)
            return ApiResponse.Fail(HttpStatusCode.Unauthorized, "Not authenticated");
        var result = await DeleteAsync($"{ServiceConfig.Current.ApiBase}/api/groups/represent");
        if (result.Success)
            await RefreshRepresentedGroupAsync();
        return result;
    }

    // Re-read the card off our own public profile. Best effort: a failed read leaves the last known card
    // alone rather than blanking somebody's nametag because one request timed out.
    public async Task RefreshRepresentedGroupAsync()
    {
        var id = await ResolveUserIdAsync();
        if (id.Failed || string.IsNullOrEmpty(id.Data))
            return;

        var me = await GetPublicUser(id.Data!);
        if (me.Failed || me.Data == null)
            return;

        SetRepresentedGroup(me.Data.RepresentedGroup);
    }

    private void SetRepresentedGroup(RepresentedGroupInfo? group)
    {
        var current = RepresentedGroup;
        if (current == null && group == null)
            return;
        if (current != null && group != null && current == group)
            return; // records compare by value, so an unchanged profile read raises nothing
        RepresentedGroup = group;
        RepresentedGroupChanged?.Invoke(group);
    }

    #endregion

    #region Content

    public Task<ApiResponse<ContentInfo>> GetContentInfo(string hash)
        => GetAsync<ContentInfo>($"{ServiceConfig.Current.ContentBase}/content/{hash}");

    public async Task<bool> ContentExists(string hash)
    {
        if (!Connectivity.IsOnline)
            return false;

        var url = ServiceConfig.Current.GetContentUrl(hash);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _content.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            Connectivity.ReportFailure();
            return false;
        }
    }

    public async Task<ApiResponse<byte[]>> FetchContent(string hash, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<byte[]>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        var url = ServiceConfig.Current.GetContentUrl(hash);
        var state = new TransferProgress { Hash = hash, State = TransferState.Active };

        try
        {
            using var response = await _content.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                ReportDownload(progress, state with { State = TransferState.Error });
                return ApiResponse<byte[]>.Fail(response.StatusCode);
            }

            Connectivity.ReportSuccess();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            state = state with { TotalBytes = totalBytes };

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();

            // pooled buffer so we dont shit on the gc
            var chunk = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                long received = 0;
                int read;

                while ((read = await stream.ReadAsync(chunk.AsMemory(0, 81920), ct)) > 0)
                {
                    await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
                    received += read;
                    ReportDownload(progress, state with { TransferredBytes = received });
                }

                ReportDownload(progress, state with { TransferredBytes = received, State = TransferState.Completed });
                return ApiResponse<byte[]>.Ok(buffer.ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            ReportDownload(progress, state with { State = TransferState.Cancelled });
            return ApiResponse<byte[]>.Fail(HttpStatusCode.RequestTimeout, "Cancelled");
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            ReportDownload(progress, state with { State = TransferState.Error });
            return ApiResponse<byte[]>.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            ReportDownload(progress, state with { State = TransferState.Error });
            return ApiResponse<byte[]>.FromException(ex);
        }
    }

    public Task<ApiResponse<byte[]>> FetchContent(Uri uri, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var hash = ContentHash.ParseHash(uri);
        return FetchContent(hash, progress, ct);
    }

    public async Task<ApiResponse<ContentInfo>> StoreContent(string filePath, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return ApiResponse<ContentInfo>.Fail(HttpStatusCode.NotFound, "File not found");

        var fileInfo = new FileInfo(filePath);
        var mime = MimeTypes.FromExtension(fileInfo.Extension);

        await using var stream = File.OpenRead(filePath);
        var hash = ContentHash.FromStream(stream);
        stream.Position = 0;

        return await StoreContentInternal(stream, hash, mime, fileInfo.Extension, progress, ct);
    }

    public async Task<ApiResponse<ContentInfo>> StoreContent(byte[] data, string mimeType, string? extension = null, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var hash = ContentHash.FromBytes(data);
        extension ??= MimeTypes.ToExtension(mimeType);

        using var stream = new MemoryStream(data);
        return await StoreContentInternal(stream, hash, mimeType, extension, progress, ct);
    }

    private async Task<ApiResponse<ContentInfo>> StoreContentInternal(Stream stream, string hash, string mimeType, string? extension, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<ContentInfo>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        var state = new TransferProgress { Hash = hash, TotalBytes = stream.Length, State = TransferState.Queued };

        try
        {
            if (await ContentExists(hash))
            {
                var existing = await GetContentInfo(hash);
                if (existing.Success)
                {
                    ReportUpload(progress, state with { TransferredBytes = state.TotalBytes, State = TransferState.Completed });
                    return existing;
                }
            }

            var initPayload = new { Hash = hash, ContentType = mimeType, Extension = extension, Size = stream.Length };
            var initResult = await PostAsync<UploadHandle>($"{ServiceConfig.Current.ContentBase}/content/upload/begin", initPayload);

            if (!initResult.Success || initResult.Data == null)
                return ApiResponse<ContentInfo>.Fail(initResult.Status, initResult.Message);

            var handle = initResult.Data;
            var chunkSize = handle.ChunkSize > 0 ? handle.ChunkSize : DefaultChunkSize;

            state = state with { State = TransferState.Active };
            ReportUpload(progress, state);

            var chunks = new List<(int Index, byte[] Data)>();
            var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                int chunkIndex = 0;
                while (stream.Position < stream.Length)
                {
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), ct);
                    if (bytesRead == 0) break;

                    var chunkData = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunkData, 0, bytesRead);
                    chunks.Add((chunkIndex++, chunkData));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            long transferred = 0;
            var semaphore = new SemaphoreSlim(MaxParallelChunks);
            var uploadTasks = new List<Task<bool>>();

            foreach (var (index, chunkData) in chunks)
            {
                await semaphore.WaitAsync(ct);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        using var chunkContent = new ByteArrayContent(chunkData);
                        chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                        var chunkUrl = $"{ServiceConfig.Current.ContentBase}/content/upload/{handle.Id}/chunk/{index}";
                        using var chunkResponse = await _api.PutAsync(chunkUrl, chunkContent, ct);

                        if (chunkResponse.IsSuccessStatusCode)
                        {
                            Interlocked.Add(ref transferred, chunkData.Length);
                            ReportUpload(progress, state with { TransferredBytes = Interlocked.Read(ref transferred) });
                            return true;
                        }
                        return false;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);

                uploadTasks.Add(task);
            }

            var results = await Task.WhenAll(uploadTasks);
            if (Array.Exists(results, r => !r))
            {
                ReportUpload(progress, state with { State = TransferState.Error });
                return ApiResponse<ContentInfo>.Fail(HttpStatusCode.InternalServerError, "Chunk upload failed");
            }

            Connectivity.ReportSuccess();

            var finishResult = await PostAsync<ContentInfo>($"{ServiceConfig.Current.ContentBase}/content/upload/{handle.Id}/finish", null);

            ReportUpload(progress, state with
            {
                TransferredBytes = stream.Length,
                State = finishResult.Success ? TransferState.Completed : TransferState.Error
            });

            return finishResult;
        }
        catch (OperationCanceledException)
        {
            ReportUpload(progress, state with { State = TransferState.Cancelled });
            return ApiResponse<ContentInfo>.Fail(HttpStatusCode.RequestTimeout, "Cancelled");
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            ReportUpload(progress, state with { State = TransferState.Error });
            return ApiResponse<ContentInfo>.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            ReportUpload(progress, state with { State = TransferState.Error });
            return ApiResponse<ContentInfo>.FromException(ex);
        }
    }

    #endregion

    #region HTTP

    private async Task<ApiResponse> GetAsync(string url)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var response = await _api.GetAsync(url);
            Connectivity.ReportSuccess();
            var body = await response.Content.ReadAsStringAsync();
            return new ApiResponse
            {
                Success = response.IsSuccessStatusCode,
                Status = response.StatusCode,
                RawBody = body,
                Message = response.IsSuccessStatusCode ? null : ErrorMessage(body)
            };
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse.FromException(ex);
        }
    }

    private async Task<ApiResponse<T>> GetAsync<T>(string url)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var response = await _api.GetAsync(url);
            Connectivity.ReportSuccess();

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return ApiResponse<T>.Fail(response.StatusCode, ErrorMessage(body));

            // A route that answers "done" with no payload (204, or a 200 with an empty body) is a success,
            // not a parse failure: deserialising an empty string throws and used to come back as a 500.
            // -xlinka
            if (string.IsNullOrWhiteSpace(body))
                return ApiResponse<T>.Ok(default!);

            var data = JsonSerializer.Deserialize<T>(body, _json);
            return ApiResponse<T>.Ok(data!);
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.FromException(ex);
        }
    }

    private async Task<ApiResponse> PostAsync(string url, object? payload)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var content = CreateJsonContent(payload);
            using var response = await _api.PostAsync(url, content);
            Connectivity.ReportSuccess();
            var body = await response.Content.ReadAsStringAsync();
            return new ApiResponse
            {
                Success = response.IsSuccessStatusCode,
                Status = response.StatusCode,
                RawBody = body,
                Message = response.IsSuccessStatusCode ? null : ErrorMessage(body)
            };
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse.FromException(ex);
        }
    }

    private async Task<ApiResponse<T>> PostAsync<T>(string url, object? payload)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var content = CreateJsonContent(payload);
            using var response = await _api.PostAsync(url, content);
            Connectivity.ReportSuccess();

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return ApiResponse<T>.Fail(response.StatusCode, ErrorMessage(body));

            // A route that answers "done" with no payload (204, or a 200 with an empty body) is a success,
            // not a parse failure: deserialising an empty string throws and used to come back as a 500.
            // -xlinka
            if (string.IsNullOrWhiteSpace(body))
                return ApiResponse<T>.Ok(default!);

            var data = JsonSerializer.Deserialize<T>(body, _json);
            return ApiResponse<T>.Ok(data!);
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.FromException(ex);
        }
    }

    private async Task<ApiResponse> DeleteAsync(string url)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var response = await _api.DeleteAsync(url);
            Connectivity.ReportSuccess();
            var body = await response.Content.ReadAsStringAsync();
            return new ApiResponse
            {
                Success = response.IsSuccessStatusCode,
                Status = response.StatusCode,
                RawBody = body,
                Message = response.IsSuccessStatusCode ? null : ErrorMessage(body)
            };
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse.FromException(ex);
        }
    }

    private async Task<ApiResponse> PutAsync(string url, object? payload)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var content = CreateJsonContent(payload);
            using var response = await _api.PutAsync(url, content);
            Connectivity.ReportSuccess();
            var body = await response.Content.ReadAsStringAsync();
            return new ApiResponse
            {
                Success = response.IsSuccessStatusCode,
                Status = response.StatusCode,
                RawBody = body,
                Message = response.IsSuccessStatusCode ? null : ErrorMessage(body)
            };
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse.FromException(ex);
        }
    }

    private async Task<ApiResponse<T>> PatchAsync<T>(string url, object? payload)
    {
        if (!Connectivity.IsOnline)
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Offline");

        try
        {
            using var content = CreateJsonContent(payload);
            using var response = await _api.PatchAsync(url, content);
            Connectivity.ReportSuccess();

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return ApiResponse<T>.Fail(response.StatusCode, ErrorMessage(body));

            // A route that answers "done" with no payload (204, or a 200 with an empty body) is a success,
            // not a parse failure: deserialising an empty string throws and used to come back as a 500.
            // -xlinka
            if (string.IsNullOrWhiteSpace(body))
                return ApiResponse<T>.Ok(default!);

            var data = JsonSerializer.Deserialize<T>(body, _json);
            return ApiResponse<T>.Ok(data!);
        }
        catch (HttpRequestException)
        {
            Connectivity.ReportFailure();
            return ApiResponse<T>.Fail(HttpStatusCode.ServiceUnavailable, "Network error");
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.FromException(ex);
        }
    }

    // The service answers a refusal with { "message": "..." }. Handing that raw at a screen puts JSON in
    // front of a player, so it gets unwrapped here once instead of at every call site. Anything that is not
    // that shape comes back as it was. -xlinka
    private static string? ErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var trimmed = body!.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString();
                return string.IsNullOrWhiteSpace(text) ? body : text;
            }
        }
        catch (JsonException)
        {
            // not JSON after all
        }
        return body;
    }

    private HttpContent CreateJsonContent(object? payload)
    {
        if (payload == null)
            return new StringContent("", Encoding.UTF8, "application/json");

        var json = JsonSerializer.Serialize(payload, _json);

        if (!EnableCompression)
            return new StringContent(json, Encoding.UTF8, "application/json");

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var outputStream = new MemoryStream();
        using (var gzip = new GZipStream(outputStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        var compressedBytes = outputStream.ToArray();
        var content = new ByteArrayContent(compressedBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }

    // Every step goes to the caller's reporter when it gave one, and to the registry always, so the
    // Debug screen and the in-world readout see every transfer without anyone wiring them up. -xlinka
    private static void ReportDownload(IProgress<TransferProgress>? progress, TransferProgress state)
    {
        TransferRegistry.Report(state, upload: false);
        progress?.Report(state);
    }

    private static void ReportUpload(IProgress<TransferProgress>? progress, TransferProgress state)
    {
        TransferRegistry.Report(state, upload: true);
        progress?.Report(state);
    }

    #endregion
}
