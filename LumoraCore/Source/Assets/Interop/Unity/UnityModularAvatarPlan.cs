// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumora.Core.Assets.Interop.Unity;

// The part of Modular Avatar the menu plan takes, read before anything is built. A merge animator adds its
// controller to the avatar's own (clip paths from the object it names, or from the avatar's root), a menu
// installer with a menu asset files that menu under the folder its target menu sits at, and a parameters
// component renames what the animators, menu items and receivers below it call their parameters.
//
// The menu built from objects is walked here too, so every menu item knows its path before the plan reads
// it: an item that sets a parameter an animator reads is handed to the plan as a menu entry, and its switch is
// the plan's. -xlinka
public static class UnityModularAvatarPlan
{
    public const string AvatarRoot = "$$$AVATAR_ROOT$$$";

    public sealed class Item
    {
        public UnityComponentRef Part = null!;
        public string Path = "";
        public int Type;
        public string Parameter = "";
        public float Value = 1f;
        public bool IsDefault;
    }

    public static List<UnityVrcPlan.Merge> Merges(UnityPackageArchive archive, UnityImportCandidate candidate)
    {
        var merges = new List<UnityVrcPlan.Merge>();
        var tools = UnityBuildTools.Of(candidate);
        tools.Items.Clear();
        if (tools.ModularAvatar.Count == 0)
            return merges;
        var byNode = tools.ModularAvatar.GroupBy(p => p.Part.Node).ToDictionary(g => g.Key, g => g.ToList());
        UnityComponentRef? On(UnityNodeRef node, UnityBuildTools.Ma kind)
            => byNode.TryGetValue(node, out var list) ? list.FirstOrDefault(p => p.Kind == kind).Part : null;
        var folders = MenuFolders(archive, candidate);

        int index = 0;
        foreach (var (kind, part) in tools.ModularAvatar)
        {
            if (kind == UnityBuildTools.Ma.MergeAnimator && part.Doc["animator"]?.Ref.Guid is { Length: > 0 } controller)
            {
                var merge = new UnityVrcPlan.Merge { Name = part.Node.Name };
                if ((part.Doc["pathMode"]?.Int() ?? 0) == 0)
                    merge.PathPrefix = ObjectPath(candidate, part.Doc["relativePathRoot"]) is { Length: > 0 } root ? root : UnityFullController.PathOf(part.Node) ?? "";
                merge.Controllers.Add((part.Doc["layerType"]?.Int(5) ?? 5, controller));
                Renames(candidate, part.Node, merge.Parameters, index++);
                merges.Add(merge);
            }
            else if (kind == UnityBuildTools.Ma.MenuInstaller && part.Doc["menuToAppend"]?.Ref.Guid is { Length: > 0 } menu)
            {
                var merge = new UnityVrcPlan.Merge { Name = part.Node.Name };
                merge.Menus.Add((menu, Folder(part, folders)));
                Renames(candidate, part.Node, merge.Parameters, index++);
                merges.Add(merge);
            }
        }

        // The object menus: what each installer puts where.
        var children = new Dictionary<UnityNodeRef, List<UnityNodeRef>>();
        foreach (var node in byNode.Keys.Concat(candidate.AddedObjects).Distinct())
            if (node.Parent != null)
            {
                if (!children.TryGetValue(node.Parent, out var list))
                    children[node.Parent] = list = new List<UnityNodeRef>();
                list.Add(node);
            }
        IEnumerable<UnityNodeRef> ChildrenOf(UnityNodeRef node) => children.TryGetValue(node, out var list) ? list : Enumerable.Empty<UnityNodeRef>();
        var controls = new UnityVrcPlan.Merge { Name = "Menu items" };
        void Walk(UnityNodeRef node, string folder, int depth)
        {
            if (depth > 8 || On(node, UnityBuildTools.Ma.MenuItem) is not { } part)
                return;
            var control = part.Doc["Control"];
            var label = UnityExpressionMenu.CleanName(Label(part, node));
            var path = folder.Length > 0 ? folder + "/" + label : label;
            int type = control?["type"]?.Int(102) ?? 102;
            if (type == (int)UnityExpressionMenu.ControlType.SubMenu)
            {
                if ((part.Doc["MenuSource"]?.Int() ?? 0) == 1)
                {
                    var source = part.Doc["menuSource_otherObjectChildren"]?.Ref.FileId is long other and not 0 && candidate.ResolveObject(other) is { } o ? o : node;
                    foreach (var child in ChildrenOf(source))
                        Walk(child, path, depth + 1);
                }
                else if (control?["subMenu"]?.Ref.Guid is { Length: > 0 } asset)
                    controls.Menus.Add((asset, path));
                return;
            }
            var item = new Item
            {
                Part = part, Path = path, Type = type, Parameter = Parameter(candidate, part, control),
                Value = control?["value"]?.Float(1f) ?? 1f, IsDefault = (part.Doc["isDefault"]?.Int() ?? 0) != 0,
            };
            if (type == (int)UnityExpressionMenu.ControlType.Radial && control?["subParameters"] is UnitySeq { Items.Count: > 0 } sub)
                item.Parameter = Renamed(candidate, part.Node, sub.Items[0]["name"]?.Text ?? "");
            tools.Items.Add(item);
            if (item.Parameter.Length == 0)
                return;
            if (type == (int)UnityExpressionMenu.ControlType.Radial)
                controls.Controls.Add((path, item.Parameter, true, 0f));
            else if (type is (int)UnityExpressionMenu.ControlType.Toggle or (int)UnityExpressionMenu.ControlType.Button)
                controls.Controls.Add((path, item.Parameter, false, item.IsDefault ? 1f : 0f));
        }
        var installed = new HashSet<UnityNodeRef>();
        foreach (var (kind, installer) in tools.ModularAvatar)
        {
            if (kind != UnityBuildTools.Ma.MenuInstaller || installer.Doc["menuToAppend"]?.Ref.Guid is { Length: > 0 } || !installed.Add(installer.Node))
                continue;
            var folder = Folder(installer, folders);
            if (On(installer.Node, UnityBuildTools.Ma.MenuItem) != null)
                Walk(installer.Node, folder, 0);
            else
            {
                var group = On(installer.Node, UnityBuildTools.Ma.MenuGroup);
                var from = group?.Doc["targetObject"]?.Ref.FileId is long target and not 0 && candidate.ResolveObject(target) is { } t ? t : installer.Node;
                foreach (var child in ChildrenOf(from))
                    Walk(child, folder, 0);
            }
        }
        if (controls.Controls.Count > 0 || controls.Menus.Count > 0)
            merges.Add(controls);
        return merges;
    }

