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

// The inventory is the account's, kept on the assets service, but a save is made on this machine first:
// it is stored in the local database and listed at once, and InventorySync takes it up in the background,
// one blob per file by hash and an item in a folder. So a save never waits on the network, only on a
// signed in account, whose saves are all the queue ever sends. Listings are the service's with this
// account's saves that are not up yet laid over them, and fetching serves one of those from this machine
// and anything else from the service, into the local asset cache to load the way a file would. -xlinka
public static class Inventory
{
    public const string Root = LumoraClient.InventoryRoot;
    public const string AvatarKind = "Avatar";
    public const string ObjectKind = "Object";
    public const string WorldKind = "World";
    private const string ItemExtension = ".litem";
    private const string WorldExtension = ".lworld";
    private const int ThumbnailWidth = 256;
    private const int ThumbnailHeight = 144;

    // hash -> local file path of a blob already brought down this run.
    private static readonly ConcurrentDictionary<string, string> Cached = new(StringComparer.OrdinalIgnoreCase);

    public static LumoraClient? Client => Engine.Current?.CDNClient;
    public static bool IsSignedIn => InventorySync.SignedIn;

    internal static InventoryResult NotSignedIn() => InventoryResult.Failure("Sign in with the profile widget on the workspace to use your inventory.");

    // LISTING

    // The folder as the service lists it, with this account's saves that are not up yet in it as well. They
    // show when the service cannot be reached too, beside the reason it could not.
    public static async Task<InventoryListing> ListAsync(string folderId)
    {
        var listing = new InventoryListing();
        var service = InventorySync.Service;
        if (service == null || !service.SignedIn)
        {
            listing.Problem = NotSignedIn().Message;
            return listing;
        }
        var result = await service.ListAsync(folderId).ConfigureAwait(false);
        if (result.Failed || result.Data == null)
            listing.Problem = result.Message ?? "The inventory service did not answer.";
        else
            Fill(listing, result.Data);
        InventorySync.MergeInto(listing, folderId);
        return listing;
    }

    public static async Task<InventoryListing> SearchAsync(string query)
    {
        var listing = new InventoryListing();
        var service = InventorySync.Service;
        if (service == null || !service.SignedIn)
        {
            listing.Problem = NotSignedIn().Message;
            return listing;
        }
        var result = await service.SearchAsync(query).ConfigureAwait(false);
        if (result.Failed || result.Data == null)
            listing.Problem = result.Message ?? "The inventory service did not answer.";
        else
            Fill(listing, result.Data);
        InventorySync.MergeSearch(listing, query);
        return listing;
    }

    // Each row under the folder the service says it is in, so a search hit knows where it lives; null is the
    // top. An item shows its whole size, not just its saved file's. -xlinka
    private static void Fill(InventoryListing listing, FolderContents contents)
    {
        foreach (var folder in contents.Folders)
        {
            listing.Entries.Add(new InventoryEntry(folder.Id, folder.Name, InventoryEntryKind.Folder, folder.CreatedAt,
                0, folder.ItemCount + folder.FolderCount, string.Empty, string.Empty, null, folder.ParentId ?? Root, folder.Path));
        }
        foreach (var item in contents.Items)
        {
            bool world = string.Equals(item.Kind, WorldKind, StringComparison.OrdinalIgnoreCase);
            string itemKind = world ? (item.WorldMode ?? string.Empty) : item.Kind;
            long size = item.TotalBytes > 0 ? item.TotalBytes : item.FileBytes;
            listing.Entries.Add(new InventoryEntry(item.Id, item.Name, world ? InventoryEntryKind.World : InventoryEntryKind.Item,
                item.UpdatedAt, size, 0, itemKind, item.FileHash, item.ThumbnailHash, item.FolderId ?? Root, item.Path));
        }
    }

    // FOLDERS AND MOVES

