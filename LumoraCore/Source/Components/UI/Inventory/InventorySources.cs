// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lumora.Core.Components.UI;

// Where the Inventory screen gets its rows and sends its changes. The cloud is the only real source;
// the demo one exists so the capture harness can draw the screen on a machine with no account. -xlinka
internal interface IInventorySource
{
    bool SignedIn { get; }
    Task<InventoryListing> List(string folderId);
    Task<InventoryListing> Search(string query);
    Task<InventoryResult> CreateFolder(string parentId, string name);
    Task<InventoryResult> Rename(InventoryEntry entry, string name);
    Task<InventoryResult> Move(InventoryEntry entry, string targetFolderId, string targetName);
    Task<InventoryResult> Delete(InventoryEntry entry);
    Task<InventoryResult> SaveItem(Slot slot, string name, string folderId);
    Task<InventoryResult> SaveWorld(World world, string name, string folderId);
    Task<(string? path, string? problem)> FetchToCache(InventoryEntry entry);
    Task<byte[]?> FetchThumbnail(InventoryEntry entry);
}

internal sealed class CloudInventorySource : IInventorySource
{
    public bool SignedIn => Inventory.IsSignedIn;
    public Task<InventoryListing> List(string folderId) => Inventory.ListAsync(folderId);
    public Task<InventoryListing> Search(string query) => Inventory.SearchAsync(query);
    public Task<InventoryResult> CreateFolder(string parentId, string name) => Inventory.CreateFolderAsync(parentId, name);
    public Task<InventoryResult> Rename(InventoryEntry entry, string name) => Inventory.RenameAsync(entry, name);
    public Task<InventoryResult> Move(InventoryEntry entry, string targetFolderId, string targetName) => Inventory.MoveAsync(entry, targetFolderId, targetName);
    public Task<InventoryResult> Delete(InventoryEntry entry) => Inventory.DeleteAsync(entry);
    public Task<InventoryResult> SaveItem(Slot slot, string name, string folderId) => Inventory.SaveItemAsync(slot, name, folderId);
    public Task<InventoryResult> SaveWorld(World world, string name, string folderId) => Inventory.SaveWorldAsync(world, name, folderId);
    public Task<(string? path, string? problem)> FetchToCache(InventoryEntry entry) => Inventory.FetchToCacheAsync(entry);
    public Task<byte[]?> FetchThumbnail(InventoryEntry entry) => Inventory.FetchThumbnailAsync(entry);
}

// An in-memory tree with a few folders and saves in it. Only the capture harness switches to it, so
// the screen can be looked at without a signed-in account and without a service to talk to. Nothing
// it holds can be spawned or opened. -xlinka
internal sealed class DemoInventorySource : IInventorySource
{
    private sealed class Node
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name = "";
        public InventoryEntryKind Kind;
        public string ItemKind = "";
        public string Parent = Inventory.Root;
        public DateTime Modified = DateTime.UtcNow;
        public long Size;
    }

    private readonly List<Node> _nodes = new();

    public DemoInventorySource()
    {
        var props = Add("Props", InventoryEntryKind.Folder, "", Inventory.Root);
        var avatars = Add("Avatars", InventoryEntryKind.Folder, "", Inventory.Root);
        Add("Lamps", InventoryEntryKind.Folder, "", props.Id);
        Add("Events", InventoryEntryKind.Folder, "", Inventory.Root);
        Add("Chair", InventoryEntryKind.Item, Inventory.ObjectKind, Inventory.Root, 48_120);
        Add("Lamp", InventoryEntryKind.Item, Inventory.ObjectKind, Inventory.Root, 22_400);
        Add("Fox", InventoryEntryKind.Item, Inventory.AvatarKind, avatars.Id, 3_140_000);
        Add("Rooftop", InventoryEntryKind.World, "Social", Inventory.Root, 12_600_000);
        Add("Workshop", InventoryEntryKind.World, "Builder", Inventory.Root, 8_200_000);
        Add("Desk lamp", InventoryEntryKind.Item, Inventory.ObjectKind, props.Id, 18_000);
        for (int i = 1; i <= 30; i++)
            Add($"Prop {i:00}", InventoryEntryKind.Item, Inventory.ObjectKind, props.Id, 10_000 + i * 700);
    }

    private Node Add(string name, InventoryEntryKind kind, string itemKind, string parent, long size = 0)
    {
        var node = new Node { Name = name, Kind = kind, ItemKind = itemKind, Parent = parent, Size = size };
        _nodes.Add(node);
        return node;
    }

    public bool SignedIn => true;

    private InventoryEntry ToEntry(Node n)
    {
        int children = 0;
        if (n.Kind == InventoryEntryKind.Folder)
        {
            foreach (var m in _nodes)
                if (m.Parent == n.Id) children++;
        }
        return new InventoryEntry(n.Id, n.Name, n.Kind, n.Modified, n.Size, children, n.ItemKind, "demo-" + n.Id, null, n.Parent, null);
    }

    public Task<InventoryListing> List(string folderId)
    {
        var listing = new InventoryListing();
        foreach (var n in _nodes)
            if (n.Parent == folderId) listing.Entries.Add(ToEntry(n));
        return Task.FromResult(listing);
    }

    public Task<InventoryListing> Search(string query)
    {
        var listing = new InventoryListing();
        foreach (var n in _nodes)
            if (n.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) listing.Entries.Add(ToEntry(n));
        return Task.FromResult(listing);
    }

    public Task<InventoryResult> CreateFolder(string parentId, string name)
    {
        Add(name.Trim(), InventoryEntryKind.Folder, "", parentId);
        return Task.FromResult(InventoryResult.Success($"Folder \"{name.Trim()}\" is here."));
    }

    public Task<InventoryResult> Rename(InventoryEntry entry, string name)
    {
        var n = _nodes.Find(x => x.Id == entry.Id);
        if (n != null) n.Name = name.Trim();
        return Task.FromResult(InventoryResult.Success($"Renamed to \"{name.Trim()}\"."));
    }

    public Task<InventoryResult> Move(InventoryEntry entry, string targetFolderId, string targetName)
    {
        var n = _nodes.Find(x => x.Id == entry.Id);
        if (n != null) n.Parent = targetFolderId;
        return Task.FromResult(InventoryResult.Success($"Moved \"{entry.Name}\" to {targetName}."));
    }

    public Task<InventoryResult> Delete(InventoryEntry entry)
    {
        _nodes.RemoveAll(x => x.Id == entry.Id || x.Parent == entry.Id);
        return Task.FromResult(InventoryResult.Success($"Deleted \"{entry.Name}\"."));
    }

    public Task<InventoryResult> SaveItem(Slot slot, string name, string folderId)
        => Task.FromResult(InventoryResult.Failure("The demo inventory cannot save."));

    public Task<InventoryResult> SaveWorld(World world, string name, string folderId)
        => Task.FromResult(InventoryResult.Failure("The demo inventory cannot save."));

    public Task<(string? path, string? problem)> FetchToCache(InventoryEntry entry)
        => Task.FromResult<(string?, string?)>((null, "The demo inventory has no content."));

    public Task<byte[]?> FetchThumbnail(InventoryEntry entry) => Task.FromResult<byte[]?>(null);
}