    private static string Label(UnityComponentRef item, UnityNodeRef node)
    {
        var label = item.Doc["label"]?.Text;
        if (!string.IsNullOrWhiteSpace(label))
            return label;
        var name = item.Doc["Control"]?["name"]?.Text;
        return string.IsNullOrWhiteSpace(name) ? node.Name : name;
    }

    private static string Parameter(UnityImportCandidate candidate, UnityComponentRef part, UnityNode? control)
        => Renamed(candidate, part.Node, control?["parameter"]?["name"]?.Text ?? "");

    // A name as the parameters components above it leave it: renamed by the nearest that lists it.
    private static string Renamed(UnityImportCandidate candidate, UnityNodeRef node, string name)
    {
        if (name.Length == 0)
            return name;
        var tools = UnityBuildTools.Of(candidate);
        for (var at = node; at != null; at = at.Parent)
            foreach (var (kind, part) in tools.ModularAvatar)
            {
                if (kind != UnityBuildTools.Ma.Parameters || !ReferenceEquals(part.Node, at) || part.Doc["parameters"] is not UnitySeq list)
                    continue;
                foreach (var entry in list.Items)
                {
                    var from = entry["nameOrPrefix"]?.Text ?? "";
                    bool prefix = (entry["isPrefix"]?.Int() ?? 0) != 0;
                    if (from.Length == 0 || (prefix ? !name.StartsWith(from, StringComparison.Ordinal) : name != from))
                        continue;
                    var to = entry["remapTo"]?.Text ?? "";
                    if (to.Length > 0)
                        return prefix ? to + name[from.Length..] : to;
                    return name;
                }
            }
        return name;
    }

    // The parameters components on the object and above it: their renames, and private names made unique.
    private static void Renames(UnityImportCandidate candidate, UnityNodeRef node, Dictionary<string, string> into, int index)
    {
        var tools = UnityBuildTools.Of(candidate);
        for (var at = node; at != null; at = at.Parent)
            foreach (var (kind, part) in tools.ModularAvatar)
            {
                if (kind != UnityBuildTools.Ma.Parameters || !ReferenceEquals(part.Node, at) || part.Doc["parameters"] is not UnitySeq list)
                    continue;
                foreach (var entry in list.Items)
                {
                    var from = entry["nameOrPrefix"]?.Text ?? "";
                    if (from.Length == 0 || (entry["isPrefix"]?.Int() ?? 0) != 0 || into.ContainsKey(from))
                        continue;
                    var to = entry["remapTo"]?.Text ?? "";
                    if (to.Length > 0)
                        into[from] = to;
                    else if ((entry["internalParameter"]?.Int() ?? 0) != 0)
                        into[from] = $"MA{index}_{from}";
                }
            }
    }

    private static string Folder(UnityComponentRef installer, Dictionary<string, string> folders)
        => installer.Doc["installTargetMenu"]?.Ref.Guid is { Length: > 0 } target && folders.TryGetValue(target, out var folder) ? folder : "";

    // Where each submenu asset of the avatar's own menu sits, as menu paths name it.
    private static Dictionary<string, string> MenuFolders(UnityPackageArchive archive, UnityImportCandidate candidate)
    {
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = candidate.Descriptor?["expressionsMenu"]?.Ref.Guid;
        if (string.IsNullOrEmpty(root))
            return folders;
        var queue = new Queue<(string Guid, string Folder, int Depth)>();
        queue.Enqueue((root!, "", 0));
        while (queue.Count > 0)
        {
            var (guid, folder, depth) = queue.Dequeue();
            if (!folders.TryAdd(guid, folder) || depth > 8)
                continue;
            var menu = UnityExpressionMenu.Parse(archive.ReadText(archive.Find(guid)));
            if (menu == null)
                continue;
            foreach (var control in menu.Controls)
                if (control.Type == UnityExpressionMenu.ControlType.SubMenu && !string.IsNullOrEmpty(control.SubMenu))
                    queue.Enqueue((control.SubMenu!, folder.Length > 0 ? folder + "/" + control.Name : control.Name, depth + 1));
        }
        return folders;
    }

    private static string? ObjectPath(UnityImportCandidate candidate, UnityNode? reference)
    {
        if (reference == null)
            return null;
        if (reference["targetObject"]?.Ref.FileId is long id and not 0 && candidate.ResolveObject(id) is { } node)
            return UnityFullController.PathOf(node);
        var path = reference["referencePath"]?.Text;
        return path == AvatarRoot ? "" : path;
    }
}
