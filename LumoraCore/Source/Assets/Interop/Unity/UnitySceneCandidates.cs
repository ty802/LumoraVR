// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// Avatars and prefabs read out of a scene or prefab graph.
//
// Every object carrying an avatar descriptor is an avatar, named as the scene names it: creators keep a
// Quest copy, a FinalIK copy and two colourways side by side, and the names are what tell them apart. A
// prefab with a model placed in it but no descriptor (an outfit, a prop) is offered as an object. A package
// also ships its history: dozens of work-in-progress and backup scenes holding near copies of the same
// avatar. Those are listed apart from the scenes it ships to upload from, by the folder and file names
// creators actually use for them. -xlinka
public static class UnitySceneCandidates
{
    // ---- finding ----

    public static void Find(UnityPrefabGraph graph, List<UnityImportCandidate> found)
    {
        var file = graph.File;
        bool prefab = file.Extension == ".prefab";
        var listing = ListingOf(file.Path);
        var avatars = new List<UnitySceneObject>();
        foreach (var obj in graph.Objects)
        {
            if (obj.IsModelNode || obj.InEditorOnly || !obj.Components.Any(UnityCandidateFinder.IsDescriptor))
                continue;
            // A descriptor nested inside another avatar is that avatar's leftover, not a second one.
            if (avatars.Any(a => obj.IsUnder(a)))
                continue;
            avatars.RemoveAll(a => a.IsUnder(obj));
            avatars.Add(obj);
        }
        foreach (var root in avatars)
        {
            var main = MainPart(graph, root);
            if (main == null)
                continue;
            var candidate = new UnityImportCandidate
            {
                Name = string.IsNullOrWhiteSpace(root.Name) ? main.Model.Name : root.Name,
                SourcePath = file.Path,
                Kind = UnityImportCandidate.Shape.Instance,
                Section = listing,
                Model = main.Model,
                IsAvatar = true,
                Renderers = graph.Parts.Count(p => p.Root.IsUnder(root)),
            };
            candidate.AwaitPrepare(file, root.Id);
            found.Add(candidate);
        }
        if (!prefab || avatars.Count > 0)
            return;
        foreach (var root in graph.Roots)
        {
            if (root.EditorOnly)
                continue;
            var main = MainPart(graph, root);
            if (main == null)
                continue;
            var candidate = new UnityImportCandidate
            {
                Name = string.IsNullOrWhiteSpace(root.Name) ? file.Name : root.Name,
                SourcePath = file.Path,
                Kind = UnityImportCandidate.Shape.Instance,
                Section = listing,
                Model = main.Model,
                Renderers = graph.Parts.Count(p => p.Root.IsUnder(root)),
            };
            candidate.AwaitPrepare(file, root.Id);
            found.Add(candidate);
        }
    }

    // The model the rest hangs off: the avatar root itself when it is a placed model, or else the largest
    // model file placed under it, which is the body.
    private static UnityModelPart? MainPart(UnityPrefabGraph graph, UnitySceneObject root)
    {
        if (root.IsPartRoot && root.Part != null)
            return root.Part;
        UnityModelPart? best = null;
        foreach (var part in graph.Parts)
            if (part.Root.IsUnder(root) && !part.Root.InEditorOnly && (best == null || part.Model.Size > best.Model.Size))
                best = part;
        return best;
    }

    // ---- listing ----

    private static readonly string[] OlderWords = { "wip", "old", "backup", "archive", "trial", "deprecated", "unused", "test", "copy" };

    public static UnityImportCandidate.Listing ListingOf(string path)
    {
        var parts = path.Split('/');
        var name = System.IO.Path.GetFileNameWithoutExtension(parts[^1]);
        bool older = false;
        for (int i = 0; i < parts.Length - 1 && !older; i++)
            older = HasWord(parts[i]);
        older |= HasWord(name);
        if (older)
            return UnityImportCandidate.Listing.OlderScene;
        return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? UnityImportCandidate.Listing.Prefab : UnityImportCandidate.Listing.Scene;
    }

