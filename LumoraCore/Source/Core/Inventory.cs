// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Math;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core;

public enum InventoryEntryKind
{
    Folder,
    Item,
    World,
}

// One thing the inventory can show: a folder, a saved item, or a saved world, as the service lists
// it. Everything the screen needs to draw a card without another round trip.
public readonly struct InventoryEntry
{
    public readonly string Id;
    public readonly string Name;
    public readonly InventoryEntryKind Kind;
    public readonly DateTime ModifiedUtc;
    public readonly long SizeBytes;
    public readonly int ChildCount;
    // What a saved item was: "Avatar" or "Object". For a world, the mode it was saved in. Empty when
    // the service never stamped it.
    public readonly string ItemKind;
    public readonly string Hash;
    public readonly string? ThumbnailHash;
    // The folder this entry sits in, "root" at the top. Search rows also carry a readable Path.
    public readonly string FolderId;
    public readonly string? Path;

    public InventoryEntry(string id, string name, InventoryEntryKind kind, DateTime modifiedUtc, long sizeBytes,
        int childCount, string itemKind, string hash, string? thumbnailHash, string folderId, string? path)
    {
        Id = id;
        Name = name;
        Kind = kind;
        ModifiedUtc = modifiedUtc;
        SizeBytes = sizeBytes;
        ChildCount = childCount;
        ItemKind = itemKind ?? string.Empty;
        Hash = hash ?? string.Empty;
        ThumbnailHash = thumbnailHash;
        FolderId = string.IsNullOrEmpty(folderId) ? Inventory.Root : folderId;
        Path = path;
    }

    public bool IsFolder => Kind == InventoryEntryKind.Folder;
    public bool IsAvatar => Kind == InventoryEntryKind.Item && string.Equals(ItemKind, Inventory.AvatarKind, StringComparison.OrdinalIgnoreCase);
}

public readonly struct InventoryResult
{
    public readonly bool Ok;
    public readonly string Message;
    public InventoryResult(bool ok, string message) { Ok = ok; Message = message; }
    public static InventoryResult Success(string message) => new(true, message);
    public static InventoryResult Failure(string message) => new(false, message);
}

public sealed class InventoryListing
{
    public readonly List<InventoryEntry> Entries = new();
    public string? Problem;
}

// The inventory lives on the assets service and nowhere else. Every save is uploaded as one blob by
// hash and listed as an item in a folder there; fetching brings the blob back into the local asset
// cache so it can be loaded the way a file would. Nothing here writes a save to the user's disk,
// because storage is what supporters pay for. The one local thing left is the file browser, and that
// imports rather than saves. -xlinka
public static class Inventory
{
    public const string Root = LumoraClient.InventoryRoot;
    public const string AvatarKind = "Avatar";
    public const string ObjectKind = "Object";
    public const string WorldKind = "World";
    private const string ItemExtension = ".litem";
    private const string WorldExtension = ".lworld";
    private const string BlobMime = "application/octet-stream";
    private const int ThumbnailWidth = 256;
    private const int ThumbnailHeight = 144;

    // hash -> local file path of a blob already brought down this run.
    private static readonly ConcurrentDictionary<string, string> Cached = new(StringComparer.OrdinalIgnoreCase);

    public static LumoraClient? Client => Engine.Current?.CDNClient;
    public static bool IsSignedIn => Client?.IsAuthenticated == true;

    private static InventoryResult NotSignedIn() => InventoryResult.Failure("Sign in from the Home screen to use your inventory.");

    // LISTING

