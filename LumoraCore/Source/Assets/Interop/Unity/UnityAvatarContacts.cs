// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Touch;
using Lumora.Core.Components.Utility;
using Lumora.Core.Components.Variables;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// The package's contacts as ours: every receiver a ContactReceiver, every sender a ContactSender, and the
// descriptor's body colliders (head, torso, hands, feet, fingers) as senders marked as the body, sized the way the
// author set them. Receivers feel senders by tag on every peer, the wearer's own body included when they allow
// themselves, so a foot sender planted in a toe receiver, a corrective sender sliding through its own receiver and
// another avatar's fingertip in a boop zone all come across.
//
// What a receiver sets reaches the animation two ways. A controller played live reads it through its parameter
// input: Value (on, the one frame of arrival, or depth). Converted poses switch on while it is touched, depth
// scaling the shapes they move. PhysBone chains report their grab and angle the same way. A receiver nothing
// answers is removed once every layer is known. -xlinka
public static partial class UnityAvatarBuilder
{
    // Parameters whose reactions the menu plan should read: what receivers set, and what grabbing a PhysBone sets.
    public static List<string> ContactParameters(UnityImportCandidate candidate)
    {
        var list = candidate.Contacts.Select(c => c.Feature).ToList();
        foreach (var part in candidate.Chains)
        {
            if (part.Type != UnityComponentRef.Kind.PhysBone || part.Doc["parameter"]?.Text is not { Length: > 0 } name)
                continue;
            list.Add(name + "_IsGrabbed");
            list.Add(name + "_IsPosed");
        }
        return list;
    }

    private static readonly (string Field, string[] Tags, FingerType? Finger, Chirality Side)[] BodyColliders =
    {
        ("collider_head", ContactTags.Head, null, Chirality.None),
        ("collider_torso", ContactTags.Torso, null, Chirality.None),
        ("collider_handL", ContactTags.HandLeft, null, Chirality.Left),
        ("collider_handR", ContactTags.HandRight, null, Chirality.Right),
        ("collider_footL", ContactTags.FootLeft, null, Chirality.Left),
        ("collider_footR", ContactTags.FootRight, null, Chirality.Right),
        ("collider_fingerIndexL", ContactTags.FingersLeft[0], FingerType.Index, Chirality.Left),
        ("collider_fingerMiddleL", ContactTags.FingersLeft[1], FingerType.Middle, Chirality.Left),
        ("collider_fingerRingL", ContactTags.FingersLeft[2], FingerType.Ring, Chirality.Left),
        ("collider_fingerLittleL", ContactTags.FingersLeft[3], FingerType.Pinky, Chirality.Left),
        ("collider_fingerIndexR", ContactTags.FingersRight[0], FingerType.Index, Chirality.Right),
        ("collider_fingerMiddleR", ContactTags.FingersRight[1], FingerType.Middle, Chirality.Right),
        ("collider_fingerRingR", ContactTags.FingersRight[2], FingerType.Ring, Chirality.Right),
        ("collider_fingerLittleR", ContactTags.FingersRight[3], FingerType.Pinky, Chirality.Right),
    };

    private sealed partial class FeatureBuilder
    {
        private readonly Dictionary<string, List<ContactReceiver>> _receivers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
        private Dictionary<string, DynamicBoneChain>? _chainOutputs;
        private Slot? _contacts;

        public void ContactZones()
        {
            foreach (var part in _candidate.Contacts)
            {
                try
                {
                    if (Receiver(part) is { } receiver)
                    {
                        if (!_receivers.TryGetValue(part.Feature, out var list))
                            _receivers[part.Feature] = list = new List<ContactReceiver>();
                        list.Add(receiver);
                    }
                }
                catch (Exception ex)
                {
                    Skip("broken contacts");
                    Logging.Logger.Warn($"UnityPackage: a contact receiver on '{part.Node.Name}' could not be built: {ex.Message}");
                }
            }
            // Senders read as collider-shaped scripts; they are the ones with tags.
            foreach (var part in _candidate.Colliders.Values)
            {
                if (part.Doc["collisionTags"] is not UnitySeq || part.Doc["receiverType"] != null)
                    continue;
                try
                {
                    Sender(part);
                }
                catch (Exception ex)
                {
                    Skip("broken contacts");
                    Logging.Logger.Warn($"UnityPackage: a contact sender on '{part.Node.Name}' could not be built: {ex.Message}");
                }
            }
            BodySenders();
        }

