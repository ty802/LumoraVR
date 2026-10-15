// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// One game object of a scene or prefab once every prefab placed in it has been opened up. A node inside a
// placed model file stands in with the placement it belongs to and the model's own id for it; everything
// else carries its own pose, in the project's (left-handed) terms.
public sealed class UnitySceneObject
{
    // Its game object's id in the graph; a placed model's root takes its root transform's.
    public long Id;
    public string Name = "";
    public bool Active = true;
    public UnitySceneObject? Parent;
    public readonly List<UnitySceneObject> Children = new();
    public float3 Position;
    public floatQ Rotation = floatQ.Identity;
    public float3 Scale = float3.One;
    public UnityModelPart? Part;
    public long ModelId;
    public long ModelIdAlt;
    public bool IsPartRoot;
    // Tagged to stay in the editor; the build strips it and everything under it.
    public bool EditorOnly;
    // Its scripts and renderers with every override applied; references inside point at graph ids.
    public readonly List<UnityDocument> Components = new();
    public UnityNodeRef? Node;

    public bool IsModelNode => Part != null && !IsPartRoot;

    public bool ActiveInHierarchy
    {
        get
        {
            for (var o = this; o != null; o = o.Parent)
                if (!o.Active)
                    return false;
            return true;
        }
    }

    public bool IsUnder(UnitySceneObject root)
    {
        for (var o = this; o != null; o = o.Parent)
            if (ReferenceEquals(o, root))
                return true;
        return false;
    }

    public bool InEditorOnly
    {
        get
        {
            for (var o = this; o != null; o = o.Parent)
                if (o.EditorOnly)
                    return true;
            return false;
        }
    }
}

// A model file placed somewhere in a scene or prefab, and everything the placement changed about it, keyed
// by the model's own object ids.
public sealed class UnityModelPart
{
    public UnityPackageAsset Model = null!;
    public UnitySceneObject Root = null!;
    public readonly Dictionary<long, SortedDictionary<int, string>> MaterialsById = new();
    public readonly Dictionary<long, bool> ActiveById = new();
    public readonly Dictionary<long, bool> EnabledById = new();
    public readonly Dictionary<long, Dictionary<string, float>> TransformOverrides = new();
    public readonly Dictionary<long, string> NamesById = new();
    // Renderer id -> the project's shape index -> weight, 0..100.
    public readonly Dictionary<long, SortedDictionary<int, float>> ShapeWeights = new();
    private readonly Dictionary<long, UnitySceneObject> _nodes = new();
    private UnityModelMeta? _meta;

    public UnityModelMeta Meta => _meta ??= UnityModelMeta.Parse(Model.Meta);

    public static bool IsRootId(long id)
        => id == RootTransform || id == RootTransformFolded || id == RootObject || id == RootObjectFolded || id == 400000 || id == 100000;

    public static readonly long RootTransform = UnityModelMeta.ObjectId("Transform", "", true);
    public static readonly long RootTransformFolded = UnityModelMeta.ObjectId("Transform", "", false);
    public static readonly long RootObject = UnityModelMeta.GameObjectId("", true);
    public static readonly long RootObjectFolded = UnityModelMeta.GameObjectId("", false);

    public UnitySceneObject Node(long id, long alt = 0)
    {
        if (IsRootId(id))
            return Root;
        if (_nodes.TryGetValue(id, out var node))
            return node;
        node = new UnitySceneObject { Part = this, ModelId = id, ModelIdAlt = alt, Parent = Root, Name = NamesById.GetValueOrDefault(id, "") };
        Root.Children.Add(node);
        _nodes[id] = node;
        if (alt != 0)
            _nodes.TryAdd(alt, node);
        return node;
    }

