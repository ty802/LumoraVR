// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// A Unity animator controller as saved: parameters, layers, and per layer a state machine of states
// joined by conditional transitions, each state playing a clip or a blend tree, with the avatar SDK's
// state behaviours (parameter drivers, tracking and layer control, audio) hung on states.
//
// The live player (UnityAnimatorRuntime) runs all of it frame by frame. Settle is the other reading: with
// these parameter values, which state does a layer come to rest in? That is enough to read an avatar's
// menu as poses when no live player can be kept, because a menu switch only ever sets a parameter.
//
// Controllers merged in from elsewhere (other playable layers, controllers a build tool would merge at
// upload) are appended after the first one: their ids are moved out of the way of the ones already here,
// their parameters renamed as the merge says, and their clip references tagged with the merge they came
// from, so one clip can sit under two different objects. -xlinka
public sealed class UnityAnimatorController
{
    public enum ParameterType { Float = 1, Int = 3, Bool = 4, Trigger = 9 }

    public sealed record Parameter(string Name, ParameterType Type, float Default);

    public sealed record Condition(int Mode, string Parameter, float Threshold);

    public sealed class Transition
    {
        public long Id;
        public long DstState;
        public long DstMachine;
        public bool IsExit;
        public bool HasExitTime;
        public float ExitTime;
        public float Duration;
        public bool FixedDuration;
        public float Offset;
        public bool CanTransitionToSelf;
        public bool Muted;
        public bool Solo;
        // 0 none, 1 the source state's transitions, 2 the destination's, 3 source then destination,
        // 4 destination then source.
        public int InterruptionSource;
        public bool OrderedInterruption = true;
        public readonly List<Condition> Conditions = new();
    }

    public sealed class State
    {
        public long Id;
        public string Name = "";
        public UnityRef Motion;
        public bool WriteDefaults;
        public string? TimeParameter;
        public float Speed = 1f;
        public string? SpeedParameter;
        public float CycleOffset;
        public bool Mirror;
        public string? MirrorParameter, CycleOffsetParameter;
        public readonly List<long> Transitions = new();
        public readonly List<long> Behaviours = new();
        // A tracking control on the state hands the eyes to the animation (an expression that sets the
        // lids itself), so blinking has to wait.
        public bool EyesAnimated;
    }

    public sealed class StateMachine
    {
        public long Id;
        public string Name = "";
        public long DefaultState;
        public readonly List<long> States = new();
        public readonly List<long> Machines = new();
        public readonly List<long> AnyTransitions = new();
        public readonly List<long> EntryTransitions = new();
        // Transitions out of a child machine's node, taken when that machine is left through its exit.
        public readonly Dictionary<long, List<long>> MachineTransitions = new();
        public readonly List<long> Behaviours = new();
    }

    public sealed class BlendTree
    {
        public long Id;
        public int Type;
        public string Parameter = "";
        public string ParameterY = "";
        public bool Normalized;
        public readonly List<(float X, float Y)> Positions = new();
        public readonly List<(float Speed, float Offset, bool Mirror)> Playback = new();
        public readonly List<(UnityRef Motion, float Threshold, string DirectParameter)> Children = new();
    }

    public sealed class Layer
    {
        public string Name = "";
        public int Index;
        public long Machine;
        public float Weight;
        public bool Additive;
        public UnityRef Mask;
        public int SyncedLayer = -1;
        // A synced layer runs another layer's states with motions of its own.
        public readonly Dictionary<long, UnityRef> SyncedMotions = new();
        // The playable layer this came from (0 base, 2 additive, 3 gesture, 4 action, 5 FX) and the layer's
        // index inside it, which is how layer control names it.
        public int Playable = 5;
        public int PlayableIndex;
        // The merge the layer came in with, "" for one read where it stands.
        public string Tag = "";
    }

    public abstract class Behaviour
    {
        public long Id;
        // The playable layer and merge of the controller it sits in.
        public int SourcePlayable = 5;
        public string Tag = "";
    }

    public enum DriverOperation { Set = 0, Add = 1, Random = 2, Copy = 3 }

    public sealed record DriverEntry(DriverOperation Type, string Name, string Source, float Value, float Min, float Max, float Chance,
        bool ConvertRange, float SourceMin, float SourceMax, float DestMin, float DestMax);

    public sealed class ParameterDriver : Behaviour
    {
        public bool LocalOnly;
        public readonly List<DriverEntry> Entries = new();
    }

