// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Audio;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// The avatar's animator played live: one ImportedAvatarAnimator running every playable layer the avatar
// carries, an input per parameter it reads, the menu's switches and sliders feeding those inputs, and a
// binding per animated property it can reach. A property is reached through the object its path names
// (for a merged controller, under the object it was merged at, else from the avatar's root) and the
// component its class names: transforms, objects and components switching, blendshapes, constraint weights,
// material values and material swaps, audio players. Parameters the animation itself writes and humanoid
// muscles need no binding; the body animation reads muscles from the animator. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private ImportedAvatarAnimator? _animator;
        private readonly Dictionary<string, ImportedAnimatorParameter> _animatorInputs = new(StringComparer.Ordinal);

        public bool LiveAnimator(UnityVrcPlan? plan, Uri? assetUri)
        {
            if (plan?.Program is not { } program)
                return false;
            if (assetUri == null)
            {
                _result.Notes.Add("The animator could not be stored, so its menu poses are converted one by one.");
                return false;
            }
            var holder = _model.AddSlot("Animator");
            var provider = holder.AttachComponent<ImportedAnimatorProvider>();
            provider.URL.Value = assetUri;
            var animator = holder.AttachComponent<ImportedAvatarAnimator>();
            _animator = animator;
            animator.Controller.Target = provider;
            animator.Root.Target = _model;
            animator.UnitScale.Value = _unit;
            animator.Gestures.Target = EnsureReader();
            animator.Voice.Target = _model.GetComponentInChildren<VisemeWeightDriver>();
            animator.InitializeForImport(program);

            Inputs(plan, program, holder, animator);
            foreach (var entry in plan.Switches)
            {
                if (!entry.Live && entry.On.IsEmpty && entry.Off.IsEmpty)
                    continue;
                var toggle = NewToggle(entry.Path, entry.DefaultOn, entry.Group);
                toggle.Momentary.Value = entry.Momentary;
                toggle.Default.Value = entry.DefaultOn;
                toggle.Saved.Value = entry.Saved;
                Icon(plan, toggle.Icon, entry.Icon);
                var input = Input(entry.Parameter, program, holder, animator);
                input.Value.Value = entry.OffValue;
                input.Switches.Add(toggle.State);
                input.SwitchValues.Add(entry.OnValue);
                if (entry.Group.Length == 0) _toggleByParameter.TryAdd(entry.Parameter, toggle);
            }

            int bound = Bindings(program, animator);
            Audio(program, animator);
            _result.AnimationControllers++;
            _result.Animations += program.Clips.Count;
            _result.Gestures += plan.Gestures.Count;
            var layers = program.Controller.Layers;
            var playables = string.Join(", ", layers.GroupBy(l => l.Playable).Select(g => $"{PlayableName(g.Key)} {g.Count()}"));
            _result.Notes.Add($"Animator played live: {layers.Count} layers ({playables}), {bound} animated properties bound.");
            int missing = program.MissingClipCount;
            if (missing > 0)
                _result.Notes.Add($"Animator: {missing} motions the package does not carry play as nothing.");
            return true;
        }

        private static string PlayableName(int playable) => playable switch
        {
            0 => "base", 2 => "additive", 3 => "gesture", 4 => "action", 5 => "FX", _ => "other",
        };

        // An input for every parameter the animator reads that is not a built-in. Inputs already made
        // under the avatar (by bone chains or contacts) are taken over by name.
        private void Inputs(UnityVrcPlan plan, UnityAnimatorProgram program, Slot holder, ImportedAvatarAnimator animator)
        {
            foreach (var existing in _model.GetComponentsInChildren<ImportedAnimatorParameter>())
                if (!string.IsNullOrEmpty(existing.Parameter.Value) && _animatorInputs.TryAdd(existing.Parameter.Value, existing))
                    animator.Parameters.Add(existing);
            var graph = program.Graph;
            for (int i = 0; i < graph.ParameterNames.Count; i++)
            {
                var name = graph.ParameterNames[i];
                if (name.Length == 0 || UnityVrcPlan.IsBuiltin(name) || graph.ParameterTypes[i] == UnityAnimatorController.ParameterType.Trigger)
                    continue;
                Input(name, program, holder, animator);
            }
            var menu = new HashSet<string>(plan.Switches.Select(s => s.Parameter).Concat(plan.Sliders.Select(s => s.Parameter)), StringComparer.Ordinal);
            foreach (var input in _animatorInputs.Values)
            {
                var info = plan.Parameters.GetValueOrDefault(input.Parameter.Value);
                // The parameter list says whether a parameter syncs. One it does not name stays on each machine,
                // unless a menu control sets it: a build tool lists those at upload, synced.
                input.Synced.Value = info?.Synced ?? menu.Contains(input.Parameter.Value);
                input.Saved.Value = info?.Saved ?? true;
                input.Default.Value = program.Defaults.GetValueOrDefault(input.Parameter.Value);
            }
        }

        private ImportedAnimatorParameter Input(string name, UnityAnimatorProgram program, Slot holder, ImportedAvatarAnimator animator)
        {
            if (_animatorInputs.TryGetValue(name, out var found))
                return found;
            var parameters = holder.FindChild("Parameters", recursive: false) ?? holder.AddSlot("Parameters");
            var input = parameters.AttachComponent<ImportedAnimatorParameter>();
            input.Parameter.Value = name;
            input.Value.Value = program.Defaults.GetValueOrDefault(name);
            animator.Parameters.Add(input);
            _animatorInputs[name] = input;
            return input;
        }

        // The object a clip path names: below the merge it came in under, else from the avatar's root.
        private Slot? AnimatedObject(string path, UnityAnimatorProgram program)
        {
            if (_nodes.AtPath(path) is { } slot)
                return slot;
            foreach (var prefix in program.PathPrefixes)
                if (path.StartsWith(prefix + "/", StringComparison.Ordinal) && _nodes.AtPath(path[(prefix.Length + 1)..]) is { } root)
                    return root;
            return null;
        }

        private int Bindings(UnityAnimatorProgram program, ImportedAvatarAnimator animator)
        {
            var graph = program.Graph;
            int bound = 0;
            var materialGroups = new Dictionary<(string Path, int ClassId, string Property), List<int>>();
            var swaps = new Dictionary<int, HashSet<int>>();
            foreach (var clip in graph.Clips)
                foreach (var track in clip.References)
                {
                    if (!swaps.TryGetValue(track.Binding, out var values))
                        swaps[track.Binding] = values = new HashSet<int>();
                    values.UnionWith(track.Values.Where(v => v >= 0));
                }
            var claimed = new HashSet<IWorldElement>();
            var rig = _model.GetComponent<HumanoidRig>();

            for (int b = 0; b < graph.Bindings.Count; b++)
            {
                var binding = graph.Bindings[b];
                var key = binding.Key;
                if (binding.Parameter >= 0 || binding.Muscle)
                    continue;
                if (key.Attribute.StartsWith("material.", StringComparison.Ordinal))
                {
                    var (property, _) = MaterialChannel(key.Attribute);
                    var group = (key.Path, key.ClassId, "material." + property);
                    if (!materialGroups.TryGetValue(group, out var list))
                        materialGroups[group] = list = new List<int>();
                    list.Add(b);
                    continue;
                }
                if (key.ClassId == 4 && key.Path.Length == 0)
                {
                    Skip("moves of the avatar's own placement");
                    continue;
                }
                var slot = AnimatedObject(key.Path, program);
                if (slot == null)
                {
                    Skip("animation targets not present in the imported model");
                    continue;
                }
                if (key.ClassId == 4)
                {
                    // The body solver poses humanoid bones over whatever an animation turns or moves them to.
                    if (key.Attribute != "scale" && rig != null && rig.GetBoneType(slot) != BodyNode.NONE)
                    {
                        Skip("turns of body bones the body pose owns");
                        continue;
                    }
                    switch (key.Attribute)
                    {
                        case "position": bound += Bind(key, ImportedAnimatorChannel.Position, slot.LocalPosition) ? 1 : 0; break;
                        case "rotation": bound += Bind(key, ImportedAnimatorChannel.Rotation, slot.LocalRotation) ? 1 : 0; break;
                        case "scale": bound += Bind(key, ImportedAnimatorChannel.Scale, slot.LocalScale) ? 1 : 0; break;
                    }
                    continue;
                }
                if (binding.Kind == UnityAnimatorGraph.ValueKind.Reference)
                {
                    if (SlotOf(slot, key.Attribute) is not { } target)
                    {
                        Skip("swaps of things other than materials");
                        continue;
                    }
                    if (target.IsDriven || !claimed.Add(target))
                    {
                        Skip("material swaps on a slot something else drives");
                        continue;
                    }
                    var entry = animator.Bindings.Add();
                    entry.Path.Value = key.Path; entry.ClassId.Value = key.ClassId; entry.Property.Value = key.Attribute; entry.Script.Value = key.Script ?? "";
                    entry.Channel.Value = ImportedAnimatorChannel.Material;
                    entry.Materials.Add().Target = target.Target;
                    entry.MaterialKeys.Add("");
                    foreach (int value in swaps.GetValueOrDefault(b) ?? new HashSet<int>())
                    {
                        var guid = graph.References[value].Guid;
                        if (guid == null || _materialFor?.Invoke(guid) is not { } material)
                            continue;
                        entry.Materials.Add().Target = material;
                        entry.MaterialKeys.Add(guid);
                    }
                    entry.MaterialSlot.DriveTarget(target);
                    bound++;
                    continue;
                }
                if (key.ClassId == 82 && key.Attribute != "m_Enabled")
                {
                    if (AudioField(slot, key.Attribute) is { } audio)
                        bound += Bind(key, ImportedAnimatorChannel.Number, audio) ? 1 : 0;
                    else
                        Skip("changes to " + key.Attribute);
                    continue;
                }
                switch (Classify(key))
                {
                    case Target.Active:
                        bound += Bind(key, ImportedAnimatorChannel.Boolean, slot.ActiveSelf) ? 1 : 0;
                        break;
                    case Target.Enabled:
                    {
                        bool any = false;
                        foreach (var component in EnabledTargets(slot, key).ToList())
                            any |= Bind(key, ImportedAnimatorChannel.Boolean, component.Enabled);
                        if (any) bound++;
                        else Skip(key.ClassId == 114 ? "switches of scripts that do not come across" : "switches of other components");
                        break;
                    }
                    case Target.ConstraintWeight:
                    case Target.ConstraintSource:
                        if (ConstraintField(slot, key, Classify(key)) is { } field)
                            bound += Bind(key, ImportedAnimatorChannel.Number, field) ? 1 : 0;
                        else
                            Skip("constraint changes on constraints that do not come across");
                        break;
                    case Target.BlendShape:
                        if (slot.GetComponent<SkinnedMeshRenderer>() is { } renderer)
                        {
                            var entry = Entry(key, ImportedAnimatorChannel.BlendShape);
                            entry.Renderer.Target = renderer;
                            bound++;
                        }
                        else
                            Skip("shapes on meshes not in the package");
                        break;
                    default:
                        Skip(key.ClassId == 114 ? "changes to script settings" : "changes to " + key.Attribute);
                        break;
                }
            }

            foreach (var ((path, classId, property), channels) in materialGroups)
            {
                var slot = AnimatedObject(path, program);
                if (slot == null)
                {
                    Skip("animation targets not present in the imported model");
                    continue;
                }
                bool any = false;
                foreach (var material in OwnMaterials(slot))
                    foreach (var (field, channel) in MaterialTargets(material, property["material.".Length..]))
                        any |= Bind(new UnityBinding(path, classId, property, null), channel, field);
                if (any) bound++;
                else Skip("material values with nothing to land on");
            }
            return bound;

            ImportedAnimatorBinding Entry(UnityBinding key, ImportedAnimatorChannel channel)
            {
                var entry = animator.Bindings.Add();
                entry.Path.Value = key.Path; entry.ClassId.Value = key.ClassId;
                entry.Property.Value = key.Attribute; entry.Script.Value = key.Script ?? "";
                entry.Channel.Value = channel;
                return entry;
            }

            bool Bind(UnityBinding key, ImportedAnimatorChannel channel, IField field)
            {
                if (field.IsDriven || !claimed.Add(field))
                {
                    Skip("animation channels already driven by another component");
                    return false;
                }
                var entry = Entry(key, channel);
                entry.Rest.Value = field.BoxedValue switch
                {
                    bool v => new float4(v ? 1f : 0f, 0, 0, 0),
                    float v => new float4(v, 0, 0, 0),
                    float3 v => new float4(v.x, v.y, v.z, 0),
                    floatQ v => new float4(v.x, v.y, v.z, v.w),
                    colorHDR v => new float4(v.r, v.g, v.b, v.a),
                    float4 v => v,
                    _ => float4.Zero,
                };
                entry.Target.DriveTarget(field);
                return true;
            }
        }

        // The material slot a swap curve's attribute names on this renderer: "m_Materials.Array.data[2]", its third.
        private static AssetRef<MaterialAsset>? SlotOf(Slot slot, string attribute)
        {
            const string prefix = "m_Materials.Array.data[";
            if (!attribute.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            int close = attribute.IndexOf(']');
            if (close < 0 || !int.TryParse(attribute.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                return null;
            var materials = MaterialSlotsOf(slot);
            return index >= 0 && index < materials.Count ? materials[index] : null;
        }

        // The fields a material property lands on in one material: a Poiyomi material keeps the name (or the
        // name a locked shader renamed it from), the others the handful of values they share.
        private IEnumerable<(IField Field, ImportedAnimatorChannel Channel)> MaterialTargets(MaterialProvider material, string property)
        {
            if (material is PoiyomiMaterial poiyomi)
            {
                if (poiyomi.Ensure(PoiyomiLockedNames.Original(material, property)) is { } param)
                    yield return (param.Value, ImportedAnimatorChannel.Vector);
                yield break;
            }
            var kind = MaterialProperty(property);
            if (kind == null)
                yield break;
            if (ColorField(material, kind.Value) is { } color)
                yield return (color, ImportedAnimatorChannel.Color);
            else if (FloatField(material, kind.Value) is { } number)
                yield return (number, ImportedAnimatorChannel.Number);
            else if (material is XiexeToonMaterial toon && kind.Value is MaterialValue.Hue)
                yield return (toon.HueShift, ImportedAnimatorChannel.Number);
            else if (material is XiexeToonMaterial toonSaturation && kind.Value is MaterialValue.Saturation)
                yield return (toonSaturation.Saturation, ImportedAnimatorChannel.Number);
        }

        // Every audio player a Play Audio state names, and every clip it may play, so each machine has them.
        private void Audio(UnityAnimatorProgram program, ImportedAvatarAnimator animator)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var behaviour in program.Graph.Behaviours)
            {
                if (behaviour is not UnityAnimatorGraph.PlayAudio audio)
                    continue;
                if (paths.Add(audio.Path))
                {
                    if (AudioCueAt(audio.Path) is { } cue)
                    {
                        animator.AudioPlayers.Add(cue);
                        animator.AudioPaths.Add(audio.Path);
                    }
                    else
                        Skip("audio a state plays on objects not in the package");
                }
                foreach (var key in audio.Source.Clips)
                {
                    var guid = UnityAnimatorController.Untag(key).Guid;
                    if (clips.Add(guid) && AudioClipFor(guid) is { } clip)
                    {
                        animator.AudioClips.Add().Target = clip;
                        animator.AudioClipKeys.Add(guid);
                    }
                }
            }
        }

        // The avatar's body follows the animator's humanoid muscles, and the menu's sliders get the details the
        // live animator keeps for them.
        public void BodyAnimators(UnityVrcPlan? plan)
        {
            if (plan == null || _animator is not { } animator || plan.Program is not { } program)
                return;
            foreach (var entry in plan.Sliders)
                if (_sliderByParameter.TryGetValue(entry.Parameter, out var slider) && !slider.IsDestroyed)
                {
                    slider.Saved.Value = entry.Saved;
                    Icon(plan, slider.Icon, entry.Icon);
                }
            if (program.Graph.MuscleBindings.Length == 0 || _model.GetComponent<HumanoidRig>() is not { } rig)
                return;
            var body = _model.GetComponent<ImportedBodyAnimation>() ?? _model.AttachComponent<ImportedBodyAnimation>();
            if (body.Rig.Target == null)
                body.Capture(rig);
            body.Sources.Add(animator);
        }

        private readonly Dictionary<string, ImageProvider> _icons = new(StringComparer.OrdinalIgnoreCase);

        // A menu control's icon: the package's picture, stored with the avatar's other assets.
        private void Icon(UnityVrcPlan plan, AssetRef<TextureAsset> icon, string? guid)
        {
            if (string.IsNullOrEmpty(guid) || !plan.IconFiles.TryGetValue(guid, out var source))
                return;
            if (!_icons.TryGetValue(guid, out var provider))
            {
                var holder = MenuSlot().FindChild("Icons", recursive: false) ?? MenuSlot().AddSlot("Icons");
                provider = holder.AttachComponent<ImageProvider>();
                provider.WrapModeU.Value = TextureWrapMode.Clamp;
                provider.WrapModeV.Value = TextureWrapMode.Clamp;
                _icons[guid] = provider;
                var db = _localDB;
                if (db == null)
                    provider.URL.Value = new Uri(source.File);
                else
                    holder.World?.StartTask(async () =>
                    {
                        await WorldContext.ToBackground();
                        string uri;
                        try
                        {
                            var bytes = await System.IO.File.ReadAllBytesAsync(source.File).ConfigureAwait(false);
                            uri = await db.SaveAssetAsync(bytes, source.Extension).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Logger.Warn($"UnityPackage: menu icon {guid} could not be stored: {ex.Message}");
                            return;
                        }
                        await WorldContext.ToWorld();
                        if (!provider.IsDestroyed && !string.IsNullOrEmpty(uri))
                            provider.URL.Value = new Uri(uri);
                    });
            }
            icon.Target = provider;
        }
    }
}