    // A change the placement made to one of the model's objects.
    internal void Apply(long target, string path, string value, UnityNode? reference)
    {
        bool root = IsRootId(target);
        if (path == "m_Name")
        {
            NamesById[target] = value;
            if (root)
                Root.Name = value;
        }
        else if (path == "m_IsActive")
        {
            ActiveById[target] = value != "0";
            if (root)
                Root.Active = value != "0";
        }
        else if (path == "m_TagString")
        {
            // A node tagged to stay in the editor is gone from the build; here it stays switched off.
            if (root)
                Root.EditorOnly = value == "EditorOnly";
            else if (value == "EditorOnly")
                ActiveById[target] = false;
        }
        else if (path == "m_Enabled")
        {
            EnabledById[target] = value != "0";
        }
        else if (Index(path, "m_Materials.Array.data[") is int slot)
        {
            var guid = reference?.Ref.Guid;
            if (!string.IsNullOrEmpty(guid))
            {
                if (!MaterialsById.TryGetValue(target, out var slots))
                    MaterialsById[target] = slots = new SortedDictionary<int, string>();
                slots[slot] = guid!;
            }
        }
        else if (Index(path, "m_BlendShapeWeights.Array.data[") is int shape)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight))
            {
                if (!ShapeWeights.TryGetValue(target, out var weights))
                    ShapeWeights[target] = weights = new SortedDictionary<int, float>();
                weights[shape] = weight;
            }
        }
        else if ((path.StartsWith("m_LocalPosition.", StringComparison.Ordinal) || path.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
            || path.StartsWith("m_LocalScale.", StringComparison.Ordinal))
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
        {
            if (root)
                UnityPrefabGraph.SetPose(Root, path, number);
            else
            {
                if (!TransformOverrides.TryGetValue(target, out var fields))
                    TransformOverrides[target] = fields = new Dictionary<string, float>(StringComparer.Ordinal);
                fields[path] = number;
            }
        }
    }

    internal static int? Index(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        int close = path.IndexOf(']', prefix.Length);
        if (close != path.Length - 1)
            return null;
        return int.TryParse(path.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : null;
    }
}

// Parsed text assets, kept for the prefabs that scenes place over and over.
public sealed class UnityDocumentCache
{
    private readonly UnityPackageArchive _archive;
    private readonly Dictionary<string, List<UnityDocument>?> _prefabs = new(StringComparer.OrdinalIgnoreCase);

    public UnityDocumentCache(UnityPackageArchive archive) => _archive = archive;

    public UnityPackageArchive Archive => _archive;

    // The last scene read, kept too: discovery reads each scene twice in a row.
    private (string Guid, List<UnityDocument>? Docs) _last;

    public List<UnityDocument>? Documents(UnityPackageAsset asset)
    {
        bool keep = asset.Extension == ".prefab";
        if (keep && _prefabs.TryGetValue(asset.Guid, out var known))
            return known;
        if (!keep && string.Equals(_last.Guid, asset.Guid, StringComparison.OrdinalIgnoreCase))
            return _last.Docs;
        List<UnityDocument>? docs;
        try
        {
            docs = UnityYaml.Parse(_archive.ReadText(asset) ?? "");
        }
        catch
        {
            docs = null;
        }
        if (keep)
            _prefabs[asset.Guid] = docs;
        else
            _last = (asset.Guid, docs);
        return docs;
    }
}

// A scene or prefab with every prefab placed in it opened up the way the editor shows it: each placement's
// objects instanced afresh, its overrides applied, its removed parts gone and the objects added to it hung
// where they were added. Model files stay closed, as placements carrying their overrides by object id, since
// their insides only exist once the model is imported.
//
// Every object and script gets one id in the graph. The file's own keep theirs; one inside a placement takes
// the editor's own rule for nested objects, the placement's id XOR its id inside the placed prefab, top bit
// cleared, applied at each level outward. References inside the scripts are rewritten to those ids, so a
// script deep in a placed prefab and one in the scene look things up the same way. -xlinka
public sealed class UnityPrefabGraph
{
    private const int GameObjectClass = 1;
    private const int TransformClass = 4;
    private const int RectTransformClass = 224;
    private const int PrefabInstanceClass = 1001;
    private const int MaxDepth = 12;