        private ContactReceiver? Receiver(UnityComponentRef part)
        {
            var d = part.Doc;
            var owner = _nodes.Resolve(part.Node);
            if (owner == null)
            {
                Skip("contacts on objects this avatar does not have");
                return null;
            }
            var receiver = owner.AttachComponent<ContactReceiver>();
            Volume(receiver, d, owner);
            receiver.Mode.Value = (d["receiverType"]?.Int() ?? 0) switch
            {
                1 => ContactReceiverMode.Enter,
                2 => ContactReceiverMode.Depth,
                _ => ContactReceiverMode.Constant,
            };
            receiver.AllowSelf.Value = (d["allowSelf"]?.Int(1) ?? 1) != 0;
            receiver.AllowOthers.Value = (d["allowOthers"]?.Int(1) ?? 1) != 0;
            receiver.MinVelocity.Value = MathF.Max(d["minVelocity"]?.Float(0.05f) ?? 0.05f, 0f);
            // Content kinds: bit 0 avatars, bit 1 everything else; all of them when unset.
            int content = d["contentTypes"]?.Int(-1) ?? -1;
            receiver.FromAvatars.Value = content == -1 || (content & 1) != 0;
            receiver.FromWorld.Value = content == -1 || (content & 2) != 0;
            receiver.Enabled.Value = part.Enabled;
            Script(_result, owner, receiver);
            return receiver;
        }

        private void Sender(UnityComponentRef part)
        {
            var owner = _nodes.Resolve(part.Node);
            if (owner == null)
            {
                Skip("contacts on objects this avatar does not have");
                return;
            }
            var sender = owner.AttachComponent<ContactSender>();
            Volume(sender, part.Doc, owner);
            sender.Enabled.Value = part.Enabled;
            Script(_result, owner, sender);
        }

        private void Volume(ContactVolume volume, UnityDocument d, Slot owner)
        {
            long rootId = d["rootTransform"]?.Ref.FileId ?? 0;
            if (rootId != 0 && _candidate.ResolveTransform(rootId) is { } root && _nodes.Resolve(root) is { } anchor && !ReferenceEquals(anchor, owner))
                volume.Anchor.Target = anchor;
            volume.Radius.Value = MathF.Max(d["radius"]?.Float(0.1f) ?? 0.1f, 0.0005f) / _unit;
            volume.Height.Value = (d["shapeType"]?.Int() ?? 0) == 1 ? MathF.Max(d["height"]?.Float() ?? 0f, 0f) / _unit : 0f;
            volume.Offset.Value = Mirror(Vector(d["position"])) / _unit;
            if (d["rotation"] is { } r)
                volume.Rotation.Value = MirrorRotation(new floatQ(r["x"]?.Float() ?? 0f, r["y"]?.Float() ?? 0f, r["z"]?.Float() ?? 0f, r["w"]?.Float(1f) ?? 1f));
            volume.LocalOnly.Value = (d["localOnly"]?.Int() ?? 0) != 0;
            if (d["collisionTags"] is UnitySeq tags)
                foreach (var tag in tags.Items)
                    if (!string.IsNullOrEmpty(tag.Text))
                        volume.Tags.Add(tag.Text);
        }

        // The descriptor's own body colliders, where the author set or the tools worked them out: each on its bone,
        // the fingers left to the skeleton when they were worked out, from the first joint to the tip.
        private void BodySenders()
        {
            var descriptor = _candidate.Descriptor;
            if (descriptor == null)
                return;
            var rig = _model.GetComponent<HumanoidRig>() ?? _model.GetComponentInChildren<HumanoidRig>();
            foreach (var (field, tags, finger, side) in BodyColliders)
            {
                try
                {
                    BodySender(descriptor[field], tags, finger, side, rig);
                }
                catch (Exception ex)
                {
                    Logging.Logger.Warn($"UnityPackage: the body contact '{field}' could not be built: {ex.Message}");
                }
            }
        }