    // A word at the start of a name or after a separator or a case change: "WIP_Versions", "Older_Versions",
    // "_Trial1", "Backups_Prefabs", but not "Golden" or "Ferret".
    private static bool HasWord(string segment)
    {
        foreach (var word in OlderWords)
        {
            int at = 0;
            while ((at = segment.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool start = at == 0 || !char.IsLetter(segment[at - 1]) || (char.IsLower(segment[at - 1]) && char.IsUpper(segment[at]));
                if (start)
                    return true;
                at += word.Length;
            }
        }
        return false;
    }

    // Scenes a creator names as the ones to use come before the rest of their section.
    public static int Rank(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        foreach (var word in new[] { "final", "ready", "upload", "public", "release", "main" })
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                return 0;
        return 1;
    }

    // The highest version number in a file name ("_V110_" -> 110), so the newest of a run comes first.
    public static int Version(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        int best = 0;
        for (int i = 0; i + 1 < name.Length; i++)
        {
            if ((name[i] != 'V' && name[i] != 'v') || !char.IsDigit(name[i + 1]) || (i > 0 && char.IsLetter(name[i - 1])))
                continue;
            int j = i + 1, value = 0;
            while (j < name.Length && char.IsDigit(name[j]) && value < 100_000)
                value = value * 10 + (name[j++] - '0');
            best = System.Math.Max(best, value);
        }
        return best;
    }

    // ---- reading one in full ----

    public static void Fill(UnityImportCandidate candidate, UnityDocumentCache cache, UnityPackageAsset file, long rootId)
    {
        var graph = UnityPrefabGraph.Build(cache, file, full: true);
        var root = graph?.Objects.FirstOrDefault(o => o.Id == rootId);
        if (graph == null || root == null)
        {
            candidate.Notes.Add($"{file.Path}: the setup could not be read again.");
            return;
        }
        var main = MainPart(graph, root);
        if (main == null)
            return;
        candidate.Model = main.Model;
        candidate.RootScale = root.Scale;
        candidate.AuthoredRoot = !root.IsPartRoot;
        candidate.ModelName = main.Root.Name;
        foreach (var (id, slots) in main.MaterialsById)
            candidate.MaterialsById[id] = slots;
        foreach (var (id, on) in main.ActiveById)
            candidate.ActiveById[id] = on;
        foreach (var (id, on) in main.EnabledById)
            candidate.EnabledById[id] = on;
        foreach (var (id, fields) in main.TransformOverrides)
            candidate.TransformOverrides[id] = fields;
        foreach (var (id, name) in main.NamesById)
            candidate.NamesById[id] = name;
        foreach (var (id, weights) in main.ShapeWeights)
            candidate.ShapeWeights[id] = weights;

        // Nodes, parents first, for everything under the avatar's root.
        var under = new List<UnitySceneObject>();
        var seen = new HashSet<UnitySceneObject>();
        var stack = new Stack<UnitySceneObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (!seen.Add(obj) || obj.EditorOnly)
                continue;
            under.Add(obj);
            for (int i = obj.Children.Count - 1; i >= 0; i--)
                stack.Push(obj.Children[i]);
        }
        foreach (var obj in under)
            obj.Node = NodeFor(obj, root, main);
        foreach (var obj in under)
        {
            if (obj.Part != null || ReferenceEquals(obj, root) || obj.Node == null)
                continue;
            candidate.AddedObjects.Add(obj.Node);
        }
        foreach (var part in graph.Parts)
            if (!ReferenceEquals(part, main) && part.Root.IsUnder(root) && !part.Root.InEditorOnly)
                candidate.Parts.Add(part);

        UnityNodeRef? Node(UnitySceneObject? obj) => obj != null && (obj.IsUnder(root) || (obj.Part != null && obj.Part.Root.IsUnder(root))) ? obj.Node : null;
        candidate.ResolveTransform = id => Node(graph.Lookup(id));
        candidate.ResolveObject = id => Node(graph.Lookup(id));
        candidate.ResolveComponent = id => Node(graph.OwnerOfComponent(id) ?? graph.Lookup(id));

        var descriptor = root.Components.FirstOrDefault(UnityCandidateFinder.IsDescriptor);
        if (descriptor != null)
        {
            UnityCandidateFinder.MarkAvatar(candidate, descriptor, root.Scale.y);
            UnityCandidateFinder.ReadVisemes(candidate);
        }
        foreach (var obj in under)
        {
            if (obj.Node == null)
                continue;
            foreach (var doc in obj.Components)
                if (!ReferenceEquals(doc, descriptor))
                    UnityCandidateFinder.Classify(candidate, doc, obj.Node);
            if (obj.Part == null)
                UnityCandidateFinder.CollectMesh(candidate, obj.Components, obj.Node);
        }
        candidate.Renderers = 1 + candidate.Parts.Count;
        foreach (var missing in graph.Missing.Where(m => m.Parent != null && m.Parent.IsUnder(root)).Select(m => m.Name).Distinct().Take(8))
            candidate.Notes.Add($"Placed in the setup but not in the package: {missing}");
    }

    private static UnityNodeRef? NodeFor(UnitySceneObject obj, UnitySceneObject root, UnityModelPart main)
    {
        if (obj.Node != null)
            return obj.Node;
        if (obj.Part != null)
        {
            var part = ReferenceEquals(obj.Part, main) ? null : obj.Part;
            obj.Node = obj.IsPartRoot
                ? new UnityNodeRef { ModelId = UnityModelPart.RootTransform, Part = part, Name = obj.Name, Active = obj.Active, Position = obj.Position, Rotation = obj.Rotation, Scale = obj.Scale }
                : new UnityNodeRef { ModelId = obj.ModelId, ModelIdAlt = obj.ModelIdAlt, Part = part, Name = obj.Name };
            if (obj.IsPartRoot && part != null && obj.Parent != null)
                obj.Node.Parent = NodeFor(obj.Parent, root, main);
            return obj.Node;
        }
        if (ReferenceEquals(obj, root))
            return obj.Node = new UnityNodeRef { IsImportRoot = true, Name = obj.Name };
        var parent = obj.Parent != null ? NodeFor(obj.Parent, root, main) : null;
        if (parent == null)
            return null;
        return obj.Node = new UnityNodeRef
        {
            Parent = parent,
            Name = obj.Name,
            Active = obj.Active,
            Position = obj.Position,
            Rotation = obj.Rotation,
            Scale = obj.Scale,
        };
    }
}
