// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components;

namespace Lumora.Core.Assets.Interop.Unity;

// What VRCFury does to the avatar once, as it is uploaded: objects marked for deletion go, and an "apply during
// upload" block's actions are carried out and left that way. Both are the state the creator meant the avatar
// to start in, so they are written into it before the menu reads its rest pose, and a switch or a merged layer
// that moves the same thing later still wins.
//
// Shapes are the one exception to writing straight in: a mesh's shapes are only known once it has loaded, so
// an upload-time shape is held by a driver just under the rest pose's. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private const int UploadShapePriority = SkinnedMeshRenderer.BlendShapePriorityRest - 1;
        private Slot? _upload;
        private UnityVrcPlan? _uploadPlan;

        private void UploadTime(UnityBuildTools.Parts tools, UnityVrcPlan? plan)
        {
            _uploadPlan = plan;
            foreach (var part in tools.Deletions)
            {
                var slot = _nodes.Resolve(part.Node);
                if (slot == null || ReferenceEquals(slot, _model) || _model.IsDescendantOf(slot))
                    continue;
                slot.Destroy();
                _result.Notes.Add($"Removed '{part.Node.Name}', which the package deletes on upload.");
            }
            int applied = 0;
            foreach (var part in tools.UploadActions)
            {
                if (part.Data == null)
                    continue;
                var references = UnityVrcFury.References(part.Doc);
                _actionRoot = part.Node;
                foreach (var action in UnityVrcFury.Actions(part.Data["action"], references))
                {
                    try
                    {
                        if (UploadAction(action))
                            applied++;
                    }
                    catch (Exception ex)
                    {
                        Skip("broken upload actions");
                        Logging.Logger.Warn($"UnityPackage: an upload action on '{part.Node.Name}' could not be applied: {ex.Message}");
                    }
                }
            }
            if (applied > 0)
                _result.Notes.Add($"Applied {applied} change{(applied == 1 ? "" : "s")} the package makes on upload.");
        }

        private bool UploadAction(UnityVrcFury.Entry action)
        {
            var data = action.Data;
            switch (action.Class)
            {
                case "ObjectToggleAction":
                {
                    if (Object(data["obj"]) is not { } target)
                        return Missing();
                    target.ActiveSelf.Value = (data["mode"]?.Int() ?? 0) switch { 0 => true, 1 => false, _ => !target.ActiveSelf.Value };
                    return true;
                }
                case "ScaleAction":
                {
                    if (Object(data["obj"]) is not { } target)
                        return Missing();
                    target.LocalScale.Value *= data["scale"]?.Float(1f) ?? 1f;
                    return true;
                }
                case "BlendShapeAction":
                {
                    var shape = data["blendShape"]?.Text;
                    if (string.IsNullOrEmpty(shape))
                        return false;
                    SkinnedMeshRenderer? renderer = null;
                    if ((data["allRenderers"]?.Int(1) ?? 1) == 0 && (renderer = Component(data["renderer"])?.GetComponent<SkinnedMeshRenderer>()) == null)
                        return Missing();
                    _upload ??= _model.AddSlot("Upload State");
                    var driver = _upload.AttachComponent<BlendShapeSwitchDriver>();
                    driver.On.Value = true;
                    driver.Renderer.Target = renderer!;
                    driver.Scope.Target = _model;
                    driver.BlendShape.Value = shape;
                    driver.Weight.Value = System.Math.Clamp((data["blendShapeValue"]?.Float(100f) ?? 100f) / 100f, 0f, 1f);
                    driver.Priority.Value = UploadShapePriority;
                    // Part of the rest a switch measures its own shape weights against: one that puts the shape
                    // back to nought is a change from this, not from the mesh's own weight.
                    var meshes = renderer != null ? new[] { renderer } : _model.GetComponentsInChildren<SkinnedMeshRenderer>().ToArray();
                    foreach (var mesh in meshes)
                        if (PathTo(mesh.Slot) is { } path)
                        {
                            var binding = new UnityBinding(path, 137, "blendShape." + shape, null);
                            _restPose.Floats[binding] = driver.Weight.Value * 100f;
                            _uploadPlan?.Rest.Floats.TryAdd(binding, driver.Weight.Value * 100f);
                        }
                    return true;
                }
                case "MaterialAction":
                {
                    var holder = Component(data["renderer"]) ?? Object(data["obj"]);
                    var materials = holder != null ? MaterialSlotsOf(holder) : new List<AssetRef<MaterialAsset>>();
                    int index = data["materialIndex"]?.Int() ?? 0;
                    var material = UnityVrcFury.Guid(data["mat"]) is { } guid ? _materialFor?.Invoke(guid) : null;
                    if (material == null || index < 0 || index >= materials.Count)
                        return Missing();
                    materials[index].Target = material;
                    return true;
                }
                case "MaterialPropertyAction":
                {
                    var property = data["propertyName"]?.Text;
                    if (string.IsNullOrEmpty(property))
                        return false;
                    var properties = new Dictionary<string, Dictionary<char, float>>(StringComparer.Ordinal) { [property] = PropertyChannels(data) };
                    var slots = new List<Slot>();
                    if ((data["affectAllMeshes"]?.Int() ?? 0) != 0)
                    {
                        slots.AddRange(_model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.Slot));
                        slots.AddRange(_model.GetComponentsInChildren<MeshRenderer>().Select(r => r.Slot));
                    }
                    else if ((Object(data["renderer2"]) ?? Component(data["renderer"])) is { } one)
                        slots.Add(one);
                    bool any = false;
                    foreach (var slot in slots.Distinct())
                        foreach (var material in OwnMaterials(slot))
                            foreach (var (field, value) in MaterialFieldValues(material, properties))
                            {
                                field.BoxedValue = value;
                                any = true;
                            }
                    return any;
                }
                case "AnimationClipAction":
                {
                    var clip = ActionClip(data["clip"]);
                    if (clip == null)
                        return Missing();
                    var pose = new UnityPose();
                    pose.Add(clip, 0f, play: false);
                    ApplyRest(pose);
                    return true;
                }
                default:
                    Skip("upload actions of other kinds");
                    return false;
            }
        }

        private bool Missing()
        {
            Skip("upload actions on objects not in the package");
            return false;
        }

        private static Dictionary<char, float> PropertyChannels(UnityNode data)
        {
            var channels = new Dictionary<char, float>();
            switch (data["propertyType"]?.Int() ?? 0)
            {
                case 1:
                {
                    var c = data["valueColor"];
                    channels['r'] = c?["r"]?.Float() ?? 0f;
                    channels['g'] = c?["g"]?.Float() ?? 0f;
                    channels['b'] = c?["b"]?.Float() ?? 0f;
                    channels['a'] = c?["a"]?.Float(1f) ?? 1f;
                    break;
                }
                case 2:
                case 3:
                {
                    var v = data["valueVector"];
                    channels['x'] = v?["x"]?.Float() ?? 0f;
                    channels['y'] = v?["y"]?.Float() ?? 0f;
                    channels['z'] = v?["z"]?.Float() ?? 0f;
                    channels['w'] = v?["w"]?.Float() ?? 0f;
                    break;
                }
                default:
                    channels['\0'] = data["value"]?.Float() ?? 0f;
                    break;
            }
            return channels;
        }
    }
}