        private void BodySender(UnityNode? config, string[] tags, FingerType? finger, Chirality side, HumanoidRig? rig)
        {
            if (config == null)
                return;
            int state = config["state"]?.Int() ?? 0;
            if (state == 2)
                return;
            float radius = MathF.Max(config["radius"]?.Float() ?? 0f, 0f) / _unit;
            if (radius <= 0f)
                return;

            ContactSender? sender = null;
            if (finger is { } kind && state == 0 && rig != null)
            {
                var start = rig.TryGetBone(kind.ComposeFinger(FingerSegmentType.Proximal, side));
                var last = rig.TryGetBone(kind.ComposeFinger(FingerSegmentType.Distal, side));
                if (start != null && last != null)
                {
                    var tip = rig.TryGetBone(kind.ComposeFinger(FingerSegmentType.Tip, side));
                    var middle = rig.TryGetBone(kind.ComposeFinger(FingerSegmentType.Intermediate, side)) ?? start;
                    float3 end = tip != null ? tip.GlobalPosition : last.GlobalPosition + (last.GlobalPosition - middle.GlobalPosition) * 0.8f;
                    float3 along = start.GlobalPointToLocal(end);
                    float length = along.Length;
                    if (length > 1e-6f)
                    {
                        sender = start.AttachComponent<ContactSender>();
                        sender.Offset.Value = along * 0.5f;
                        sender.Rotation.Value = Lumora.Core.Components.Avatar.IK.FabrikSolver.FromToRotation(float3.Up, along / length);
                        sender.Height.Value = length + radius * 2f;
                    }
                }
            }
            if (sender == null)
            {
                long id = config["transform"]?.Ref.FileId ?? 0;
                var bone = id != 0 && _candidate.ResolveTransform(id) is { } node ? _nodes.Resolve(node) : null;
                if (bone == null)
                    return;
                sender = bone.AttachComponent<ContactSender>();
                sender.Offset.Value = Mirror(Vector(config["position"])) / _unit;
                if (config["rotation"] is { } r)
                    sender.Rotation.Value = MirrorRotation(new floatQ(r["x"]?.Float() ?? 0f, r["y"]?.Float() ?? 0f, r["z"]?.Float() ?? 0f, r["w"]?.Float(1f) ?? 1f));
                sender.Height.Value = MathF.Max(config["height"]?.Float() ?? 0f, 0f) / _unit;
            }
            sender.Radius.Value = radius;
            sender.Body.Value = true;
            foreach (var tag in tags)
                sender.Tags.Add(tag);
        }

        // A VRCFury global collider: something on the avatar that other people's bones and contacts feel as a finger.
        public void GlobalCollider(UnityDocument doc, UnityNodeRef node)
        {
            var owner = _nodes.Resolve(node);
            if (owner == null)
            {
                Skip("contacts on objects this avatar does not have");
                return;
            }
            var sender = owner.AttachComponent<ContactSender>();
            long rootId = doc["rootTransform"]?.Ref.FileId ?? 0;
            if (rootId != 0 && _candidate.ResolveTransform(rootId) is { } root && _nodes.Resolve(root) is { } anchor && !ReferenceEquals(anchor, owner))
                sender.Anchor.Target = anchor;
            sender.Radius.Value = MathF.Max(doc["radius"]?.Float(0.05f) ?? 0.05f, 0.0005f) / _unit;
            sender.Height.Value = MathF.Max(doc["height"]?.Float() ?? 0f, 0f) / _unit;
            sender.Tags.Add("Finger");
            sender.PushesBones.Value = true;
            sender.Enabled.Value = (doc["m_Enabled"]?.Int(1) ?? 1) != 0;
            Script(_result, owner, sender);
        }

        // The reactions converted poses show: on while touched (or while held, for a chain's grab), depth scaling the
        // shapes they move.
        public void Contacts(UnityVrcPlan? plan)
        {
            if (plan == null)
                return;
            var outputs = ChainOutputs();
            foreach (var (parameter, poses) in plan.Contacts)
            {
                var switches = new List<IField<bool>>();
                IField<float>? amount = null;
                List<ContactReceiver>? receivers = null;
                if (_receivers.TryGetValue(parameter, out receivers) && receivers.Count > 0)
                {
                    foreach (var receiver in receivers)
                        switches.Add(receiver.Touching);
                    if (receivers.Any(r => r.Mode.Value == ContactReceiverMode.Depth))
                        amount = Lead(receivers).Value;
                }
                else if (parameter.EndsWith("_IsGrabbed", StringComparison.Ordinal) && outputs.TryGetValue(parameter[..^"_IsGrabbed".Length], out var chain))
                {
                    switches.Add(chain.OutputGrabbed);
                    chain.Outputs.Value = true;
                }
                else
                {
                    continue;
                }

                // A switch the reaction is locked behind turns the receivers themselves on and off.
                bool locked = false;
                foreach (var gate in poses.Gates)
                {
                    if (!_toggleByParameter.TryGetValue(gate, out var toggle))
                    {
                        locked = true;
                        break;
                    }
                    if (receivers != null)
                        foreach (var receiver in receivers)
                            Switch(receiver.Enabled, false, true, toggle.State, toggle.Slot);
                }
                if (locked)
                {
                    Skip("contacts locked behind something other than a menu switch");
                    continue;
                }
                _contacts ??= _model.AddSlot("Contacts");
                var holder = _contacts.AddSlot(parameter);
                var variable = holder.AttachComponent<ValueVariable<bool>>();
                variable.VariableName.Value = parameter;
                var any = holder.AttachComponent<ValueSwitchDriver<bool>>();
                any.OffValue.Value = false;
                any.OnValue.Value = true;
                foreach (var field in switches)
                    any.AddSwitch(field, true);
                any.AddDrive(variable.Value);

                int before = _carried;
                ApplyPose(poses.On, poses.Off, variable.Value, amount, SkinnedMeshRenderer.BlendShapePriorityGesture, 0.1f, holder);
                if (_carried == before)
                {
                    Skip("contacts whose reaction does not come across");
                    holder.Destroy();
                    continue;
                }
                _answered.Add(parameter);
            }
        }

