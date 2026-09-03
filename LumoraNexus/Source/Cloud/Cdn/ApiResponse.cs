// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;

namespace Lumora.Nexus.Cloud.Cdn;

public class ApiResponse
{
    public bool Success { get; init; }
    public HttpStatusCode Status { get; init; }
    public string? Message { get; init; }
    public string? RawBody { get; init; }

    public bool Failed => !Success;

    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Status = HttpStatusCode.OK,
        Message = message
    };

    public static ApiResponse Fail(HttpStatusCode status, string? message = null) => new()
    {
        Success = false,
        Status = status,
        Message = message
    };

    public static ApiResponse FromException(Exception ex) => new()
    {
        Success = false,
        Status = HttpStatusCode.InternalServerError,
        Message = ex.Message
    };

    public override string ToString() => Success
        ? $"OK: {Message ?? "Success"}"
        : $"Failed ({Status}): {Message ?? "Unknown error"}";
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Status = HttpStatusCode.OK,
        Data = data,
        Message = message
    };

    public static new ApiResponse<T> Fail(HttpStatusCode status, string? message = null) => new()
    {
        Success = false,
        Status = status,
        Message = message
    };

    public static new ApiResponse<T> FromException(Exception ex) => new()
    {
        Success = false,
        Status = HttpStatusCode.InternalServerError,
        Message = ex.Message
    };

    public ApiResponse<TOut> Map<TOut>(Func<T, TOut> mapper) => Success && Data != null
        ? ApiResponse<TOut>.Ok(mapper(Data), Message)
        : ApiResponse<TOut>.Fail(Status, Message);
}

public record ContentInfo
{
    public required string Hash { get; init; }
    public string? Owner { get; init; }
    public long Size { get; init; }
    public string? ContentType { get; init; }
    public DateTime Uploaded { get; init; }
    public bool Public { get; init; }
    public string? Variant { get; init; }
}

public record TransferProgress
{
    public required string Hash { get; init; }
    public long TotalBytes { get; init; }
    public long TransferredBytes { get; init; }
    public TransferState State { get; init; }

    public double Percentage => TotalBytes > 0 ? (double)TransferredBytes / TotalBytes * 100 : 0;
    public bool IsComplete => State == TransferState.Completed;
}

public enum TransferState
{
    Queued,
    Active,
    Completed,
    Cancelled,
    Error
}

// auth session - API returns token + a per-login session id; user info comes from /api/user/me
public record Session
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    // per-login session id (the JWT jti). We publish our session public key under this so a game host
    // can fetch the matching key and verify our account when we join. -xlinka
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = "";
}

// a user's published session public key (from GET /api/user/{id}/rsa/{sessionId})
public record SessionKeyInfo
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("sessionId")] public string SessionId { get; init; } = "";
    [JsonPropertyName("publicKey")] public string PublicKey { get; init; } = ""; // base64 DER SPKI
    [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; init; }
}

// a group as returned by the groups API. Visibility/MyRole come back as strings ("Public", "Owner"). -xlinka
public record GroupInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    // 2 to 6 characters, uppercase. The short form that rides a nametag. -xlinka
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("ownerId")] public string OwnerId { get; init; } = "";
    [JsonPropertyName("visibility")] public string Visibility { get; init; } = "Public";
    [JsonPropertyName("iconHash")] public string? IconHash { get; init; }
    // "#RRGGBB". Drives the card chip and the nametag card fill.
    [JsonPropertyName("color")] public string Color { get; init; } = "";
    [JsonPropertyName("storageQuotaBytes")] public long StorageQuotaBytes { get; init; }
    [JsonPropertyName("usedStorageBytes")] public long UsedStorageBytes { get; init; }
    [JsonPropertyName("memberCount")] public int MemberCount { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("myRole")] public string? MyRole { get; init; } // caller's role, null if not a member
    // Storage state from the owner's billing: "Active" | "Grace" | "Locked", or null on list rows. -xlinka
    [JsonPropertyName("storageStatus")] public string? StorageStatus { get; init; }
    [JsonPropertyName("storageLockAt")] public DateTime? StorageLockAt { get; init; }
    // The caller's own slice of the pool, 0 when not a member.
    [JsonPropertyName("myAllocatedBytes")] public long MyAllocatedBytes { get; init; }
    [JsonPropertyName("myUsedBytes")] public long MyUsedBytes { get; init; }
}

public record GroupMemberInfo
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    // Carried on the member list so the page never resolves names one profile at a time. -xlinka
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("role")] public string Role { get; init; } = "Member";
    [JsonPropertyName("joinedAt")] public DateTime JoinedAt { get; init; }
    [JsonPropertyName("allocatedBytes")] public long AllocatedBytes { get; init; }
    [JsonPropertyName("usedBytes")] public long UsedBytes { get; init; }
}

// a pending join request on a private group (from GET /api/groups/{id}/requests). -xlinka
public record GroupJoinRequestInfo
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("requestedAt")] public DateTime RequestedAt { get; init; }
}

