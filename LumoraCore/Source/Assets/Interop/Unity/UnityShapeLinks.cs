// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Linq;
using Lumora.Core.Components;

namespace Lumora.Core.Assets.Interop.Unity;

// Clothes that follow the body's shapes. VRCFury's blendshape link names a base object and the meshes that
// copy it, every shape they share by name or a list of pairs; Modular Avatar's blendshape sync sits on the
// mesh that follows and lists the shapes it reads from another. Both become a BlendShapeLink on the mesh that
// follows, so a belly or chest slider moves the shirt with the body instead of through it. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private int _shapeLinks;

        private void ShapeLinks(UnityBuildTools.Parts tools)
        {
            foreach (var part in tools.ShapeLinks)
            {
                var data = part.Data;
                if (data == null)
                    continue;
                var baseName = data["baseObj"]?.Text?.Trim() ?? "";
                var source = MeshNamed(baseName);
                if (source == null)
                {
                    Skip("shape links to a base mesh not in the package");
                    continue;
                }
                var targets = new System.Collections.Generic.List<SkinnedMeshRenderer>();
                if (data["linkSkins"] is UnitySeq skins)
                    foreach (var item in skins.Items)
                        if (Component(item["renderer"])?.GetComponent<SkinnedMeshRenderer>() is { } renderer)
                            targets.Add(renderer);
                if (data["objs"] is UnitySeq objects)
                    foreach (var item in objects.Items)
                        if (Object(item) is { } slot)
                            targets.AddRange(slot.GetComponentsInChildren<SkinnedMeshRenderer>());
                foreach (var target in targets.Distinct())
                {
                    if (ReferenceEquals(target, source))
                        continue;
                    var link = target.Slot.AttachComponent<BlendShapeLink>();
                    link.Source.Target = source;
                    link.Target.Target = target;
                    link.AllShapes.Value = (data["includeAll"]?.Int(1) ?? 1) != 0;
                    link.IgnoreCase.Value = (data["exactMatch"]?.Int() ?? 0) == 0;
                    if (data["excludes"] is UnitySeq excludes)
                        foreach (var item in excludes.Items)
                            if ((item["name"]?.Text ?? item.Text) is { Length: > 0 } name)
                                link.Excluded.Add(name);
                    if (data["includes"] is UnitySeq includes)
                        foreach (var item in includes.Items)
                        {
                            var from = item["nameOnBase"]?.Text ?? "";
                            if (from.Length == 0)
                                continue;
                            link.SourceShapes.Add(from);
                            link.TargetShapes.Add(item["nameOnLinked"]?.Text ?? "");
                        }
                    _shapeLinks++;
                }
            }
        }

        // The base object a link names: a path from the avatar's root, or failing that the one mesh by that name.
        private SkinnedMeshRenderer? MeshNamed(string name)
        {
            if (name.Length == 0)
                return null;
            if (_nodes.AtPath(name) is { } slot && slot.GetComponent<SkinnedMeshRenderer>() is { } atPath)
                return atPath;
            var leaf = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            return _model.GetComponentsInChildren<SkinnedMeshRenderer>()
                .FirstOrDefault(r => string.Equals(r.Slot.SlotName.Value, leaf, StringComparison.Ordinal));
        }

        // Modular Avatar's: the mesh it sits on follows each listed shape of the mesh each entry names.
        private void ShapeSync(UnityComponentRef part)
        {
            var owner = _nodes.Resolve(part.Node)?.GetComponent<SkinnedMeshRenderer>();
            if (owner == null || part.Doc["Bindings"] is not UnitySeq bindings)
            {
                Skip("shape syncs on meshes not in the package");
                return;
            }
            foreach (var group in bindings.Items.GroupBy(b => AvatarObject(b["ReferenceMesh"])))
            {
                var source = group.Key?.GetComponent<SkinnedMeshRenderer>();
                if (source == null || ReferenceEquals(source, owner))
                {
                    Skip("shape syncs from meshes not in the package");
                    continue;
                }
                var link = owner.Slot.AttachComponent<BlendShapeLink>();
                link.Source.Target = source;
                link.Target.Target = owner;
                link.AllShapes.Value = false;
                foreach (var binding in group)
                {
                    var shape = binding["Blendshape"]?.Text ?? "";
                    if (shape.Length == 0)
                        continue;
                    link.SourceShapes.Add(shape);
                    link.TargetShapes.Add(binding["LocalBlendshape"]?.Text ?? "");
                }
                _shapeLinks++;
            }
        }
    }
}