    public static async Task<InventoryListing> ListAsync(string folderId)
    {
        var listing = new InventoryListing();
        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            listing.Problem = NotSignedIn().Message;
            return listing;
        }
        var result = await client.GetFolderContents(folderId).ConfigureAwait(false);
        if (result.Failed || result.Data == null)
        {
            listing.Problem = result.Message ?? "The inventory service did not answer.";
            return listing;
        }
        Fill(listing, result.Data, folderId);
        return listing;
    }

    public static async Task<InventoryListing> SearchAsync(string query)
    {
        var listing = new InventoryListing();
        var client = Client;
        if (client == null || !client.IsAuthenticated)
        {
            listing.Problem = NotSignedIn().Message;
            return listing;
        }
        var result = await client.SearchInventory(query).ConfigureAwait(false);
        if (result.Failed || result.Data == null)
        {
            listing.Problem = result.Message ?? "The inventory service did not answer.";
            return listing;
        }
        Fill(listing, result.Data, Root);
        return listing;
    }

    private static void Fill(InventoryListing listing, FolderContents contents, string folderId)
    {
        foreach (var folder in contents.Folders)
        {
            listing.Entries.Add(new InventoryEntry(folder.Id, folder.Name, InventoryEntryKind.Folder, folder.CreatedAt,
                0, folder.ItemCount + folder.FolderCount, string.Empty, string.Empty, null, folderId, folder.Path));
        }
        foreach (var item in contents.Items)
        {
            bool world = string.Equals(item.Kind, WorldKind, StringComparison.OrdinalIgnoreCase) || item.Type == AssetType.World;
            string itemKind = world ? (item.Mode ?? string.Empty)
                : string.IsNullOrEmpty(item.Kind) ? (item.Type == AssetType.Avatar ? AvatarKind : ObjectKind) : item.Kind;
            listing.Entries.Add(new InventoryEntry(item.AssetId, item.Name, world ? InventoryEntryKind.World : InventoryEntryKind.Item,
                item.AddedAt, item.SizeBytes, 0, itemKind, item.Hash, item.ThumbnailHash, folderId, item.Path));
        }
    }

    // FOLDERS AND MOVES

    public static async Task<InventoryResult> CreateFolderAsync(string parentId, string name)
    {
        if (!ValidName(name, out var error))
            return InventoryResult.Failure(error!);
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = await client.CreateFolder(name.Trim(), parentId == Root ? null : parentId).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Folder \"{name.Trim()}\" is here.")
            : InventoryResult.Failure(result.Message ?? "Couldn't make that folder.");
    }

    public static async Task<InventoryResult> RenameAsync(InventoryEntry entry, string name)
    {
        if (!ValidName(name, out var error))
            return InventoryResult.Failure(error!);
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = entry.IsFolder
            ? await client.RenameFolder(entry.Id, name.Trim()).ConfigureAwait(false)
            : await client.RenameInventoryItem(entry.Id, name.Trim()).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Renamed to \"{name.Trim()}\".")
            : InventoryResult.Failure(result.Message ?? "Couldn't rename that.");
    }

    public static async Task<InventoryResult> MoveAsync(InventoryEntry entry, string targetFolderId, string targetName)
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        if (string.Equals(entry.FolderId, targetFolderId, StringComparison.Ordinal))
            return InventoryResult.Success("It is already there.");
        if (entry.IsFolder && string.Equals(entry.Id, targetFolderId, StringComparison.Ordinal))
            return InventoryResult.Failure("A folder cannot go inside itself.");
        var result = entry.IsFolder
            ? await client.MoveFolder(entry.Id, targetFolderId).ConfigureAwait(false)
            : await client.MoveItemToFolder(entry.Id, entry.FolderId, targetFolderId).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Moved \"{entry.Name}\" to {targetName}.")
            : InventoryResult.Failure(result.Message ?? "Couldn't move that.");
    }

    public static async Task<InventoryResult> DeleteAsync(InventoryEntry entry)
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = entry.IsFolder
            ? await client.DeleteFolder(entry.Id).ConfigureAwait(false)
            : await client.DeleteInventoryItem(entry.Id).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Deleted \"{entry.Name}\".")
            : InventoryResult.Failure(result.Message ?? "Couldn't delete that.");
    }

    // SAVING

    // Serialises on the calling (world) thread, then uploads off it. The permission question is asked
    // here as well as in the UI, so a route added later cannot skip it. -xlinka
    public static Task<InventoryResult> SaveItemAsync(Slot slot, string? name, string? folderId)
    {
        if (slot == null || slot.IsDestroyed || slot.World == null)
            return Task.FromResult(InventoryResult.Failure("There is nothing to save."));
        if (!Components.ItemProtection.AllowsSaveCopy(slot, null, out var refusal))
            return Task.FromResult(InventoryResult.Failure(refusal ?? "That cannot be saved."));
        if (!IsSignedIn)
            return Task.FromResult(NotSignedIn());

        string label = string.IsNullOrWhiteSpace(name) ? slot.Name : name!.Trim();
        if (string.IsNullOrWhiteSpace(label))
            label = "Item";
        string kind = ItemKindOf(slot);
        Persistence.DataTreeDictionary tree;
        try
        {
            tree = slot.SaveObject(Persistence.DependencyHandling.CollectAssets).Root;
        }
        catch (Exception ex)
        {
            return Task.FromResult(InventoryResult.Failure($"Couldn't pack \"{label}\": {ex.Message}"));
        }
        return UploadSaveAsync(tree, ItemExtension, label, kind, folderId, null, null);
    }

    // The world as it is now, with a fresh picture of it. Both go up as blobs; the item links them.
    public static Task<InventoryResult> SaveWorldAsync(World world, string? name, string? folderId)
    {
        if (world == null || world.IsDestroyed)
            return Task.FromResult(InventoryResult.Failure("There is no world to save."));
        if (world.IsSessionStartPending)
            return Task.FromResult(InventoryResult.Failure("That world hasn't finished loading."));
        if (!IsSignedIn)
            return Task.FromResult(NotSignedIn());

        string label = string.IsNullOrWhiteSpace(name) ? world.Name : name!.Trim();
        Persistence.DataTreeDictionary tree;
        try
        {
            tree = world.SaveWorld();
        }
        catch (Exception ex)
        {
            return Task.FromResult(InventoryResult.Failure($"Couldn't pack \"{label}\": {ex.Message}"));
        }
        byte[]? picture = null;
        try
        {
            var input = Engine.Current?.InputInterface;
            if (input != null && input.TryCaptureWorldView(ThumbnailWidth, ThumbnailHeight, out var jpeg) && jpeg != null && jpeg.Length > 0)
                picture = jpeg;
        }
        catch { }
        return UploadSaveAsync(tree, WorldExtension, label, WorldKind, folderId, picture, world.Mode.ToString());
    }

    // The tree was built on the world thread; from here on it is a detached snapshot. Every local
    // asset it names goes up by its own hash first (the store skips ones it already has), the URLs
    // are rewritten to cloud ones, and only then does the graph itself go up. -xlinka
    private static async Task<InventoryResult> UploadSaveAsync(Persistence.DataTreeDictionary tree, string extension, string name, string kind,
        string? folderId, byte[]? picture, string? mode)
    {
        var client = Client;
        if (client == null)
            return NotSignedIn();

        var problems = new List<string>();
        var manifest = await Persistence.CloudAssetPacker.PackAsync(tree, client, Engine.Current?.LocalDB, problems).ConfigureAwait(false);
        if (problems.Count > 0)
            Logging.Logger.Warn($"Inventory: \"{name}\" packed with {problems.Count} asset problem(s): {string.Join("; ", problems)}");

        byte[] bytes;
        try
        {
            bytes = Persistence.DataTreeConverter.SaveToBytes(tree);
        }
        catch (Exception ex)
        {
            return InventoryResult.Failure($"Couldn't pack \"{name}\": {ex.Message}");
        }

        var stored = await client.StoreContent(bytes, BlobMime, extension).ConfigureAwait(false);
        if (stored.Failed || stored.Data == null)
            return InventoryResult.Failure(stored.Message ?? "The upload did not go through.");

        string? thumbnail = null;
        if (picture != null)
        {
            var shot = await client.StoreContent(picture, "image/jpeg", ".jpg").ConfigureAwait(false);
            if (shot.Success && shot.Data != null)
                thumbnail = shot.Data.Hash;
        }

        var entries = new List<AssetManifestEntry>(manifest.Count);
        foreach (var m in manifest)
            entries.Add(new AssetManifestEntry { Hash = m.Hash, Name = m.Name, Extension = m.Extension, Type = m.Type, SizeBytes = m.SizeBytes });

        var added = await client.AddInventoryItem(stored.Data.Hash, name, kind, folderId, thumbnail, bytes.LongLength, mode, null, entries).ConfigureAwait(false);
        if (added.Failed)
            return InventoryResult.Failure(added.Message ?? "The upload went through but the item was refused.");

        // What was just sent is what a spawn would fetch, so keep it on hand.
        try
        {
            var db = Engine.Current?.LocalDB;
            if (db != null)
            {
                var path = db.GetTempFilePath(extension);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                Cached[stored.Data.Hash] = path;
            }
        }
        catch { }
        return InventoryResult.Success($"Saved \"{name}\".");
    }

    // FETCHING

    // The blob for an entry, as a file in the local cache. The same hash is only fetched once per run.
    public static async Task<(string? path, string? problem)> FetchToCacheAsync(InventoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Hash))
            return (null, "That item has no content.");
        if (Cached.TryGetValue(entry.Hash, out var have) && File.Exists(have))
            return (have, null);
        var client = Client;
        var db = Engine.Current?.LocalDB;
        if (client == null || !client.IsAuthenticated)
            return (null, NotSignedIn().Message);
        if (db == null)
            return (null, "The local cache isn't up yet.");
        var fetched = await client.FetchContent(entry.Hash).ConfigureAwait(false);
        if (fetched.Failed || fetched.Data == null)
            return (null, fetched.Message ?? "The download did not go through.");

        // Everything the graph names comes down before the graph is loaded, so the provider finds
        // every asset in the local database and nothing pops in late or stays blank.
        try
        {
            if (Persistence.DataTreeConverter.LoadFromBytes(Persistence.LocalEncryption.Decrypt(fetched.Data)) is Persistence.DataTreeDictionary tree)
            {
                var problems = new List<string>();
                await Persistence.CloudAssetPacker.PrefetchAsync(tree, client, db, problems).ConfigureAwait(false);
                if (problems.Count > 0)
                    Logging.Logger.Warn($"Inventory: \"{entry.Name}\" is missing {problems.Count} asset(s): {string.Join("; ", problems)}");
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Inventory: could not read the asset list of \"{entry.Name}\": {ex.Message}");
        }

        try
        {
            var path = db.GetTempFilePath(entry.Kind == InventoryEntryKind.World ? WorldExtension : ItemExtension);
            await File.WriteAllBytesAsync(path, fetched.Data).ConfigureAwait(false);
            Cached[entry.Hash] = path;
            return (path, null);
        }
        catch (Exception ex)
        {
            return (null, $"Couldn't keep the download: {ex.Message}");
        }
    }

    public static async Task<byte[]?> FetchThumbnailAsync(InventoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ThumbnailHash))
            return null;
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return null;
        var fetched = await client.FetchContent(entry.ThumbnailHash!).ConfigureAwait(false);
        return fetched.Success ? fetched.Data : null;
    }

    // Loads a fetched item into the world. Must run on the world's update thread; the fetch happens
    // before, on whatever thread the caller awaited on.
    public static Slot? LoadItem(World world, string path, string name)
    {
        if (world?.RootSlot == null || !File.Exists(path))
            return null;
        try
        {
            var slot = world.RootSlot.AddSlot(string.IsNullOrWhiteSpace(name) ? "Item" : name);
            slot.LoadObjectFromFile(path);
            return slot;
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"Inventory: failed to load '{name}': {ex.Message}");
            return null;
        }
    }

    // HELPERS

    public static string ItemKindOf(Slot slot)
        => slot.GetComponentInChildren<global::Lumora.Core.Components.Avatar.AvatarForm>() != null ? AvatarKind : ObjectKind;

    public static bool ValidName(string? name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Give it a name.";
            return false;
        }
        var trimmed = name.Trim();
        if (trimmed.Length > 64)
        {
            error = "That name is too long.";
            return false;
        }
        if (trimmed.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            error = "Slashes cannot be in a name.";
            return false;
        }
        return true;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }
}