    // Per part, 0 leaves it, 1 hands it to tracking, 2 to the animation. Parts in order: head, left hand,
    // right hand, hip, left foot, right foot, left fingers, right fingers, eyes and lids, mouth and jaw.
    public sealed class TrackingControl : Behaviour
    {
        public readonly int[] Modes = new int[10];
    }

    // Blends one layer's weight to a goal over a time. Playable as the descriptor numbers them.
    public sealed class LayerControl : Behaviour
    {
        public int Playable;
        public int Layer;
        public float Goal;
        public float Duration;
    }

    // Blends a whole playable layer's weight to a goal over a time.
    public sealed class PlayableControl : Behaviour
    {
        public int Playable;
        public float Goal;
        public float Duration;
    }

    public sealed class PlayAudio : Behaviour
    {
        public string SourcePath = "";
        // 0 random, 1 random without repeating, 2 in order, 3 picked by a parameter.
        public int Order;
        public string Parameter = "";
        public float2 Volume = new(1f, 1f);
        public float2 Pitch = new(1f, 1f);
        // 0 always, 1 only when the source is stopped, 2 never.
        public int VolumeApply, PitchApply, ClipsApply, LoopApply;
        public bool Loop;
        public float Delay;
        public bool PlayOnEnter, StopOnEnter, PlayOnExit, StopOnExit;
        public readonly List<string> Clips = new();
    }

    public readonly List<Parameter> Parameters = new();
    public readonly List<Layer> Layers = new();
    public readonly Dictionary<long, State> States = new();
    public readonly Dictionary<long, StateMachine> Machines = new();
    public readonly Dictionary<long, Transition> Transitions = new();
    public readonly Dictionary<long, BlendTree> BlendTrees = new();
    public readonly Dictionary<long, Behaviour> Behaviours = new();
    // Kinds of behaviour seen that nothing here runs, by name.
    public readonly SortedDictionary<string, int> OtherBehaviours = new(StringComparer.Ordinal);

    // The descriptor's playable layers start at full weight except the action layer, which emotes blend in.
    public static float PlayableDefaultWeight(int playable) => playable == 4 ? 0f : 1f;

    public static UnityAnimatorController? Parse(string? text, int playable = 5)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var docs = UnityYaml.Parse(text);
        var root = docs.FirstOrDefault(d => d.ClassId == 91);
        if (root == null)
            return null;
        var controller = new UnityAnimatorController();

        if (root["m_AnimatorParameters"] is UnitySeq parameters)
        {
            foreach (var p in parameters.Items)
            {
                var type = (ParameterType)(p["m_Type"]?.Int() ?? 1);
                float value = type switch
                {
                    ParameterType.Float => p["m_DefaultFloat"]?.Float() ?? 0f,
                    ParameterType.Int => p["m_DefaultInt"]?.Int() ?? 0,
                    _ => p["m_DefaultBool"]?.Int() ?? 0,
                };
                var name = p["m_Name"]?.Text ?? "";
                if (controller.FindParameter(name) == null)
                    controller.Parameters.Add(new Parameter(name, type, value));
            }
        }
        if (root["m_AnimatorLayers"] is UnitySeq layers)
        {
            int index = 0;
            foreach (var l in layers.Items)
            {
                var layer = new Layer
                {
                    Name = l["m_Name"]?.Text ?? "",
                    Index = index,
                    PlayableIndex = index,
                    Playable = playable,
                    Machine = l["m_StateMachine"]?.Ref.FileId ?? 0,
                    // A controller's first layer always plays at full weight, whatever it says.
                    Weight = index == 0 ? 1f : l["m_DefaultWeight"]?.Float() ?? 0f,
                    // The additive playable layer is added onto the base as a whole.
                    Additive = (l["m_BlendingMode"]?.Int() ?? 0) == 1 || playable == 2,
                    Mask = l["m_Mask"]?.Ref ?? l["m_AvatarMask"]?.Ref ?? default,
                    SyncedLayer = l["m_SyncedLayerIndex"]?.Int(-1) ?? -1,
                };
                if (layer.SyncedLayer >= 0 && l["m_Motions"] is UnitySeq motions)
                    foreach (var m in motions.Items)
                        if (m["m_State"]?.Ref.FileId is long state and not 0)
                            layer.SyncedMotions[state] = m["m_Motion"]?.Ref ?? default;
                controller.Layers.Add(layer);
                index++;
            }
        }