    public readonly UnityPackageAsset File;
    public readonly List<UnitySceneObject> Roots = new();
    public readonly List<UnitySceneObject> Objects = new();
    public readonly List<UnityModelPart> Parts = new();
    // Placements of prefabs and models that are not in the package, by name, and where they hang.
    public readonly List<(string Name, UnitySceneObject? Parent)> Missing = new();
    private readonly List<(string Name, Scope Scope, long Father)> _missing = new();

    private readonly UnityDocumentCache _cache;
    private readonly bool _full;
    private readonly Dictionary<long, UnitySceneObject> _byId = new();
    private readonly Dictionary<long, (UnityDocument Doc, UnitySceneObject Owner)> _components = new();
    private readonly List<(UnitySceneObject Child, Scope Scope, long Father)> _parenting = new();

    private UnityPrefabGraph(UnityDocumentCache cache, UnityPackageAsset file, bool full)
    {
        _cache = cache;
        File = file;
        _full = full;
    }

    // Light reads only names, poses, switches and which scripts sit where; a full read also clones every
    // script with its overrides applied, for an import.
    public static UnityPrefabGraph? Build(UnityDocumentCache cache, UnityPackageAsset file, bool full)
    {
        var docs = cache.Documents(file);
        if (docs == null)
            return null;
        var graph = new UnityPrefabGraph(cache, file, full);
        var scope = new Scope(file, docs, null, 0);
        var roots = graph.Instantiate(scope, new List<Mod>(), new HashSet<long>(), 0);
        foreach (var (child, owner, father) in graph._parenting)
        {
            var parent = father != 0 ? graph.Lookup(owner.Global(father)) : null;
            if (parent == null || ReferenceEquals(parent, child) || parent.IsUnder(child))
                continue;
            child.Parent = parent;
            parent.Children.Add(child);
        }
        foreach (var root in roots)
            if (root.Parent == null)
                graph.Roots.Add(root);
        foreach (var (name, owner, father) in graph._missing)
            graph.Missing.Add((name, father != 0 ? graph.Lookup(owner.Global(father)) : null));
        return graph;
    }

    public UnitySceneObject? Lookup(long id) => _byId.TryGetValue(id, out var o) ? o : null;

    public UnitySceneObject? OwnerOfComponent(long id) => _components.TryGetValue(id, out var c) ? c.Owner : null;

    public UnityDocument? Component(long id) => _components.TryGetValue(id, out var c) ? c.Doc : null;

    // ---- building ----

    private readonly record struct Mod(long Target, string Path, string Value, UnityNode? Reference);

    private sealed class Placement
    {
        public UnityDocument Doc = null!;
        public UnityPackageAsset? Source;
        public Scope? Scope;
        public UnityModelPart? Part;
        public List<UnitySceneObject> Roots = new();
        public readonly List<Mod> Mods = new();
        public readonly HashSet<long> Removed = new();
    }

    private sealed class Scope
    {
        public readonly UnityPackageAsset Asset;
        public readonly Dictionary<long, UnityDocument> Docs = new();
        public readonly Scope? Outer;
        public readonly long Instance;
        // A stand-in's id -> the placement it stands in for and that object's id in the placed file.
        public readonly Dictionary<long, (long Instance, long Source)> Stand = new();
        public readonly Dictionary<long, Placement> Placements = new();

        public Scope(UnityPackageAsset asset, List<UnityDocument> docs, Scope? outer, long instance)
        {
            Asset = asset;
            Outer = outer;
            Instance = instance;
            foreach (var d in docs)
            {
                Docs[d.FileId] = d;
                if (d.Stripped)
                {
                    long inst = d["m_PrefabInstance"]?.Ref.FileId ?? 0;
                    long source = d["m_CorrespondingSourceObject"]?.Ref.FileId ?? 0;
                    if (inst != 0 && source != 0)
                        Stand[d.FileId] = (inst, source);
                }
            }
        }

        public int Depth => Outer == null ? 0 : Outer.Depth + 1;