    // Under an id made here, so a request answered too late to be seen cannot make the folder twice.
    public static async Task<InventoryResult> CreateFolderAsync(string parentId, string name)
    {
        if (!ValidName(name, out var error))
            return InventoryResult.Failure(error!);
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = await client.UpsertFolder(Guid.NewGuid().ToString(), name.Trim(), parentId).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Folder \"{name.Trim()}\" is here.")
            : InventoryResult.Failure(result.Message ?? "Couldn't make that folder.");
    }

    // A save that has not gone up yet is changed here, everything else on the service.
    public static async Task<InventoryResult> RenameAsync(InventoryEntry entry, string name)
    {
        if (!ValidName(name, out var error))
            return InventoryResult.Failure(error!);
        if (!IsSignedIn)
            return NotSignedIn();
        if (!entry.IsFolder && await InventorySync.RenameAsync(entry.Id, name.Trim()).ConfigureAwait(false) is { } renamed)
            return renamed;
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
        if (!IsSignedIn)
            return NotSignedIn();
        if (string.Equals(entry.FolderId, targetFolderId, StringComparison.Ordinal))
            return InventoryResult.Success("It is already there.");
        if (entry.IsFolder && string.Equals(entry.Id, targetFolderId, StringComparison.Ordinal))
            return InventoryResult.Failure("A folder cannot go inside itself.");
        if (!entry.IsFolder && await InventorySync.MoveAsync(entry.Id, targetFolderId, targetName).ConfigureAwait(false) is { } moved)
            return moved;
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = entry.IsFolder
            ? await client.MoveFolder(entry.Id, targetFolderId).ConfigureAwait(false)
            : await client.MoveItemToFolder(entry.Id, targetFolderId).ConfigureAwait(false);
        return result.Success
            ? InventoryResult.Success($"Moved \"{entry.Name}\" to {targetName}.")
            : InventoryResult.Failure(result.Message ?? "Couldn't move that.");
    }

    public static async Task<InventoryResult> DeleteAsync(InventoryEntry entry)
    {
        if (!IsSignedIn)
            return NotSignedIn();
        if (!entry.IsFolder && await InventorySync.DeleteAsync(entry.Id).ConfigureAwait(false) is { } deleted)
            return deleted;
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return NotSignedIn();
        var result = entry.IsFolder
            ? await client.DeleteFolder(entry.Id).ConfigureAwait(false)
            : await client.DeleteInventoryItem(entry.Id).ConfigureAwait(false);
        if (result.Failed)
            return InventoryResult.Failure(result.Message ?? "Couldn't delete that.");
        if (entry.IsFolder)
            await InventorySync.DropFolderAsync(entry.Id).ConfigureAwait(false);
        InventorySync.SpaceFreed();
        return InventoryResult.Success($"Deleted \"{entry.Name}\".");
    }

    // SAVING

    // Serialises on the calling (world) thread, then keeps the save on this machine off it, and the sync
    // takes it up from there. The permission question is asked here as well as in the UI, so a route added
    // later cannot skip it. -xlinka
    public static Task<InventoryResult> SaveItemAsync(Slot slot, string? name, string? folderId)
        => SaveItemAsync(slot, name, folderId, null);

    // With the picture its card shows: a photo shows itself.
    public static Task<InventoryResult> SaveItemAsync(Slot slot, string? name, string? folderId, byte[]? picture)
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
        return InventorySync.SaveAsync(tree, ItemExtension, label, kind, folderId, picture, null);
    }

    // The folder of that name at the top, made the first time it is asked for; null when that cannot be done.
    public static async Task<string?> TopFolderAsync(string name)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var listing = await ListAsync(Root).ConfigureAwait(false);
            foreach (var entry in listing.Entries)
            {
                if (entry.IsFolder && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry.Id;
            }
            if (attempt == 0 && !(await CreateFolderAsync(Root, name).ConfigureAwait(false)).Ok)
                return null;
        }
        return null;
    }

    // The world as it is now, with a fresh picture of it. Both are kept here and go up as blobs; the item
    // links them.
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
        return InventorySync.SaveAsync(tree, WorldExtension, label, WorldKind, folderId, picture, world.Mode.ToString());
    }

    // FETCHING

    // The blob for an entry, as a file in the local cache. The same hash is only fetched once per run.
    public static async Task<(string? path, string? problem)> FetchToCacheAsync(InventoryEntry entry)
    {
        // A save that has not gone up yet is on this machine already. One that has is the service's, under the
        // hash it went up as, and its copy here is dropped once nothing else needs it.
        var hash = entry.Hash;
        if (InventorySync.Find(entry.Id) is { } row)
        {
            if (row.State != InventoryRowState.Synced || string.IsNullOrEmpty(row.CloudHash))
                return await FetchLocalAsync(row).ConfigureAwait(false);
            hash = row.CloudHash!;
        }
        if (string.IsNullOrEmpty(hash))
            return (null, "That item has no content.");
        if (Cached.TryGetValue(hash, out var have) && File.Exists(have))
            return (have, null);
        var client = Client;
        var db = Engine.Current?.LocalDB;
        if (client == null || !client.IsAuthenticated)
            return (null, NotSignedIn().Message);
        if (db == null)
            return (null, "The local cache isn't up yet.");
        var fetched = await client.FetchContent(hash).ConfigureAwait(false);
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
            Cached[hash] = path;
            return (path, null);
        }
        catch (Exception ex)
        {
            return (null, $"Couldn't keep the download: {ex.Message}");
        }
    }

    // The saved copy, read through the database so a copy kept encrypted comes out plain, into a file of
    // its own: whatever opens it may write it back, and the database's file must stay as it was.
    private static async Task<(string? path, string? problem)> FetchLocalAsync(InventoryRow row)
    {
        if (Cached.TryGetValue(row.LocalUri, out var have) && File.Exists(have))
            return (have, null);
        var db = Engine.Current?.LocalDB;
        if (db == null)
            return (null, "The local cache isn't up yet.");
        var bytes = await db.ReadAssetBytesAsync(row.LocalUri).ConfigureAwait(false);
        if (bytes == null)
            return (null, $"The saved copy of \"{row.Name}\" is no longer on this machine.");
        try
        {
            var path = db.GetTempFilePath(ExtensionFor(row.Kind));
            await File.WriteAllBytesAsync(path, db.EncryptAssetsAtRest ? Persistence.LocalEncryption.Encrypt(bytes) : bytes).ConfigureAwait(false);
            Cached[row.LocalUri] = path;
            return (path, null);
        }
        catch (Exception ex)
        {
            return (null, $"Couldn't open the saved copy: {ex.Message}");
        }
    }

    // The picture is read here while this machine keeps it; once the save is up the copy may be gone, and the
    // service has it under the same hash.
    public static async Task<byte[]?> FetchThumbnailAsync(InventoryEntry entry)
    {
        if (InventorySync.Find(entry.Id) is { ThumbnailUri: { } picture } && Engine.Current?.LocalDB is { } db
            && await db.ReadAssetBytesAsync(picture).ConfigureAwait(false) is { } kept)
            return kept;
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
            // What someone takes out of their inventory is theirs, on the host as on a guest.
            using var own = world.EnterSpawnAllocation();
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

    internal static string ExtensionFor(string kind) => string.Equals(kind, WorldKind, StringComparison.OrdinalIgnoreCase) ? WorldExtension : ItemExtension;

    // What just went up is what a spawn of it would fetch, so it is kept on hand.
    internal static void Keep(string hash, byte[] packed, bool world)
    {
        try
        {
            var db = Engine.Current?.LocalDB;
            if (db == null)
                return;
            var path = db.GetTempFilePath(world ? WorldExtension : ItemExtension);
            File.WriteAllBytes(path, packed);
            Cached[hash] = path;
        }
        catch { }
    }

    // The refusal for a save whose files could not all go up, naming the first few of them.
    public static string NotUploaded(string name, List<string> problems)
    {
        const int Named = 4;
        var named = string.Join("; ", problems.GetRange(0, System.Math.Min(Named, problems.Count)));
        var more = problems.Count > Named ? $"; and {problems.Count - Named} more" : string.Empty;
        return problems.Count == 1
            ? $"Couldn't save \"{name}\": {named}."
            : $"Couldn't save \"{name}\", {problems.Count} files it uses did not go up: {named}{more}.";
    }

    // What a save that went up without the files this machine no longer has says about them.
    public static string LeftOut(string name, List<string> dropped)
    {
        const int Named = 4;
        var named = string.Join("; ", dropped.GetRange(0, System.Math.Min(Named, dropped.Count)));
        var more = dropped.Count > Named ? $"; and {dropped.Count - Named} more" : string.Empty;
        return dropped.Count == 1
            ? $"Saved \"{name}\" without {named}, which is missing on this machine."
            : $"Saved \"{name}\" without {dropped.Count} files missing on this machine: {named}{more}.";
    }

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
