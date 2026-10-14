// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets.Interop.Unity;

// VRCFury's FullController, read into what the menu plan merges: each one adds its own controllers to the
// avatar's, its menus at the folder it names and its parameter sets, with every clip path in them measured from
// the object it sits on rather than the avatar's root.
//
// At upload it gives every parameter that is not global a name of its own, so two prefabs that both say
// "Toggle" never touch. Here a name is only changed where two sources really share it: the same separation,
// and a receiver or bone inside the prefab still finds the parameter by the name it was given.
//
// A VRCFury toggle that sets a global parameter is what the merged layers read it from (the hoodie toggle
// pulls the hood layer along), so those toggles are handed over as menu entries too. -xlinka
public static class UnityFullController
{
    public static List<UnityVrcPlan.Merge> Merges(UnityPackageArchive archive, UnityImportCandidate candidate)
    {
        var merges = new List<UnityVrcPlan.Merge>();
        var tools = UnityBuildTools.Of(candidate);
        tools.IconFiles.Clear();
        void IconFile(string? guid)
        {
            if (guid != null && archive.Find(guid) is { File: { } file } asset && asset.Extension is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".webp")
                tools.IconFiles.TryAdd(guid, (file, asset.Extension));
        }
        foreach (var part in candidate.Toggles)
            if (part.Data is { } toggle && (toggle["enableIcon"]?.Int() ?? 0) != 0)
                IconFile(UnityVrcFury.Guid(toggle["icon"]));
        foreach (var part in tools.Icons)
            IconFile(UnityVrcFury.Guid(part.Data?["icon"]));
        var controllers = new Dictionary<string, UnityAnimatorController?>(StringComparer.OrdinalIgnoreCase);
        UnityAnimatorController? Controller(string guid)
        {
            if (!controllers.TryGetValue(guid, out var known))
                controllers[guid] = known = UnityAnimatorController.Parse(archive.ReadText(archive.Find(guid)));
            return known;
        }

        // Every name a source uses, so a private name can be told apart from one two sources share.
        var users = new Dictionary<string, int>(StringComparer.Ordinal);
        void Uses(IEnumerable<string> names)
        {
            foreach (var name in names.Distinct(StringComparer.Ordinal))
                users[name] = users.GetValueOrDefault(name) + 1;
        }
        var own = new HashSet<string>(StringComparer.Ordinal);
        if (candidate.Descriptor is { } descriptor)
        {
            if (descriptor["baseAnimationLayers"] is UnitySeq layers)
                foreach (var layer in layers.Items)
                    if ((layer["isDefault"]?.Int(1) ?? 1) == 0 && layer["animatorController"]?.Ref.Guid is { Length: > 0 } guid && Controller(guid) is { } c)
                        own.UnionWith(c.Parameters.Select(p => p.Name));
            if (UnityExpressionParameters.Parse(archive.ReadText(archive.Find(descriptor["expressionParameters"]?.Ref.Guid))) is { } set)
                own.UnionWith(set.ByName.Keys);
        }
        var toggles = GlobalToggles(candidate);
        own.UnionWith(toggles.Select(t => t.Parameter));
        Uses(own);

        var sources = new List<(UnityComponentRef Part, UnityVrcPlan.Merge Merge, HashSet<string> Names, HashSet<string> Synced)>();
        foreach (var part in tools.FullControllers)
        {
            var data = part.Data;
            if (data == null)
                continue;
            var merge = new UnityVrcPlan.Merge { Name = part.Node.Name };
            var root = part.Node;
            if (data["rootObjOverride"]?.Ref.FileId is long overrideId and not 0 && candidate.ResolveObject(overrideId) is { } other)
                root = other;
            merge.PathPrefix = PathOf(root) ?? "";
            merge.RootBindingsApplyToAvatar = (data["rootBindingsApplyToAvatar"]?.Int() ?? 0) != 0;

            var names = new HashSet<string>(StringComparer.Ordinal);
            var synced = new HashSet<string>(StringComparer.Ordinal);
            // A locomotion, sitting or calibration pose replacement has nothing to do here: the avatar is
            // moved, seated and calibrated by our own systems. A prefab that is only that is left out whole.
            bool bodyOnly = false;
            void AddController(UnityNode? wrapped, int type)
            {
                if (UnityVrcFury.Guid(wrapped) is not { } guid || Controller(guid) is not { } c)
                    return;
                if (type is 0 or 6 or 7 or 8)
                {
                    bodyOnly = true;
                    return;
                }
                merge.Controllers.Add((type, guid));
                names.UnionWith(c.Parameters.Select(p => p.Name));
            }
            if (data["controllers"] is UnitySeq list)
                foreach (var item in list.Items)
                    AddController(item["controller"], item["type"]?.Int(5) ?? 5);
            AddController(data["controller"], 5);

            void AddMenu(UnityNode? wrapped, string prefix)
            {
                if (UnityVrcFury.Guid(wrapped) is { } guid && archive.Find(guid) != null)
                    merge.Menus.Add((guid, prefix.Trim('/')));
            }
            if (data["menus"] is UnitySeq menus)
                foreach (var item in menus.Items)
                    AddMenu(item["menu"], item["prefix"]?.Text ?? "");
            AddMenu(data["menu"], data["submenu"]?.Text ?? "");

            void AddParameters(UnityNode? wrapped)
            {
                if (UnityVrcFury.Guid(wrapped) is not { } guid || UnityExpressionParameters.Parse(archive.ReadText(archive.Find(guid))) is not { } set)
                    return;
                merge.ParameterSets.Add(guid);
                names.UnionWith(set.ByName.Keys);
                synced.UnionWith(set.ByName.Keys);
            }
            if (data["prms"] is UnitySeq prms)
                foreach (var item in prms.Items)
                    AddParameters(item["parameters"]);
            AddParameters(data["parameters"]);

            if (data["rewriteBindings"] is UnitySeq rewrites)
                foreach (var item in rewrites.Items)
                    merge.Rewrites.Add((Trim(item["from"]?.Text), Trim(item["to"]?.Text), (item["delete"]?.Int() ?? 0) != 0));
            if (data["removePrefixes"] is UnitySeq removed)
                foreach (var item in removed.Items)
                    if (Trim(item.Text) is { Length: > 0 } prefix)
                        merge.Rewrites.Add((prefix, "", false));
            if (Trim(data["addPrefix"]?.Text) is { Length: > 0 } added)
                merge.Rewrites.Add(("", added, false));

            if (merge.Controllers.Count == 0 && (merge.Menus.Count == 0 || bodyOnly))
            {
                if (bodyOnly)
                    Logger.Debug($"UnityPackage: full controller '{merge.Name}' only replaces locomotion; left out");
                continue;
            }
            sources.Add((part, merge, names, synced));
            Uses(names);
        }

        // Private names shared with another source get the prefab's own.
        for (int i = 0; i < sources.Count; i++)
        {
            var (part, merge, names, synced) = sources[i];
            var data = part.Data!;
            var global = new HashSet<string>(StringComparer.Ordinal);
            var notGlobal = new HashSet<string>(StringComparer.Ordinal);
            bool all = false;
            if (data["globalParams"] is UnitySeq globals)
                foreach (var item in globals.Items)
                {
                    var text = item.Text?.Trim() ?? "";
                    if (text == "*")
                        all = true;
                    else if (text.StartsWith('!'))
                        notGlobal.Add(text[1..]);
                    else if (text.Length > 0)
                        global.Add(text);
                }
            bool nonsynced = (data["allNonsyncedAreGlobal"]?.Int() ?? 0) != 0;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || UnityVrcPlan.IsBuiltin(name) || global.Contains(name)
                    || all && !notGlobal.Contains(name) || nonsynced && !synced.Contains(name))
                    continue;
                if (users.GetValueOrDefault(name) > 1)
                    merge.Parameters[name] = $"VF{i}_{name}";
            }
            merges.Add(merge);
            Logger.Debug($"UnityPackage: full controller '{merge.Name}' at '{merge.PathPrefix}': {merge.Controllers.Count} controllers, "
                + $"{merge.Menus.Count} menus, {merge.ParameterSets.Count} parameter sets, {merge.Parameters.Count} renamed, {merge.Rewrites.Count} rewrites");
        }

        if (toggles.Count > 0)
        {
            var controls = new UnityVrcPlan.Merge { Name = "Toggles" };
            foreach (var toggle in toggles)
                controls.Controls.Add(toggle);
            merges.Add(controls);
        }
        merges.AddRange(UnityModularAvatarPlan.Merges(archive, candidate));
        return merges;
    }

    // VRCFury toggles that set a parameter of their own choosing, as the menu entries they stand for.
    private static List<(string Path, string Parameter, bool Slider, float Default)> GlobalToggles(UnityImportCandidate candidate)
    {
        var found = new List<(string Path, string Parameter, bool Slider, float Default)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in candidate.Toggles)
        {
            var data = part.Data;
            if (data == null || part.Feature != "Toggle" || (data["useGlobalParam"]?.Int() ?? 0) == 0)
                continue;
            var parameter = data["globalParam"]?.Text?.Trim();
            if (string.IsNullOrEmpty(parameter) || !seen.Add(parameter))
                continue;
            bool slider = (data["slider"]?.Int() ?? 0) != 0;
            float value = slider ? System.Math.Clamp(data["defaultSliderValue"]?.Float() ?? 0f, 0f, 1f) : (data["defaultOn"]?.Int() ?? 0) != 0 ? 1f : 0f;
            found.Add((data["name"]?.Text ?? parameter, parameter, slider, value));
        }
        return found;
    }

    // The object's path from the avatar's root, as clip paths name it; null for a node inside a placed model,
    // whose path the project never wrote down.
    internal static string? PathOf(UnityNodeRef node)
    {
        if (node.Path != null)
            return node.Path;
        var names = new List<string>();
        for (var at = node; at != null; at = at.Parent)
        {
            // The avatar's root: an object of the project's own, or the main model's own root.
            if (at.IsImportRoot || at.Parent == null && at.Part == null && UnityModelPart.IsRootId(at.ModelId))
            {
                names.Reverse();
                return string.Join('/', names);
            }
            names.Add(at.Name);
        }
        return null;
    }

    private static string Trim(string? path) => (path ?? "").Trim().Trim('/');
}