        // This file's id for something in the whole graph's terms.
        public long Global(long id)
        {
            var scope = this;
            for (int guard = 0; scope != null && guard < 64; guard++)
            {
                if (scope.Stand.TryGetValue(id, out var stand))
                    id = Combine(stand.Instance, stand.Source);
                if (scope.Outer == null)
                    return id;
                id = Combine(scope.Instance, id);
                scope = scope.Outer;
            }
            return id;
        }
    }

    private static long Combine(long instance, long id) => (instance ^ id) & long.MaxValue;

    private List<UnitySceneObject> Instantiate(Scope scope, List<Mod> incoming, HashSet<long> removed, int depth)
    {
        var docs = scope.Docs;
        var objects = new Dictionary<long, UnitySceneObject>();
        var roots = new List<UnitySceneObject>();

        foreach (var d in docs.Values)
        {
            if (d.ClassId == PrefabInstanceClass && !d.Stripped)
            {
                var guid = (d["m_SourcePrefab"] ?? d["m_ParentPrefab"])?.Ref.Guid;
                var source = _cache.Archive.Find(guid);
                var placement = new Placement { Doc = d, Source = source?.File != null ? source : null };
                scope.Placements[d.FileId] = placement;
                ReadPlacementMods(scope, placement);
                if (placement.Source == null)
                {
                    var named = placement.Mods.Find(m => m.Path == "m_Name").Value;
                    long father = (d["m_Modification"]?["m_TransformParent"] ?? d["m_TransformParent"])?.Ref.FileId ?? 0;
                    _missing.Add((source?.Path ?? (string.IsNullOrEmpty(named) ? "a prefab " + guid : "'" + named + "'"), scope, father));
                }
            }
        }

        // What the outer file changed or took away: on this file's own objects, or handed on inward.
        var own = new Dictionary<long, List<Mod>>();
        foreach (var mod in incoming)
            Route(scope, mod.Target, mod, own, null);
        var removedOwn = new HashSet<long>();
        foreach (var id in removed)
            Route(scope, id, default, null, removedOwn);

        foreach (var d in docs.Values)
        {
            if (d.ClassId != GameObjectClass || d.Stripped || removedOwn.Contains(d.FileId))
                continue;
            var obj = new UnitySceneObject
            {
                Name = d["m_Name"]?.Text ?? "",
                Active = (d["m_IsActive"]?.Int(1) ?? 1) != 0,
                EditorOnly = UnityCandidateFinder.IsEditorOnly(d),
            };
            if (own.TryGetValue(d.FileId, out var changes))
                foreach (var m in changes)
                {
                    if (m.Path == "m_Name")
                        obj.Name = m.Value;
                    else if (m.Path == "m_IsActive")
                        obj.Active = m.Value != "0";
                    else if (m.Path == "m_TagString")
                        obj.EditorOnly = m.Value == "EditorOnly";
                }
            objects[d.FileId] = obj;
            Objects.Add(obj);
            _byId[scope.Global(d.FileId)] = obj;
        }

        foreach (var d in docs.Values)
        {
            if (d.ClassId is not (TransformClass or RectTransformClass) || d.Stripped)
                continue;
            long go = d["m_GameObject"]?.Ref.FileId ?? 0;
            if (!objects.TryGetValue(go, out var obj))
                continue;
            obj.Position = Vector(d["m_LocalPosition"], float3.Zero);
            obj.Scale = Vector(d["m_LocalScale"], float3.One);
            var r = d["m_LocalRotation"];
            obj.Rotation = r == null ? floatQ.Identity : new floatQ(r["x"]?.Float() ?? 0f, r["y"]?.Float() ?? 0f, r["z"]?.Float() ?? 0f, r["w"]?.Float(1f) ?? 1f);
            if (own.TryGetValue(d.FileId, out var changes))
                foreach (var m in changes)
                    if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        SetPose(obj, m.Path, number);
            _byId[scope.Global(d.FileId)] = obj;
            long father = d["m_Father"]?.Ref.FileId ?? 0;
            if (father != 0)
                _parenting.Add((obj, scope, father));
            else
                roots.Add(obj);
        }

        // Scripts and renderers. One added onto an object of a placement waits until that object exists.
        var onPlaced = new List<(UnityDocument Doc, long GameObject, long Id)>();
        foreach (var d in docs.Values)
        {
            if (d.Stripped || d.ClassId is GameObjectClass or TransformClass or RectTransformClass or PrefabInstanceClass)
                continue;
            long go = d["m_GameObject"]?.Ref.FileId ?? 0;
            if (go == 0 || removedOwn.Contains(d.FileId))
                continue;
            var doc = d;
            if (_full && scope.Outer != null)
            {
                doc = Clone(d, scope);
                if (own.TryGetValue(d.FileId, out var changes))
                    foreach (var m in changes)
                        SetPath(doc.Body, m.Path, m.Reference ?? new UnityScalar(m.Value));
            }
            if (objects.TryGetValue(go, out var owner))
            {
                owner.Components.Add(doc);
                _components[scope.Global(d.FileId)] = (doc, owner);
            }
            else
            {
                onPlaced.Add((doc, go, scope.Global(d.FileId)));
            }
        }
        foreach (var obj in objects)
            obj.Value.Id = scope.Global(obj.Key);

        foreach (var (id, placement) in scope.Placements)
        {
            if (placement.Source == null)
                continue;
            long father = (placement.Doc["m_Modification"]?["m_TransformParent"] ?? placement.Doc["m_TransformParent"])?.Ref.FileId ?? 0;
            if (IsModel(placement.Source))
            {
                var part = new UnityModelPart { Model = placement.Source };
                part.Root = new UnitySceneObject { Name = placement.Source.Name, Part = part, IsPartRoot = true };
                foreach (var m in placement.Mods)
                    part.Apply(m.Target, m.Path, m.Value, m.Reference);
                foreach (var gone in placement.Removed)
                    part.ActiveById[gone] = false;
                placement.Part = part;
                placement.Roots.Add(part.Root);
                Parts.Add(part);
                Objects.Add(part.Root);
                foreach (var rootId in new[] { UnityModelPart.RootTransform, UnityModelPart.RootTransformFolded, UnityModelPart.RootObject, UnityModelPart.RootObjectFolded, 400000L, 100000L })
                    _byId[scope.Global(Combine(id, rootId))] = part.Root;
                part.Root.Id = scope.Global(Combine(id, UnityModelPart.RootTransform));
            }
            else if (placement.Source.Extension == ".prefab" && depth < MaxDepth)
            {
                var docsOf = _cache.Documents(placement.Source);
                if (docsOf == null)
                    continue;
                var inner = new Scope(placement.Source, docsOf, scope, id);
                placement.Scope = inner;
                placement.Roots = Instantiate(inner, placement.Mods, placement.Removed, depth + 1);
            }
            foreach (var root in placement.Roots)
            {
                if (father != 0)
                    _parenting.Add((root, scope, father));
                else
                    roots.Add(root);
            }
        }

        // Stand-ins: what they stand for, under this file's id for them. The outermost file's own scripts
        // keep its ids as written, so there a stand-in answers to its own id as well.
        foreach (var (stand, (inst, source)) in scope.Stand)
        {
            if (!scope.Placements.TryGetValue(inst, out var placement))
                continue;
            var target = Find(placement, source);
            if (target == null)
                continue;
            _byId[scope.Global(stand)] = target;
            if (scope.Outer == null)
                _byId[stand] = target;
        }
        foreach (var (doc, go, id) in onPlaced)
        {
            if (Lookup(scope.Global(go)) is not { } owner)
                continue;
            owner.Components.Add(doc);
            _components[id] = (doc, owner);
        }
        return roots;
    }

