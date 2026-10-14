// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components.Utility;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// The package's build-time tools, run where they would have run at upload. Before the constraints and the
// menu: deletions and upload-time actions, then everything Modular Avatar moves (armatures merged onto the
// body, props sent to bones, replacements, scale adjusters, world-fixed objects). After the switches: shape
// links, Modular Avatar's menus and reactive switches, objects kept in first person, and constraints VRCFury
// moves onto the avatar's own bones (read when the constraints are built). -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private UnityModelMeta? _meta;

        public void Arrange(UnityModelMeta meta, bool asAvatar, UnityVrcPlan? plan)
        {
            _meta = meta;
            var tools = UnityBuildTools.Of(_candidate);
            if (!tools.Any)
                return;
            UploadTime(tools, plan);
            if (asAvatar)
                ModularArrange(tools);
        }

        public void BuildTools(UnityVrcPlan? plan)
        {
            FinishOffStates();
            var tools = UnityBuildTools.Of(_candidate);
            Icons(plan, tools);
            if (!tools.Any)
                return;
            ShapeLinks(tools);
            ModularSwitches(tools);
            FirstPerson(tools);
            foreach (var part in tools.GlobalColliders)
                GlobalCollider(part.Doc, part.Node);
            if (_shapeLinks > 0)
                _result.Notes.Add($"{_shapeLinks} mesh{(_shapeLinks == 1 ? "" : "es")} follow the body's shapes.");
        }

        // A humanoid bone of the avatar, by the package's bone number.
        private Slot? HumanBone(int index) => _meta?.HumanBone(index) is { } name ? _nodes.ByName(name) : null;

        // VRCFury's constraint retarget: the constraint on this object holds the avatar's own bone instead.
        private Slot? RetargetBone(UnityNodeRef node)
        {
            foreach (var part in UnityBuildTools.Of(_candidate).Retargets)
                if (ReferenceEquals(part.Node, node) && part.Data?["bone"]?.Int(-1) is int bone and >= 0)
                    return HumanBone(bone);
            return null;
        }

        // Things on the head the wearer still sees. The head is collapsed for its wearer's own eyes, and
        // everything hung from it goes with it, so these move beside the head and follow it from there.
        private void FirstPerson(UnityBuildTools.Parts tools)
        {
            var nodes = tools.FirstPerson.Select(p => p.Node)
                .Concat(tools.ModularAvatar.Where(m => m.Kind == UnityBuildTools.Ma.VisibleHeadAccessory).Select(m => m.Part.Node));
            var head = HumanBone(10);
            int kept = 0;
            foreach (var node in nodes)
            {
                var slot = _nodes.Resolve(node);
                if (slot == null || head == null || head.Parent == null || !slot.IsDescendantOf(head) || ReferenceEquals(slot, head))
                    continue;
                var holder = head.Parent.AddSlot("Seen in First Person");
                var follow = holder.AttachComponent<TransformConstraint>();
                follow.Kind.Value = ConstraintKind.Parent;
                follow.AddSource(head);
                holder.GlobalPosition = head.GlobalPosition;
                holder.GlobalRotation = head.GlobalRotation;
                slot.SetParent(holder, preserveGlobalTransform: true);
                kept++;
            }
            if (kept > 0)
                _result.Notes.Add($"{kept} object{(kept == 1 ? "" : "s")} on the head stay visible to the wearer.");
        }
    }
}