        foreach (var d in docs)
        {
            switch (d.ClassId)
            {
                case 1107:
                {
                    var machine = new StateMachine { Id = d.FileId, Name = d["m_Name"]?.Text ?? "", DefaultState = d["m_DefaultState"]?.Ref.FileId ?? 0 };
                    if (d["m_ChildStates"] is UnitySeq states)
                        foreach (var s in states.Items)
                            machine.States.Add(s["m_State"]?.Ref.FileId ?? 0);
                    if (d["m_ChildStateMachines"] is UnitySeq machines)
                        foreach (var m in machines.Items)
                            machine.Machines.Add(m["m_StateMachine"]?.Ref.FileId ?? 0);
                    Ids(d["m_AnyStateTransitions"], machine.AnyTransitions);
                    Ids(d["m_EntryTransitions"], machine.EntryTransitions);
                    Ids(d["m_StateMachineBehaviours"], machine.Behaviours);
                    if (d["m_StateMachineTransitions"] is UnitySeq exits)
                        foreach (var e in exits.Items)
                        {
                            long from = e["first"]?.Ref.FileId ?? 0;
                            var list = new List<long>();
                            Ids(e["second"], list);
                            if (from != 0 && list.Count > 0)
                                machine.MachineTransitions[from] = list;
                        }
                    controller.Machines[d.FileId] = machine;
                    break;
                }
                case 1102:
                {
                    var state = new State
                    {
                        Id = d.FileId,
                        Name = d["m_Name"]?.Text ?? "",
                        Motion = d["m_Motion"]?.Ref ?? default,
                        WriteDefaults = (d["m_WriteDefaultValues"]?.Int(1) ?? 1) != 0,
                        TimeParameter = (d["m_TimeParameterActive"]?.Int() ?? 0) != 0 ? d["m_TimeParameter"]?.Text : null,
                        Speed = d["m_Speed"]?.Float(1f) ?? 1f,
                        SpeedParameter = (d["m_SpeedParameterActive"]?.Int() ?? 0) != 0 ? d["m_SpeedParameter"]?.Text : null,
                        CycleOffset = d["m_CycleOffset"]?.Float() ?? 0f,
                        Mirror = (d["m_Mirror"]?.Int() ?? 0) != 0,
                        MirrorParameter = (d["m_MirrorParameterActive"]?.Int() ?? 0) != 0 ? d["m_MirrorParameter"]?.Text : null,
                        CycleOffsetParameter = (d["m_CycleOffsetParameterActive"]?.Int() ?? 0) != 0 ? d["m_CycleOffsetParameter"]?.Text : null,
                    };
                    Ids(d["m_Transitions"], state.Transitions);
                    Ids(d["m_StateMachineBehaviours"], state.Behaviours);
                    controller.States[d.FileId] = state;
                    break;
                }
                case 1101:
                case 1109:
                {
                    var transition = new Transition
                    {
                        Id = d.FileId,
                        DstState = d["m_DstState"]?.Ref.FileId ?? 0,
                        DstMachine = d["m_DstStateMachine"]?.Ref.FileId ?? 0,
                        IsExit = (d["m_IsExit"]?.Int() ?? 0) != 0,
                        HasExitTime = (d["m_HasExitTime"]?.Int() ?? 0) != 0,
                        ExitTime = d["m_ExitTime"]?.Float() ?? 0f,
                        Duration = MathF.Max(0f, d["m_TransitionDuration"]?.Float() ?? 0f),
                        FixedDuration = (d["m_HasFixedDuration"]?.Int() ?? 0) != 0,
                        Offset = d["m_TransitionOffset"]?.Float() ?? 0f,
                        CanTransitionToSelf = (d["m_CanTransitionToSelf"]?.Int(1) ?? 1) != 0,
                        Muted = (d["m_Mute"]?.Int() ?? 0) != 0,
                        Solo = (d["m_Solo"]?.Int() ?? 0) != 0,
                        InterruptionSource = d["m_InterruptionSource"]?.Int() ?? 0,
                        OrderedInterruption = (d["m_OrderedInterruption"]?.Int(1) ?? 1) != 0,
                    };
                    if (d["m_Conditions"] is UnitySeq conditions)
                        foreach (var c in conditions.Items)
                            transition.Conditions.Add(new Condition(c["m_ConditionMode"]?.Int() ?? 0, c["m_ConditionEvent"]?.Text ?? "", c["m_EventTreshold"]?.Float() ?? 0f));
                    controller.Transitions[d.FileId] = transition;
                    break;
                }
                case 206:
                    controller.BlendTrees[d.FileId] = ReadTree(d);
                    break;
                case 114:
                    if (ReadBehaviour(d) is { } behaviour)
                    {
                        behaviour.SourcePlayable = playable;
                        controller.Behaviours[d.FileId] = behaviour;
                    }
                    else if (!d.Stripped)
                        controller.Other(d);
                    break;
            }
        }
        controller.MarkEyes();
        return controller;
    }

    private void Other(UnityDocument d)
    {
        string kind = d["disableLocomotion"] != null ? "locomotion control"
            : d["enterPoseSpace"] != null ? "temporary pose space"
            : d.Body.Entries.Count > 0 ? "behaviours of another kind" : "";
        if (kind.Length > 0)
            OtherBehaviours[kind] = OtherBehaviours.GetValueOrDefault(kind) + 1;
    }

    private void MarkEyes()
    {
        foreach (var state in States.Values)
            state.EyesAnimated = state.Behaviours.Any(id => Behaviours.TryGetValue(id, out var b) && b is TrackingControl t && t.Modes[8] == 2);
    }

    // The avatar SDK's state behaviours, known by the fields they save.
    private static Behaviour? ReadBehaviour(UnityDocument d)
    {
        if (d["localOnly"] != null && d["parameters"] != null)
        {
            var driver = new ParameterDriver { Id = d.FileId, LocalOnly = (d["localOnly"]?.Int() ?? 0) != 0 };
            foreach (var e in (d["parameters"] as UnitySeq)?.Items ?? new List<UnityNode>())
            {
                driver.Entries.Add(new DriverEntry((DriverOperation)System.Math.Clamp(e["type"]?.Int() ?? 0, 0, 3),
                    e["name"]?.Text ?? "", e["source"]?.Text ?? "", e["value"]?.Float() ?? 0f,
                    e["valueMin"]?.Float() ?? 0f, e["valueMax"]?.Float() ?? 0f, e["chance"]?.Float(1f) ?? 1f,
                    (e["convertRange"]?.Int() ?? 0) != 0, e["sourceMin"]?.Float() ?? 0f, e["sourceMax"]?.Float() ?? 0f,
                    e["destMin"]?.Float() ?? 0f, e["destMax"]?.Float() ?? 0f));
            }
            return driver;
        }
        if (d["trackingEyes"] != null)
        {
            var tracking = new TrackingControl { Id = d.FileId };
            string[] parts = { "trackingHead", "trackingLeftHand", "trackingRightHand", "trackingHip", "trackingLeftFoot",
                "trackingRightFoot", "trackingLeftFingers", "trackingRightFingers", "trackingEyes", "trackingMouth" };
            for (int i = 0; i < parts.Length; i++)
                tracking.Modes[i] = d[parts[i]]?.Int() ?? 0;
            return tracking;
        }
        if (d["goalWeight"] != null && d["layer"] != null && d["blendDuration"] != null)
        {
            // The SDK numbers its blendable layers action, FX, gesture, additive.
            int Playable(int blendable) => blendable switch { 0 => 4, 1 => 5, 2 => 3, 3 => 2, _ => 5 };
            float goal = System.Math.Clamp(d["goalWeight"]?.Float() ?? 0f, 0f, 1f);
            float duration = MathF.Max(0f, d["blendDuration"]?.Float() ?? 0f);
            if (d["playable"] != null)
                return new LayerControl { Id = d.FileId, Playable = Playable(d["playable"]?.Int() ?? 1), Layer = d["layer"]?.Int() ?? 0, Goal = goal, Duration = duration };
            return new PlayableControl { Id = d.FileId, Playable = Playable(d["layer"]?.Int() ?? 0), Goal = goal, Duration = duration };
        }
        if (d["SourcePath"] != null && d["PlaybackOrder"] != null)
        {
            var audio = new PlayAudio
            {
                Id = d.FileId,
                SourcePath = d["SourcePath"]?.Text ?? "",
                Order = d["PlaybackOrder"]?.Int() ?? 0,
                Parameter = d["ParameterName"]?.Text ?? "",
                Volume = new float2(d["Volume"]?["x"]?.Float(1f) ?? 1f, d["Volume"]?["y"]?.Float(1f) ?? 1f),
                Pitch = new float2(d["Pitch"]?["x"]?.Float(1f) ?? 1f, d["Pitch"]?["y"]?.Float(1f) ?? 1f),
                VolumeApply = d["VolumeApplySettings"]?.Int() ?? 0,
                PitchApply = d["PitchApplySettings"]?.Int() ?? 0,
                ClipsApply = d["ClipsApplySettings"]?.Int() ?? 0,
                LoopApply = d["LoopApplySettings"]?.Int() ?? 0,
                Loop = (d["Loop"]?.Int() ?? 0) != 0,
                Delay = MathF.Max(0f, d["DelayInSeconds"]?.Float() ?? 0f),
                PlayOnEnter = (d["PlayOnEnter"]?.Int() ?? 0) != 0,
                StopOnEnter = (d["StopOnEnter"]?.Int() ?? 0) != 0,
                PlayOnExit = (d["PlayOnExit"]?.Int() ?? 0) != 0,
                StopOnExit = (d["StopOnExit"]?.Int() ?? 0) != 0,
            };
            if (d["Clips"] is UnitySeq clips)
                foreach (var c in clips.Items)
                    if (c.Ref.Guid is { Length: > 0 } guid)
                        audio.Clips.Add(guid);
            return audio;
        }
        return null;
    }

    internal static BlendTree ReadTree(UnityDocument d, string? assetGuid = null)
    {
        var tree = new BlendTree { Id = d.FileId, Type = d["m_BlendType"]?.Int() ?? 0,
            Parameter = d["m_BlendParameter"]?.Text ?? "", ParameterY = d["m_BlendParameterY"]?.Text ?? "",
            Normalized = (d["m_NormalizedBlendValues"]?.Int() ?? 0) != 0 };
        if (d["m_Childs"] is UnitySeq children)
            foreach (var c in children.Items)
            {
                var motion = c["m_Motion"]?.Ref ?? default;
                if (assetGuid != null && motion.IsLocal) motion = new UnityRef(motion.FileId, assetGuid, 2);
                tree.Children.Add((motion, c["m_Threshold"]?.Float() ?? 0f, c["m_DirectBlendParameter"]?.Text ?? ""));
                tree.Positions.Add((c["m_Position"]?["x"]?.Float() ?? 0, c["m_Position"]?["y"]?.Float() ?? 0));
                tree.Playback.Add((c["m_TimeScale"]?.Float(1f) ?? 1f, c["m_CycleOffset"]?.Float() ?? 0f, (c["m_Mirror"]?.Int() ?? 0) != 0));
            }
        return tree;
    }

    private static void Ids(UnityNode? node, List<long> into)
    {
        if (node is UnitySeq seq)
            foreach (var item in seq.Items)
                if (item.Ref.FileId != 0)
                    into.Add(item.Ref.FileId);
    }

    public Parameter? FindParameter(string name)
    {
        foreach (var p in Parameters)
            if (p.Name == name)
                return p;
        return null;
    }

    // Another controller's layers after these, as one more source of the same avatar. Its ids move to fresh
    // ones, its parameters take the names the merge gives them, and its clip references carry the tag so the
    // program can read each clip as that merge places it.
    public void Append(UnityAnimatorController other, Func<string, string> rename, string tag, int playable)
    {
        var ids = new Dictionary<long, long>();
        long next = -1;
        long Fresh(long id)
        {
            if (id == 0)
                return 0;
            if (ids.TryGetValue(id, out var known))
                return known;
            while (States.ContainsKey(next) || Machines.ContainsKey(next) || Transitions.ContainsKey(next)
                || BlendTrees.ContainsKey(next) || Behaviours.ContainsKey(next))
                next--;
            ids[id] = next;
            return next--;
        }
        UnityRef Motion(UnityRef motion)
        {
            if (motion.IsNull)
                return motion;
            if (motion.IsLocal)
                return new UnityRef(Fresh(motion.FileId), null, motion.Type);
            return tag.Length == 0 ? motion : new UnityRef(motion.FileId, Tagged(motion.Guid!, tag), motion.Type);
        }
        string? Name(string? name) => string.IsNullOrEmpty(name) ? name : rename(name);
        void Remap(List<long> list)
        {
            for (int i = 0; i < list.Count; i++)
                list[i] = Fresh(list[i]);
        }

        foreach (var p in other.Parameters)
        {
            var name = rename(p.Name);
            if (FindParameter(name) == null)
                Parameters.Add(p with { Name = name });
        }
        int baseIndex = Layers.Count;
        foreach (var l in other.Layers)
        {
            var layer = new Layer
            {
                Name = l.Name, Index = baseIndex + l.Index, Machine = Fresh(l.Machine), Weight = l.Weight, Additive = l.Additive,
                Mask = l.Mask, SyncedLayer = l.SyncedLayer >= 0 ? baseIndex + l.SyncedLayer : -1, Playable = playable,
                PlayableIndex = l.PlayableIndex, Tag = tag,
            };
            foreach (var (state, motion) in l.SyncedMotions)
                layer.SyncedMotions[Fresh(state)] = Motion(motion);
            Layers.Add(layer);
        }
        foreach (var (id, m) in other.Machines)
        {
            var machine = new StateMachine { Id = Fresh(id), Name = m.Name, DefaultState = Fresh(m.DefaultState) };
            machine.States.AddRange(m.States); Remap(machine.States);
            machine.Machines.AddRange(m.Machines); Remap(machine.Machines);
            machine.AnyTransitions.AddRange(m.AnyTransitions); Remap(machine.AnyTransitions);
            machine.EntryTransitions.AddRange(m.EntryTransitions); Remap(machine.EntryTransitions);
            machine.Behaviours.AddRange(m.Behaviours); Remap(machine.Behaviours);
            foreach (var (from, list) in m.MachineTransitions)
            {
                var copy = new List<long>(list);
                Remap(copy);
                machine.MachineTransitions[Fresh(from)] = copy;
            }
            Machines[machine.Id] = machine;
        }
        foreach (var (id, s) in other.States)
        {
            var state = new State
            {
                Id = Fresh(id), Name = s.Name, Motion = Motion(s.Motion), WriteDefaults = s.WriteDefaults, TimeParameter = Name(s.TimeParameter),
                Speed = s.Speed, SpeedParameter = Name(s.SpeedParameter), CycleOffset = s.CycleOffset, Mirror = s.Mirror,
                MirrorParameter = Name(s.MirrorParameter), CycleOffsetParameter = Name(s.CycleOffsetParameter), EyesAnimated = s.EyesAnimated,
            };
            state.Transitions.AddRange(s.Transitions); Remap(state.Transitions);
            state.Behaviours.AddRange(s.Behaviours); Remap(state.Behaviours);
            States[state.Id] = state;
        }
        foreach (var (id, t) in other.Transitions)
        {
            var transition = new Transition
            {
                Id = Fresh(id), DstState = Fresh(t.DstState), DstMachine = Fresh(t.DstMachine), IsExit = t.IsExit, HasExitTime = t.HasExitTime,
                ExitTime = t.ExitTime, Duration = t.Duration, FixedDuration = t.FixedDuration, Offset = t.Offset,
                CanTransitionToSelf = t.CanTransitionToSelf, Muted = t.Muted, Solo = t.Solo, InterruptionSource = t.InterruptionSource,
                OrderedInterruption = t.OrderedInterruption,
            };
            foreach (var c in t.Conditions)
                transition.Conditions.Add(c with { Parameter = rename(c.Parameter) });
            Transitions[transition.Id] = transition;
        }
        foreach (var (id, t) in other.BlendTrees)
        {
            var tree = new BlendTree { Id = Fresh(id), Type = t.Type, Parameter = Name(t.Parameter) ?? "", ParameterY = Name(t.ParameterY) ?? "", Normalized = t.Normalized };
            tree.Positions.AddRange(t.Positions);
            tree.Playback.AddRange(t.Playback);
            foreach (var c in t.Children)
                tree.Children.Add((Motion(c.Motion), c.Threshold, Name(c.DirectParameter) ?? ""));
            BlendTrees[tree.Id] = tree;
        }
        foreach (var (id, b) in other.Behaviours)
        {
            Behaviour copy = b switch
            {
                ParameterDriver d => Driver(d),
                TrackingControl t => Tracking(t),
                LayerControl l => new LayerControl { Playable = l.Playable, Layer = l.Layer, Goal = l.Goal, Duration = l.Duration },
                PlayableControl p => new PlayableControl { Playable = p.Playable, Goal = p.Goal, Duration = p.Duration },
                PlayAudio a => Audio(a),
                _ => b,
            };
            copy.Id = Fresh(id);
            copy.SourcePlayable = playable;
            copy.Tag = tag;
            Behaviours[copy.Id] = copy;
        }
        foreach (var (kind, count) in other.OtherBehaviours)
            OtherBehaviours[kind] = OtherBehaviours.GetValueOrDefault(kind) + count;

        ParameterDriver Driver(ParameterDriver d)
        {
            var copy = new ParameterDriver { LocalOnly = d.LocalOnly };
            foreach (var e in d.Entries)
                copy.Entries.Add(e with { Name = rename(e.Name), Source = string.IsNullOrEmpty(e.Source) ? e.Source : rename(e.Source) });
            return copy;
        }
        TrackingControl Tracking(TrackingControl t)
        {
            var copy = new TrackingControl();
            Array.Copy(t.Modes, copy.Modes, copy.Modes.Length);
            return copy;
        }
        PlayAudio Audio(PlayAudio a)
        {
            var copy = new PlayAudio
            {
                SourcePath = a.SourcePath, Order = a.Order, Parameter = Name(a.Parameter) ?? "", Volume = a.Volume, Pitch = a.Pitch,
                VolumeApply = a.VolumeApply, PitchApply = a.PitchApply, ClipsApply = a.ClipsApply, LoopApply = a.LoopApply, Loop = a.Loop,
                Delay = a.Delay, PlayOnEnter = a.PlayOnEnter, StopOnEnter = a.StopOnEnter, PlayOnExit = a.PlayOnExit, StopOnExit = a.StopOnExit,
            };
            copy.Clips.AddRange(a.Clips);
            return copy;
        }
    }

    // A clip reference as one merge reads it: "guid|tag". Plain guids stay as they are.
    public static string Tagged(string guid, string tag) => tag.Length == 0 ? guid : guid + "|" + tag;

    public static (string Guid, string Tag) Untag(string key)
    {
        int bar = key.IndexOf('|');
        return bar < 0 ? (key, "") : (key[..bar], key[(bar + 1)..]);
    }

    // Every parameter anything in a layer reads or writes: conditions, blend trees, state time, speed,
    // mirror and offset parameters, and the drivers and audio behaviours on its states.
    public HashSet<string> ParametersOf(Layer layer)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var machine in MachinesUnder(layer.Machine))
        {
            foreach (var id in machine.AnyTransitions.Concat(machine.EntryTransitions).Concat(machine.MachineTransitions.Values.SelectMany(l => l)))
                if (Transitions.TryGetValue(id, out var t))
                    foreach (var c in t.Conditions)
                        names.Add(c.Parameter);
            foreach (var stateId in machine.States)
            {
                if (!States.TryGetValue(stateId, out var state))
                    continue;
                foreach (var id in state.Transitions)
                    if (Transitions.TryGetValue(id, out var t))
                        foreach (var c in t.Conditions)
                            names.Add(c.Parameter);
                foreach (var p in new[] { state.TimeParameter, state.SpeedParameter, state.MirrorParameter, state.CycleOffsetParameter })
                    if (!string.IsNullOrEmpty(p))
                        names.Add(p);
                if (state.Motion.IsLocal && BlendTrees.TryGetValue(state.Motion.FileId, out var tree))
                    foreach (var p in TreeParameters(tree, 0))
                        names.Add(p);
                foreach (var id in state.Behaviours)
                    BehaviourParameters(id, names);
            }
            foreach (var id in machine.Behaviours)
                BehaviourParameters(id, names);
        }
        return names;
    }

    private void BehaviourParameters(long id, HashSet<string> names)
    {
        switch (Behaviours.GetValueOrDefault(id))
        {
            case ParameterDriver driver:
                foreach (var e in driver.Entries)
                {
                    if (e.Name.Length > 0) names.Add(e.Name);
                    if (e.Type == DriverOperation.Copy && e.Source.Length > 0) names.Add(e.Source);
                }
                break;
            case PlayAudio audio when audio.Parameter.Length > 0:
                names.Add(audio.Parameter);
                break;
        }
    }

    // Every parameter the whole controller reads or writes.
    public HashSet<string> ReferencedParameters()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in Layers)
            names.UnionWith(ParametersOf(layer));
        foreach (var tree in BlendTrees.Values)
            names.UnionWith(TreeParameters(tree, 0));
        return names;
    }

    private IEnumerable<string> TreeParameters(BlendTree tree, int depth)
    {
        if (depth > 8)
            yield break;
        if (!string.IsNullOrEmpty(tree.Parameter))
            yield return tree.Parameter;
        if (tree.Type is 1 or 2 or 3 && !string.IsNullOrEmpty(tree.ParameterY))
            yield return tree.ParameterY;
        foreach (var child in tree.Children)
        {
            if (tree.Type == 4 && !string.IsNullOrEmpty(child.DirectParameter))
                yield return child.DirectParameter;
            if (child.Motion.IsLocal && BlendTrees.TryGetValue(child.Motion.FileId, out var inner))
                foreach (var p in TreeParameters(inner, depth + 1))
                    yield return p;
        }
    }

    public IEnumerable<StateMachine> MachinesUnder(long id, int depth = 0)
    {
        if (depth > 16 || !Machines.TryGetValue(id, out var machine))
            yield break;
        yield return machine;
        foreach (var child in machine.Machines)
            foreach (var inner in MachinesUnder(child, depth + 1))
                yield return inner;
    }

    // The state a layer comes to rest in for these parameter values. Transitions out of a state that
    // need nothing but time are taken too: they are how a one-shot returns, and resting means after it
    // has. A path that comes back on itself stops where it was.
    // Timed off stops before transitions that only wait out time, for a reaction that should be read
    // while it plays rather than after it has run back to idle.
    public State? Settle(Layer layer, IReadOnlyDictionary<string, float> values, bool timed = true)
    {
        var current = Enter(layer.Machine, values, 0);
        if (current == null)
            return null;
        var any = MachinesUnder(layer.Machine).SelectMany(m => m.AnyTransitions).ToList();
        var seen = new HashSet<long> { current.Id };
        for (int step = 0; step < 32; step++)
        {
            Transition? taken = null;
            foreach (var id in any)
            {
                if (Transitions.TryGetValue(id, out var t) && t.DstState != current.Id && Passes(t, values, current, timed))
                {
                    taken = t;
                    break;
                }
            }
            if (taken == null)
            {
                foreach (var id in current.Transitions)
                {
                    if (Transitions.TryGetValue(id, out var t) && Passes(t, values, current, timed))
                    {
                        taken = t;
                        break;
                    }
                }
            }
            if (taken == null)
                break;
            var next = taken.IsExit ? Enter(layer.Machine, values, 0)
                : taken.DstState != 0 && States.TryGetValue(taken.DstState, out var direct) ? direct
                : taken.DstMachine != 0 ? Enter(taken.DstMachine, values, 0) : null;
            if (next == null || !seen.Add(next.Id))
            {
                if (next != null)
                    current = next;
                break;
            }
            current = next;
        }
        return current;
    }

    internal State? Enter(long machineId, IReadOnlyDictionary<string, float> values, int depth)
    {
        if (depth > 16 || !Machines.TryGetValue(machineId, out var machine))
            return null;
        foreach (var id in machine.EntryTransitions)
        {
            if (!Transitions.TryGetValue(id, out var t) || !Passes(t, values, null))
                continue;
            if (t.DstState != 0 && States.TryGetValue(t.DstState, out var state))
                return state;
            if (t.DstMachine != 0)
                return Enter(t.DstMachine, values, depth + 1);
        }
        if (States.TryGetValue(machine.DefaultState, out var fallback))
            return fallback;
        if (Machines.ContainsKey(machine.DefaultState))
            return Enter(machine.DefaultState, values, depth + 1);
        return machine.Machines.Count > 0 ? Enter(machine.Machines[0], values, depth + 1) : null;
    }

    private static bool Passes(Transition t, IReadOnlyDictionary<string, float> values, State? from, bool timed = true)
    {
        if (t.Muted)
            return false;
        if (t.Conditions.Count == 0)
            return from == null || timed && (t.HasExitTime || t.IsExit);
        foreach (var c in t.Conditions)
        {
            float v = values.TryGetValue(c.Parameter, out var found) ? found : 0f;
            if (!Test(c.Mode, v, c.Threshold))
                return false;
        }
        return true;
    }

    // One condition: 1 if, 2 if not, 3 greater, 4 less, 6 equals, 7 not equal.
    public static bool Test(int mode, float value, float threshold) => mode switch
    {
        1 => value > 0.5f,
        2 => value <= 0.5f,
        3 => value > threshold,
        4 => value < threshold,
        6 => MathF.Abs(value - threshold) < 0.5f,
        7 => MathF.Abs(value - threshold) >= 0.5f,
        _ => false,
    };
}