    // Something inside a placement, by its id in the placed file.
    private UnitySceneObject? Find(Placement placement, long id)
    {
        if (placement.Part != null)
            return placement.Part.Node(id);
        if (placement.Scope == null)
            return null;
        var scope = placement.Scope;
        if (_byId.TryGetValue(scope.Global(id), out var known))
            return known;
        if (scope.Stand.TryGetValue(id, out var stand) && scope.Placements.TryGetValue(stand.Instance, out var inner))
            return Find(inner, stand.Source);
        Placement? model = null;
        long modelId = 0;
        foreach (var (inst, nested) in scope.Placements)
        {
            long a = inst ^ id, b = a ^ long.MinValue;
            if (nested.Part != null)
            {
                if (model == null)
                {
                    model = nested;
                    modelId = a;
                }
                continue;
            }
            if (nested.Source == null)
                continue;
            if (OwnsDeep(nested.Source, a, 0))
                return Find(nested, a);
            if (OwnsDeep(nested.Source, b, 0))
                return Find(nested, b);
        }
        return model?.Part!.Node(modelId, modelId ^ long.MinValue);
    }

    // The ids a prefab file answers to: its own objects and everything inside what it places.
    private sealed class AssetIds
    {
        public readonly HashSet<long> Ids = new();
        public readonly List<(long Instance, UnityPackageAsset Source)> Prefabs = new();
    }