        // Once every layer is known: controller inputs read the receivers and chains that set them, and receivers
        // nothing answers go.
        public void ContactInputs()
        {
            var inputs = new Dictionary<string, List<ImportedAnimatorParameter>>(StringComparer.Ordinal);
            foreach (var input in _model.GetComponentsInChildren<ImportedAnimatorParameter>())
            {
                var name = input.Parameter.Value ?? "";
                if (!inputs.TryGetValue(name, out var list))
                    inputs[name] = list = new List<ImportedAnimatorParameter>();
                list.Add(input);
            }

            foreach (var (parameter, receivers) in _receivers)
            {
                receivers.RemoveAll(r => r == null || r.IsDestroyed);
                if (receivers.Count == 0)
                    continue;
                var lead = Lead(receivers);
                foreach (var receiver in receivers)
                    if (!ReferenceEquals(receiver, lead))
                        receiver.ReportTo.Target = lead;
                bool used = _answered.Contains(parameter);
                if (inputs.TryGetValue(parameter, out var list))
                {
                    foreach (var input in list)
                        if (input.Source.Target == null && input.Contacts.Count == 0 && input.Switches.Count == 0)
                            input.Source.Target = lead.Value;
                    used = true;
                }
                if (used)
                {
                    _result.Contacts++;
                    continue;
                }
                Skip("contacts no animation layer answers");
                foreach (var receiver in receivers)
                {
                    if (_result.Scripts.TryGetValue(receiver.Slot, out var built))
                        built.Remove(receiver);
                    receiver.Destroy();
                }
            }

            foreach (var (name, chain) in ChainOutputs())
            {
                bool wired = false;
                if (inputs.TryGetValue(name + "_IsGrabbed", out var grabbed))
                    foreach (var input in grabbed)
                    {
                        if (input.Switches.Contains(chain.OutputGrabbed))
                            continue;
                        input.Switches.Add(chain.OutputGrabbed);
                        input.SwitchValues.Add(1f);
                        wired = true;
                    }
                wired |= Feed(inputs, name + "_Angle", chain.OutputAngle);
                wired |= Feed(inputs, name + "_Stretch", chain.OutputStretch);
                wired |= Feed(inputs, name + "_Squish", chain.OutputSquish);
                if (wired || _answered.Contains(name + "_IsGrabbed"))
                    chain.Outputs.Value = true;
            }
        }

        private static bool Feed(Dictionary<string, List<ImportedAnimatorParameter>> inputs, string name, IField<float> value)
        {
            if (!inputs.TryGetValue(name, out var list))
                return false;
            bool fed = false;
            foreach (var input in list)
            {
                if (input.Source.Target != null)
                    continue;
                input.Source.Target = value;
                fed = true;
            }
            return fed;
        }

        private static ContactReceiver Lead(List<ContactReceiver> receivers) => receivers[0];

        // Each PhysBone chain that names a parameter, by that name.
        private Dictionary<string, DynamicBoneChain> ChainOutputs()
        {
            if (_chainOutputs != null)
                return _chainOutputs;
            _chainOutputs = new Dictionary<string, DynamicBoneChain>(StringComparer.Ordinal);
            foreach (var part in _candidate.Chains)
            {
                if (part.Type != UnityComponentRef.Kind.PhysBone || part.Doc["parameter"]?.Text is not { Length: > 0 } name)
                    continue;
                var root = ChainRoot(_candidate, part, _nodes);
                if (root == null)
                    continue;
                var owner = _nodes.Resolve(part.Node) ?? root;
                foreach (var chain in owner.GetComponents<DynamicBoneChain>())
                {
                    if (ReferenceEquals(chain.Root.Target, root))
                    {
                        _chainOutputs.TryAdd(name, chain);
                        break;
                    }
                }
            }
            return _chainOutputs;
        }
    }
}