// One invite out of a group, as its moderators see it (GET /api/groups/{id}/invites). The group is
// already on screen there, so the row is about the person.
public record GroupInviteInfo
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("invitedBy")] public string InvitedBy { get; init; } = "";
    [JsonPropertyName("invitedAt")] public DateTime InvitedAt { get; init; }
}

// An invite waiting for the caller (GET /api/groups/invites/mine). The other way round: the person is
// known and the row is about the group, so it carries just enough of one to draw a card. -xlinka
public record GroupInviteSummary
{
    [JsonPropertyName("groupId")] public string GroupId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    [JsonPropertyName("iconHash")] public string? IconHash { get; init; }
    [JsonPropertyName("color")] public string Color { get; init; } = "";
    [JsonPropertyName("invitedBy")] public string InvitedBy { get; init; } = "";
    [JsonPropertyName("invitedAt")] public DateTime InvitedAt { get; init; }
}

// What POST /api/groups/{id}/join answers with: a public group lets you in, a private one files a
// request, and an invite to either lets you straight in. The page has to say which happened. -xlinka
public record GroupJoinResult
{
    [JsonPropertyName("joined")] public bool Joined { get; init; }
    [JsonPropertyName("requested")] public bool Requested { get; init; }
}

public record GroupEventInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("groupId")] public string GroupId { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("worldName")] public string WorldName { get; init; } = "";
    [JsonPropertyName("startsAt")] public DateTime StartsAt { get; init; }
    [JsonPropertyName("endsAt")] public DateTime? EndsAt { get; init; }
    [JsonPropertyName("hostUserId")] public string HostUserId { get; init; } = "";
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
}

public record GroupAnnouncementInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("groupId")] public string GroupId { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
    [JsonPropertyName("authorId")] public string AuthorId { get; init; } = "";
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
}

public record GroupBanInfo
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("bannedBy")] public string BannedBy { get; init; } = "";
    [JsonPropertyName("bannedAt")] public DateTime BannedAt { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
}

// The body of PATCH /api/groups/{id}. Every field is nullable and a null means "leave this one alone",
// so the admin page sends the three things somebody edited instead of re-posting the whole group and
// racing another admin's edit of a field it never touched. -xlinka
public record GroupPatch
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("tag")] public string? Tag { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("iconHash")] public string? IconHash { get; init; }
    [JsonPropertyName("color")] public string? Color { get; init; }
    [JsonPropertyName("visibility")] public string? Visibility { get; init; }

    public bool IsEmpty => Name == null && Tag == null && Description == null
        && IconHash == null && Color == null && Visibility == null;
}

// The group a user has chosen to wear, as it rides their public profile. This is what the nametag card
// draws from, so it carries everything the card needs and nothing else. -xlinka
public record RepresentedGroupInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("color")] public string Color { get; init; } = "";
    [JsonPropertyName("iconHash")] public string? IconHash { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = "";
}

// public view of a user incl. PLATFORM moderation ban status (from GET /api/user/{id}). NOT world bans. -xlinka
public record PublicUserInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("isVerified")] public bool IsVerified { get; init; }
    [JsonPropertyName("bio")] public string Bio { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; init; } = new(); // all tags = profile badges. -xlinka
    [JsonPropertyName("nameplateBadges")] public List<string> NameplateBadges { get; init; } = new(); // subset shown on the nameplate.
    [JsonPropertyName("isAccountBanned")] public bool IsAccountBanned { get; init; }
    [JsonPropertyName("isPublicBanned")] public bool IsPublicBanned { get; init; }
    [JsonPropertyName("isSpectatorBanned")] public bool IsSpectatorBanned { get; init; }
    [JsonPropertyName("isMuteBanned")] public bool IsMuteBanned { get; init; }
    // Null when the user is not wearing a group.
    [JsonPropertyName("representedGroup")] public RepresentedGroupInfo? RepresentedGroup { get; init; }
}

public record UploadHandle
{
    public required string Id { get; init; }
    public required string Hash { get; init; }
    public DateTime Expires { get; init; }
    public int ChunkSize { get; init; }
    public int TotalChunks { get; init; }
}

// user profile from /api/user/me
public record UserProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("email")]
    public string? Email { get; init; }
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";
    [JsonPropertyName("normalizedUsername")]
    public string? NormalizedUsername { get; init; }
    [JsonPropertyName("registrationDate")]
    public DateTime RegistrationDate { get; init; }
    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; init; }
    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; init; }
    [JsonPropertyName("nameColor")]
    public string NameColor { get; init; } = "#FFFFFF";
    [JsonPropertyName("twoFactorEnabled")]
    public bool TwoFactorEnabled { get; init; }
    [JsonPropertyName("patreonData")]
    public PatreonInfo? PatreonData { get; init; }
    [JsonPropertyName("storageQuota")]
    public StorageQuota? StorageQuota { get; init; }
    // Roles + badges (admin/moderator/staff, supporter, patreon, tier:<name>). -xlinka
    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();
    [JsonPropertyName("bio")]
    public string Bio { get; init; } = "";
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
}