    private readonly Dictionary<string, AssetIds?> _assetIds = new(StringComparer.OrdinalIgnoreCase);

    private AssetIds? IdsOf(UnityPackageAsset asset)
    {
        if (_assetIds.TryGetValue(asset.Guid, out var known))
            return known;
        AssetIds? ids = null;
        if (asset.Extension == ".prefab" && _cache.Documents(asset) is { } docs)
        {
            ids = new AssetIds();
            foreach (var d in docs)
            {
                ids.Ids.Add(d.FileId);
                if (d.ClassId == PrefabInstanceClass && !d.Stripped
                    && _cache.Archive.Find((d["m_SourcePrefab"] ?? d["m_ParentPrefab"])?.Ref.Guid) is { File: not null, Extension: ".prefab" } source)
                    ids.Prefabs.Add((d.FileId, source));
            }
        }
        _assetIds[asset.Guid] = ids;
        return ids;
    }

    private bool OwnsDeep(UnityPackageAsset asset, long id, int depth)
    {
        var ids = IdsOf(asset);
        if (ids == null)
            return false;
        if (ids.Ids.Contains(id))
            return true;
        if (depth > MaxDepth)
            return false;
        foreach (var (inst, source) in ids.Prefabs)
        {
            long a = inst ^ id;
            if (OwnsDeep(source, a, depth + 1) || OwnsDeep(source, a ^ long.MinValue, depth + 1))
                return true;
        }
        return false;
    }

    // A change or a removal aimed at this file's id: kept when it is one of the file's own objects, handed to
    // the placement it belongs to otherwise. An id inside a placed model cannot be checked before the import,
    // so it goes to every model placed here under both ids the top bit allows; only the right one matches.
    private void Route(Scope scope, long id, Mod mod, Dictionary<long, List<Mod>>? own, HashSet<long>? removed)
    {
        if (scope.Docs.TryGetValue(id, out var doc) && !doc.Stripped)
        {
            if (own != null)
            {
                if (!own.TryGetValue(id, out var list))
                    own[id] = list = new List<Mod>();
                list.Add(mod);
            }
            removed?.Add(id);
            return;
        }
        if (scope.Stand.TryGetValue(id, out var stand) && scope.Placements.TryGetValue(stand.Instance, out var standing))
        {
            Hand(standing, stand.Source, mod, own != null);
            return;
        }
        Placement? model = null;
        foreach (var (inst, placement) in scope.Placements)
        {
            long a = inst ^ id, b = a ^ long.MinValue;
            if (placement.Source == null)
                continue;
            if (IsModel(placement.Source))
            {
                model ??= placement;
                continue;
            }
            if (OwnsDeep(placement.Source, a, 0))
            {
                Hand(placement, a, mod, own != null);
                return;
            }
            if (OwnsDeep(placement.Source, b, 0))
            {
                Hand(placement, b, mod, own != null);
                return;
            }
        }
        if (model == null)
            return;
        foreach (var (inst, placement) in scope.Placements)
        {
            if (placement.Source == null || !IsModel(placement.Source))
                continue;
            long a = inst ^ id;
            Hand(placement, a, mod, own != null);
            Hand(placement, a ^ long.MinValue, mod, own != null);
        }
    }

    private static void Hand(Placement placement, long id, Mod mod, bool isMod)
    {
        if (isMod)
            placement.Mods.Add(mod with { Target = id });
        else
            placement.Removed.Add(id);
    }

    // A placement's own changes, read in the placing file's terms, ahead of anything handed in from outside.
    private void ReadPlacementMods(Scope scope, Placement placement)
    {
        UnityNode modification = placement.Doc["m_Modification"] ?? placement.Doc.Body;
        if (modification["m_Modifications"] is UnitySeq mods)
        {
            foreach (var item in mods.Items)
            {
                var target = item["target"]?.Ref ?? default;
                var path = item["propertyPath"]?.Text ?? "";
                if (path.Length == 0 || target.FileId == 0)
                    continue;
                var reference = item["objectReference"];
                UnityNode? value = null;
                if (reference != null && !reference.Ref.IsNull)
                    value = LocalReference(reference, scope);
                placement.Mods.Add(new Mod(target.FileId, path, item["value"]?.Text ?? "", value));
            }
        }
        foreach (var key in new[] { "m_RemovedComponents", "m_RemovedGameObjects" })
            if (modification[key] is UnitySeq gone)
                foreach (var item in gone.Items)
                    if (item.Ref.FileId != 0)
                        placement.Removed.Add(item.Ref.FileId);
    }

    // A reference written in a file, in graph terms: an object of the same file becomes its graph id, an
    // asset keeps its GUID.
    private static UnityNode LocalReference(UnityNode reference, Scope scope)
    {
        var r = reference.Ref;
        if (!string.IsNullOrEmpty(r.Guid) || r.FileId == 0)
            return reference;
        var map = new UnityMap();
        map.Entries.Add(new KeyValuePair<string, UnityNode>("fileID", new UnityScalar(scope.Global(r.FileId).ToString(CultureInfo.InvariantCulture))));
        return map;
    }

    private static UnityDocument Clone(UnityDocument d, Scope scope)
    {
        return new UnityDocument
        {
            ClassId = d.ClassId,
            FileId = scope.Global(d.FileId),
            Stripped = d.Stripped,
            TypeName = d.TypeName,
            Body = (UnityMap)CloneNode(d.Body, scope),
        };
    }

    private static UnityNode CloneNode(UnityNode node, Scope scope)
    {
        switch (node)
        {
            case UnityMap map:
            {
                if (map["fileID"] is UnityScalar id && map["guid"] is not { Text.Length: > 0 }
                    && long.TryParse(id.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long local) && local != 0)
                {
                    var copy = new UnityMap();
                    foreach (var pair in map.Entries)
                        copy.Entries.Add(pair.Key == "fileID"
                            ? new KeyValuePair<string, UnityNode>("fileID", new UnityScalar(scope.Global(local).ToString(CultureInfo.InvariantCulture)))
                            : pair);
                    return copy;
                }
                var clone = new UnityMap();
                foreach (var pair in map.Entries)
                    clone.Entries.Add(new KeyValuePair<string, UnityNode>(pair.Key, CloneNode(pair.Value, scope)));
                return clone;
            }
            case UnitySeq seq:
            {
                var clone = new UnitySeq();
                foreach (var item in seq.Items)
                    clone.Items.Add(CloneNode(item, scope));
                return clone;
            }
            default:
                return node;
        }
    }