public record PatreonInfo
{
    [JsonPropertyName("isActiveSupporter")]
    public bool IsActiveSupporter { get; init; }
    [JsonPropertyName("tierName")]
    public string TierName { get; init; } = "";
    [JsonPropertyName("tierColor")]
    public string? TierColor { get; init; }
    [JsonPropertyName("totalSupportMonths")]
    public int TotalSupportMonths { get; init; }
}

public record StorageQuota
{
    [JsonPropertyName("quotaMB")]
    public int QuotaMB { get; init; }
    [JsonPropertyName("usedMB")]
    public int UsedMB { get; init; }
    [JsonPropertyName("availableMB")]
    public int AvailableMB { get; init; }
    [JsonPropertyName("percentUsed")]
    public double PercentUsed { get; init; }
}

public record TwoFactorSetup
{
    public required string Secret { get; init; }
    public required string QrCode { get; init; }
    public required List<string> RecoveryCodes { get; init; }
}

// inventory models matching backend Inventory.cs

public enum AssetType
{
    Avatar,
    World,
    Prop
}

public record AssetRef
{
    [JsonPropertyName("assetId")] public string AssetId { get; init; } = "";
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("type")] public AssetType Type { get; init; }
    [JsonPropertyName("addedAt")] public DateTime AddedAt { get; init; }
    [JsonPropertyName("tags")] public List<string> Tags { get; init; } = new();
    [JsonPropertyName("thumbnailHash")] public string? ThumbnailHash { get; init; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    // What the save was: "Avatar" | "Object" | "World", and a world's mode. Empty on rows written
    // before the service learned to stamp them. -xlinka
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    // Search rows carry where they live; a folder listing leaves this null.
    [JsonPropertyName("path")] public string? Path { get; init; }
}

public record VariantState
{
    [JsonPropertyName("variantId")] public string? VariantId { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "Pending";
    [JsonPropertyName("resultHash")] public string? ResultHash { get; init; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    public bool IsReady => State == "Ready" && !string.IsNullOrEmpty(ResultHash);
    public bool IsSkipped => State == "Skipped";
}

public record VariantJob
{
    [JsonPropertyName("assetHash")] public string AssetHash { get; init; } = "";
    [JsonPropertyName("variantId")] public string VariantId { get; init; } = "";
    [JsonPropertyName("attempt")] public int Attempt { get; init; }
}

// One asset a save depends on, told to the service so a shared texture is stored and counted once.
public record AssetManifestEntry
{
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("extension")] public string Extension { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "other";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
}

public record FolderSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("itemCount")] public int ItemCount { get; init; }
    [JsonPropertyName("folderCount")] public int FolderCount { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
}

// One folder's direct children, or the rows a search found.
public record FolderContents
{
    [JsonPropertyName("folders")] public List<FolderSummary> Folders { get; init; } = new();
    [JsonPropertyName("items")] public List<AssetRef> Items { get; init; } = new();
}

public record UserFolder
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("parentId")] public string? ParentId { get; init; }
    [JsonPropertyName("assets")] public List<AssetRef> Assets { get; init; } = new();
    [JsonPropertyName("subfolders")] public List<UserFolder> Subfolders { get; init; } = new();
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
}

public record InventoryResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("folders")] public List<UserFolder> Folders { get; init; } = new();
    [JsonPropertyName("lastUpdated")] public DateTime LastUpdated { get; init; }
}

public record UserQuotaResponse
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = "";
    [JsonPropertyName("quotaMB")] public int QuotaMB { get; init; }
    [JsonPropertyName("usedMB")] public long UsedMB { get; init; }
    [JsonPropertyName("availableMB")] public long AvailableMB { get; init; }
    [JsonPropertyName("percentUsed")] public double PercentUsed { get; init; }
}

public record AssetComponentInfo
{
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("extension")] public string Extension { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
}

public record AssetSharingInfo
{
    [JsonPropertyName("level")] public string Level { get; init; } = "Private";
    [JsonPropertyName("allowedUserIds")] public List<string> AllowedUserIds { get; init; } = new();
    [JsonPropertyName("allowCopy")] public bool AllowCopy { get; init; }
    [JsonPropertyName("allowModify")] public bool AllowModify { get; init; }
    [JsonPropertyName("sharedAt")] public DateTime? SharedAt { get; init; }
    [JsonPropertyName("sharedBy")] public string? SharedBy { get; init; }
}

public record AssetInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("extension")] public string Extension { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public AssetType Type { get; init; }
    [JsonPropertyName("ownerId")] public string OwnerId { get; init; } = "";
    [JsonPropertyName("originalSizeBytes")] public long OriginalSizeBytes { get; init; }
    [JsonPropertyName("processedComponents")] public List<AssetComponentInfo> ProcessedComponents { get; init; } = new();
    [JsonPropertyName("uploadedAt")] public DateTime UploadedAt { get; init; }
    [JsonPropertyName("lastModifiedAt")] public DateTime LastModifiedAt { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, object> Metadata { get; init; } = new();
    [JsonPropertyName("thumbnailHash")] public string? ThumbnailHash { get; init; }
    [JsonPropertyName("sharing")] public AssetSharingInfo? Sharing { get; init; }
}