    // "a.b.Array.data[2].c" written into a document: maps along the way are made when missing, lists grow.
    internal static void SetPath(UnityMap body, string path, UnityNode value)
    {
        var parts = path.Split('.');
        UnityNode current = body;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            bool last = i == parts.Length - 1;
            if (part == "Array" && i + 1 < parts.Length && current is UnitySeq seq)
            {
                var next = parts[++i];
                if (next == "size")
                {
                    if (int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) && size >= 0 && size < 100_000)
                    {
                        while (seq.Items.Count > size)
                            seq.Items.RemoveAt(seq.Items.Count - 1);
                        while (seq.Items.Count < size)
                            seq.Items.Add(seq.Items.Count > 0 ? CloneShape(seq.Items[^1]) : new UnityScalar(""));
                    }
                    return;
                }
                if (!next.StartsWith("data[", StringComparison.Ordinal) || !next.EndsWith(']')
                    || !int.TryParse(next.AsSpan(5, next.Length - 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0 || index > 100_000)
                    return;
                while (seq.Items.Count <= index)
                    seq.Items.Add(seq.Items.Count > 0 ? CloneShape(seq.Items[^1]) : new UnityScalar(""));
                if (i == parts.Length - 1)
                {
                    seq.Items[index] = value;
                    return;
                }
                current = seq.Items[index];
                continue;
            }
            if (current is not UnityMap map)
                return;
            int at = map.Entries.FindIndex(e => e.Key == part);
            if (last)
            {
                var entry = new KeyValuePair<string, UnityNode>(part, value);
                if (at >= 0)
                    map.Entries[at] = entry;
                else
                    map.Entries.Add(entry);
                return;
            }
            if (at < 0)
            {
                UnityNode made = i + 1 < parts.Length && parts[i + 1] == "Array" ? new UnitySeq() : new UnityMap();
                map.Entries.Add(new KeyValuePair<string, UnityNode>(part, made));
                current = made;
            }
            else
            {
                current = map.Entries[at].Value;
            }
        }
    }

    // A fresh list item shaped like its neighbour, for an index past the end.
    private static UnityNode CloneShape(UnityNode node) => node switch
    {
        UnityMap map => CloneMap(map),
        UnitySeq => new UnitySeq(),
        _ => new UnityScalar(""),
    };

    private static UnityMap CloneMap(UnityMap map)
    {
        var copy = new UnityMap();
        foreach (var pair in map.Entries)
            copy.Entries.Add(new KeyValuePair<string, UnityNode>(pair.Key, pair.Value is UnityMap inner ? CloneMap(inner) : pair.Value));
        return copy;
    }

    internal static void SetPose(UnitySceneObject obj, string path, float value)
    {
        switch (path)
        {
            case "m_LocalPosition.x": obj.Position.x = value; break;
            case "m_LocalPosition.y": obj.Position.y = value; break;
            case "m_LocalPosition.z": obj.Position.z = value; break;
            case "m_LocalRotation.x": obj.Rotation = new floatQ(value, obj.Rotation.y, obj.Rotation.z, obj.Rotation.w); break;
            case "m_LocalRotation.y": obj.Rotation = new floatQ(obj.Rotation.x, value, obj.Rotation.z, obj.Rotation.w); break;
            case "m_LocalRotation.z": obj.Rotation = new floatQ(obj.Rotation.x, obj.Rotation.y, value, obj.Rotation.w); break;
            case "m_LocalRotation.w": obj.Rotation = new floatQ(obj.Rotation.x, obj.Rotation.y, obj.Rotation.z, value); break;
            case "m_LocalScale.x": obj.Scale.x = value; break;
            case "m_LocalScale.y": obj.Scale.y = value; break;
            case "m_LocalScale.z": obj.Scale.z = value; break;
        }
    }

    private static float3 Vector(UnityNode? node, float3 fallback)
        => node == null ? fallback : new float3(node["x"]?.Float(fallback.x) ?? fallback.x, node["y"]?.Float(fallback.y) ?? fallback.y, node["z"]?.Float(fallback.z) ?? fallback.z);

    internal static bool IsModel(UnityPackageAsset asset) => UnityCandidateFinder.IsModelFile(asset);
}
