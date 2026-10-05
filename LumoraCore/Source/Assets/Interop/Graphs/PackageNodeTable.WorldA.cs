// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lumora.Core.Components;
using Lumora.Core.Persistence;
using Lumora.Core.Physics;

namespace Lumora.Core.Assets.Interop;

// Package nodes first seen in worlds: physics, contacts, grabbing, anchors, body nodes, input and the user's root.
//
// A contact node listens to one collider, and a still collider is only ever touched by what moves on its own:
// people and physics bodies. Where a graph acts on a contact only after asking whether the other side is a
// character, and does nothing otherwise, the contact is heard here as a person touching, which asks the
// physics nothing and costs nothing while nobody is near, and the asking is true. Anything else is a
// trigger's or a collision's event, as the package's start and end are. -xlinka
internal static partial class PackageNodeTable
{
    private static readonly ConditionalWeakTable<PackageNodeIndex, Dictionary<string, List<PackageNode>>> ReadersByIndex = new();
    private static readonly List<PackageNode> NoReaders = new();

    private static void WorldA()
    {
        WorldAContacts();
        WorldAGrabbing();
        WorldABodies();
        WorldADevices();
    }

    private static void WorldAContacts()
    {
        // Asked of a contact heard as a person's, it is one. Asked of anything else: whether it is someone's user
        // root, where a person's character collider sits.
        On("IsCharacterController", s =>
        {
            if (ContactOf(s.Index, s.Node.Ref("Collider")) is { } contact && HeardAsPerson(s.Index, contact))
            {
                s.OutConstant("", true);
                return;
            }
            if (!s.Wired("Collider"))
            {
                s.OutConstant("", false);
                return;
            }
            string owner = s.Add("BodyOwner");
            s.In("Collider", owner, "slot");
            string root = s.Add("UserBodyRoot");
            s.Wire(owner, "user", root, "user");
            string same = s.Add("IsSame", "slot");
            s.Wire(root, "slot", same, "a");
            s.In("Collider", same, "b");
            string both = s.Add("And", "bool,bool");
            s.Wire(owner, "body", both, "a");
            s.Wire(same, "same", both, "b");
            s.Out("", both, "result");
        });

        foreach (var (cls, person, trigger, collision) in new[]
                 {
                     ("OnContactStart", "OnPersonEnter", "OnTriggerEnter", "OnCollisionEnter"),
                     ("OnContactStay", "OnPersonStay", "OnTriggerStay", "OnCollisionStay"),
                     ("OnContactEnd", "OnPersonExit", "OnTriggerExit", "OnCollisionExit"),
                 })
        {
            TryGet(cls, out var earlier);
            On(cls, s =>
            {
                // A global reference that holds nothing listens to nothing there either.
                if (s.Unset("Collider"))
                {
                    s.Problem("listens to no collider, so it never fires");
                    s.OutConstant("Other", null);
                    return;
                }
                if (s.WorldOf("Collider") is not Component collider)
                {
                    Unheard(s, "Collider", "a collider", "Other");
                    return;
                }
                bool people = HeardAsPerson(s.Index, s.Node);
                if (!people && earlier != null)
                {
                    earlier(s);
                    return;
                }
                bool isTrigger = collider is Collider { Type.Value: ColliderType.Trigger };
                string heard = s.Add(people ? person : isTrigger ? trigger : collision);
                s.Bind(heard, "source", collider.Slot, "slot");
                s.Out("Other", heard, people ? "body" : "other");
                s.Exit("OnEvent", heard, "next");
            });
        }

        // The hit is cast again in each flow step that reads it, so a read after something moved sees the world as
        // it is then; the package keeps what its one cast found. With no range it reaches 1000 km here.
        On("RaycastOne", s =>
        {
            string cast = s.Add("CastRay");
            s.In("Origin", cast, "origin");
            s.In("Direction", cast, "direction");
            s.In("MaxDistance", cast, "range", 1e6);
            s.In("HitTriggers", cast, "triggers", false);
            s.In("UsersOnly", cast, "usersOnly", false);
            s.In("IgnoreHierarchy", cast, "ignore");
            s.In("Root", cast, "root");
            string struck = s.Add("If");
            s.Op("", struck);
            s.Wire(cast, "hit", struck, "condition");
            s.Exit("OnHit", struck, "true");
            s.Exit("OnMiss", struck, "false");
            s.Out("HitCollider", cast, "slot");
            s.Out("HitPoint", cast, "point");
            s.Out("HitNormal", cast, "normal");
            s.Out("HitDistance", cast, "distance");
            if (s.IsRead("HitTriangleIndex"))
            {
                string mesh = s.Add("HitMeshPoint");
                s.Wire(cast, "slot", mesh, "slot");
                s.Wire(cast, "point", mesh, "point");
                s.Out("HitTriangleIndex", mesh, "triangle");
            }
            if (s.Wired("DebugDuration"))
                s.Problem("its drawing of the ray for debugging is not carried");
        });
    }

    private static void WorldAGrabbing()
    {
        // Told on the machine that grabbed: heard here where the one who grabbed is the user at this machine.
        On("OnGrabbableGrabbed", s =>
        {
            var held = SlotOfElement(s.WorldOf("Grabbable"));
            if (held == null)
            {
                if (s.Unset("Grabbable"))
                    s.Problem("listens to no grabbable, so it never fires");
                else
                    Unheard(s, "Grabbable", "a grabbable");
                return;
            }
            string grabbed = s.Add("OnGrabbed");
            s.Bind(grabbed, "source", held, "slot");
            s.Exit("OnGrabbed", OnlyHere(s, grabbed), "true");
        });

        // Told on the machine that let go, the same way.
        On("OnGrabbableReleased", s =>
        {
            string released = s.Add("OnLetGo");
            s.In("Grabbable", released, "source");
            s.Exit("OnReleased", OnlyHere(s, released), "true");
        });

        // A holder is only read here through Grabber Body Node and Is Null, which look through to the grabbable.
        On("GrabbableGrabber", s =>
        {
            foreach (var reader in ReadersOf(s.Index, s.Node.Id))
            {
                if (reader.Class != "GrabberBodyNode")
                {
                    s.Fail("its grabber is read by something that has no counterpart for one here");
                    return;
                }
            }
        });

        // The package's hand grabbers carry LeftHand and RightHand; anything else, or nothing holding, is NONE.
        On("GrabberBodyNode", s =>
        {
            if (GrabbableOf(s, "Grabber") is not { } grabbable)
            {
                s.Fail($"reads the hand of {Aimed(s, "Grabber")}");
                return;
            }
            string hand = s.Add("HoldingHand");
            Feed(s, grabbable, hand, "slot");
            string onSide = s.Add("BodyNodeOnSide");
            s.Set(onSide, "node", "LeftHand");
            s.Wire(hand, "side", onSide, "side");
            string none = s.Add("IsSame", "chirality");
            s.Wire(hand, "side", none, "a");
            s.Set(none, "b", "None");
            string pick = s.Add("Conditional", "bodyNode");
            s.Wire(none, "same", pick, "condition");
            s.Set(pick, "whenTrue", "NONE");
            s.Wire(onSide, "result", pick, "whenFalse");
            s.Out("", pick, "result");
        });

        // The user at this machine lets go with the hand the body node names, and flow goes on only when that
        // hand was there. Only LeftHand and RightHand name a hand, as only those carry one in the package. Its
        // quiet release, which tells nobody, has no counterpart: what it holds hears the release.
        On("ReleaseAllGrabbed", s =>
        {
            string split = s.Add("SplitBodyNode");
            s.In("Node", split, "node");
            string onSide = s.Add("BodyNodeOnSide");
            s.Set(onSide, "node", "LeftHand");
            s.Wire(split, "side", onSide, "side");
            string isHand = s.Add("IsSame", "bodyNode");
            s.Wire(onSide, "result", isHand, "a");
            s.In("Node", isHand, "b");
            string hand = s.Add("If");
            s.Wire(isHand, "same", hand, "condition");
            s.Op("", hand);
            string release = s.Add("LetGo");
            s.Wire(split, "side", release, "side");
            s.Flow(hand, "true", release);
            string done = s.Add("If");
            s.Wire(release, "released", done, "condition");
            s.Flow(release, "next", done);
            s.Exit("Next", done, "true");
            if (s.Wired("SupressEvents") && !(s.Feeder("SupressEvents") is { Class: "ValueInput" } quiet && quiet.Value("Value") is false))
                s.Problem("its release without telling anyone is not carried");
        });

        // Read from the tool's slot. A tool that did not come across is not equipped here, so it reads false.
        On("IsToolEquipped", s =>
        {
            if (s.WorldOf("Tool") is Component tool)
            {
                string state = s.Add("ToolState");
                s.Bind(state, "slot", tool.Slot, "slot");
                s.Out("", state, "equipped");
                return;
            }
            if (s.Wired("Tool") && s.Feeder("Tool") is { Class: not ("RefObjectInput" or "GlobalReference") })
            {
                string state = s.Add("ToolState");
                s.In("Tool", state, "slot");
                s.Out("", state, "equipped");
                return;
            }
            if (s.Wired("Tool") && !s.Unset("Tool"))
                s.Problem($"reads {Aimed(s, "Tool")}, so it reads false");
            s.OutConstant("", false);
        });
    }

    private static void WorldABodies()
    {
        // The user's bone for the node, else what tracking moves for it; this machine's user with none given.
        On("BodyNodeSlot", s =>
        {
            string bone = s.Add("UserBoneSlot");
            UserOrMachine(s, s.Node, "Source", bone, "user");
            s.In("Node", bone, "node");
            s.Out("", bone, "bone");
        });

        // Moving the feet moves the whole body by as much: the hips go there plus the step. Without a user root
        // the package's set does nothing and flow stops.
        On("SetFeetPosition", s =>
        {
            if (!s.Wired("UserRoot"))
            {
                s.OpEnds("");
                return;
            }
            string pose = s.Add("UserBodyPose");
            s.In("UserRoot", pose, "user");
            string step = s.Add("Subtract", "float3,float3");
            s.In("Position", step, "a", new[] { 0d, 0d, 0d });
            s.Wire(pose, "feet", step, "b");
            string hips = s.Add("Add", "float3,float3");
            s.Wire(pose, "hips", hips, "a");
            s.Wire(step, "result", hips, "b");
            string place = s.Add("PlaceHips");
            s.In("UserRoot", place, "user");
            s.Wire(hips, "result", place, "position");
            s.Op("", place);
            s.Exit("Next", place, "next");
        });

        // Turning the feet turns the whole body by as much: the hips turn by the turn that takes the feet there.
        On("SetFeetRotation", s =>
        {
            if (!s.Wired("UserRoot"))
            {
                s.OpEnds("");
                return;
            }
            string pose = s.Add("UserBodyPose");
            s.In("UserRoot", pose, "user");
            string turn = s.Add("FromToRotation", "floatQ,floatQ");
            s.Wire(pose, "feetRotation", turn, "from");
            s.Wire(FacingFromPackage(s, "Rotation"), "rotation", turn, "to");
            string hips = s.Add("CombineRotations");
            s.Wire(pose, "hipsRotation", hips, "first");
            s.Wire(turn, "rotation", hips, "then");
            string place = s.Add("TurnHips");
            s.In("UserRoot", place, "user");
            s.Wire(hips, "rotation", place, "rotation");
            s.Op("", place);
            s.Exit("Next", place, "next");
        });

        // Body rotations read back as the package's: the head as it is turned, the hips, the feet and the head's
        // level facing.
        foreach (var (cls, ours, port) in new[]
        {
            ("HeadRotation", "HeadOrientation", "rotation"), ("HipsRotation", "UserBodyPose", "hipsRotation"),
            ("FeetRotation", "UserBodyPose", "feetRotation"), ("HeadFacingRotation", "UserBodyPose", "facingRotation"),
        })
        {
            On(cls, s =>
            {
                string node = s.Add(ours);
                s.In("UserRoot", node, "user");
                FacingToPackage(s, "", node, port);
            });
        }
    }

    // A package body faces along a rotation's +Z and ours along its -Z, so the same facing is a half turn about
    // the rotation's own up apart. A rotation a package graph hands in is turned onto ours, and one of ours read
    // out is turned back. Up and the turn about it stay as they were, so a turn worked out on one comes out the
    // same on the other. -xlinka
    private static readonly double[] HalfTurnAboutUp = { 0d, 1d, 0d, 0d };

    private static string FacingFromPackage(PackageNodeScope s, string theirInput)
    {
        string facing = s.Add("CombineRotations");
        s.Set(facing, "first", HalfTurnAboutUp);
        s.In(theirInput, facing, "then", new[] { 0d, 0d, 0d, 1d });
        return facing;
    }

    private static void FacingToPackage(PackageNodeScope s, string theirOutput, string ourNode, string ourPort)
    {
        string facing = s.Add("CombineRotations");
        s.Set(facing, "first", HalfTurnAboutUp);
        s.Wire(ourNode, ourPort, facing, "then");
        s.Out(theirOutput, facing, "rotation");
    }

    private static void WorldADevices()
    {
        // The package's raw move in pixels, up positive: ours is in screen heights of 1080 pixels, down positive,
        // at this person's mouse sensitivity, which is 1 unless they changed it.
        On("MouseMovementDelta", s =>
        {
            string motion = s.Add("MouseMotion");
            string pixels = s.Add("Multiply", "float2,float2");
            s.Wire(motion, "look", pixels, "a");
            s.Set(pixels, "b", new[] { 1080d, -1080d });
            s.Out("", pixels, "result");
        });

        // A head device named as the package names them, as ours does: a headless one is a server here, every
        // headset is VR and every camera a camera.
        foreach (var cls in new[] { "ValueInput", "GlobalValue" })
        {
            if (!TryGet(cls, out var plain))
                continue;
            On(cls, s =>
            {
                if (s.Node.Args.Count == 1 && s.Node.Args[0] == "HeadOutputDevice" && s.Node.Value("Value") is string device)
                {
                    s.OutConstant("", device switch
                    {
                        "Headless" => "Server",
                        "Screen" or "Screen360" => "Screen",
                        "StaticCamera" or "StaticCamera360" => "Camera",
                        "SteamVR" or "WindowsMR" or "Oculus" or "OculusQuest" => "VR",
                        _ => device,
                    });
                    return;
                }
                plain(s);
            });
        }

        // The package buzzes a controller for a time between the controller's shortest and longest, at full
        // strength; ours takes the time in seconds, here between a touch controller's 5 and 100 milliseconds.
        // No side, or none of the two, finds no controller: nothing buzzes and flow stops.
        On("TriggerHapticsOnController", s =>
        {
            var side = s.Feeder("Side");
            string? named = side is { Class: "ValueInput" } ? side.Value("Value") as string : null;
            if (!s.Wired("Side") || (side is { Class: "ValueInput" } && named is not ("Left" or "Right")))
            {
                s.OpEnds("");
                return;
            }
            const double shortest = 0.005, longest = 0.1;
            string pulse = s.Add("HapticPulse");
            if (named != null)
                s.Set(pulse, "right", named == "Right");
            else
            {
                string right = s.Add("IsSame", "chirality");
                s.In("Side", right, "a");
                s.Set(right, "b", "Right");
                s.Wire(right, "same", pulse, "right");
            }
            s.Set(pulse, "strength", 1d);
            if (s.Feeder("RelativeIntensity") is { Class: "ValueInput" } fixedIntensity && fixedIntensity.Value("Value") is IConvertible amount)
                s.Set(pulse, "seconds", shortest + (longest - shortest) * Convert.ToDouble(amount, CultureInfo.InvariantCulture));
            else if (!s.Wired("RelativeIntensity"))
                s.Set(pulse, "seconds", shortest + (longest - shortest) * 0.5);
            else
            {
                string scaled = s.Add("Multiply", "float,float");
                s.In("RelativeIntensity", scaled, "a");
                s.Set(scaled, "b", longest - shortest);
                string seconds = s.Add("Add", "float,float");
                s.Wire(scaled, "result", seconds, "a");
                s.Set(seconds, "b", shortest);
                s.Wire(seconds, "result", pulse, "seconds");
            }
            s.Op("", pulse);
            string done = s.Add("If");
            s.Wire(pulse, "pulsed", done, "condition");
            s.Flow(pulse, "next", done);
            s.Exit("Next", done, "true");
        });

        // Told on the machine of whoever types: heard here where that is the user at this machine.
        On("TextEditorEvents", s =>
        {
            var field = SlotOfElement(s.WorldOf("Editor"));
            if (field == null)
            {
                if (s.Unset("Editor"))
                    s.Problem("listens to no text editor, so it never fires");
                else
                    Unheard(s, "Editor", "a text editor");
                return;
            }
            foreach (var (call, ours) in new[]
                     {
                         ("EditingStarted", "OnEditStarted"), ("EditingChanged", "OnTextEdited"),
                         ("EditingFinished", "OnEditEnded"), ("SubmitPressed", "OnTextSubmitted"),
                     })
            {
                if (!s.Wired(call))
                    continue;
                string heard = s.Add(ours);
                s.Bind(heard, "source", field, "slot");
                s.Exit(call, OnlyHere(s, heard), "true");
            }
        });
    }

    // An event that names who did it, going on only where that is the user at this machine. Answers the If.
    private static string OnlyHere(PackageNodeScope s, string heard)
    {
        string mine = s.Add("IsMachineUser");
        s.Wire(heard, "user", mine, "user");
        string here = s.Add("If");
        s.Flow(heard, "next", here);
        s.Wire(mine, "local", here, "condition");
        return here;
    }

    // An event aimed at something of the package's that did not come across never fires here, and the rest of
    // its graph still runs; one aimed at something worked out as the graph runs has nothing to listen to.
    private static void Unheard(PackageNodeScope s, string member, string what, params string[] outputs)
    {
        if (s.Feeder(member) is { } feeder && (feeder.Class.StartsWith("GlobalReference", StringComparison.Ordinal)
                || feeder.Class.StartsWith("RefObjectInput", StringComparison.Ordinal)))
        {
            s.Problem($"listens to {Aimed(s, member)}, so it never fires");
            foreach (var output in outputs)
                s.OutConstant(output, null);
            return;
        }
        s.Fail($"listens to {what} worked out as the graph runs, which has nothing to listen to here");
    }

    private static Slot? SlotOfElement(IWorldElement? element) => element switch
    {
        Slot slot => slot,
        Component component => component.Slot,
        _ => null,
    };

    // What a member reaches for, through a global reference or a reference input, for saying what is missing.
    private static string Aimed(PackageNodeScope s, string member)
    {
        string? id = s.Node.Ref(member);
        if (s.Index.TryNode(id, out var node, out _))
            id = node.Class.StartsWith("GlobalReference", StringComparison.Ordinal) ? node.Ref("Reference")
                : node.Class.StartsWith("RefObjectInput", StringComparison.Ordinal) ? node.Ref("Target") : id;
        return s.Index.Describe(id);
    }

    // The grabbable a holder read belongs to: the one Grabbable Grabber asks of, or the one whose holder field a
    // reference source reads. Either the package input that names it or its slot.
    private sealed record GrabbableSource(PackageNode? Node, string? Input, Slot? Slot);

    private static GrabbableSource? GrabbableOf(PackageNodeScope s, string member)
    {
        var feeder = s.Feeder(member);
        if (feeder == null)
            return null;
        if (feeder.Class == "GrabbableGrabber")
            return new GrabbableSource(feeder, "Grabbable", null);
        if (feeder.Class != "ReferenceSource")
            return null;
        string? id = feeder.Ref("Source");
        if (s.Index.TryNode(id, out var global, out _) && global.Class.StartsWith("GlobalReference", StringComparison.Ordinal))
            id = global.Ref("Reference");
        return s.Index.OwnerOf(id, out string field) is Component { IsDestroyed: false } owner && field == "_grabber"
            && owner.Slot is { } slot ? new GrabbableSource(null, null, slot) : null;
    }

    private static void Feed(PackageNodeScope s, GrabbableSource grabbable, string ourNode, string port)
    {
        if (grabbable.Slot != null)
            s.Bind(ourNode, port, grabbable.Slot, "slot");
        else
            s.InFrom(grabbable.Node!, grabbable.Input!, ourNode, port);
    }

    // The contact node whose Other an input reads, through relays.
    private static PackageNode? ContactOf(PackageNodeIndex index, string? id)
    {
        for (int depth = 0; depth < 32 && index.TryNode(id, out var node, out string member); depth++)
        {
            if (member.Length == 0 && node.Class.StartsWith("ObjectRelay", StringComparison.Ordinal))
            {
                id = node.Ref("Input");
                continue;
            }
            return member == "Other" && node.Class is "OnContactStart" or "OnContactStay" or "OnContactEnd" ? node : null;
        }
        return null;
    }

    // Whether a contact is only ever asked of whether it is a character, and its flow does nothing but behind an
    // If on that answer with nothing on the false side, through relays, sequences and an index sent through a
    // demultiplexer back into a multiplexer.
    private static bool HeardAsPerson(PackageNodeIndex index, PackageNode contact)
    {
        string? other = ((contact.Data.TryGetNode("Other") as DataTreeDictionary)?.TryGetNode("ID") as DataTreeValue)?.Value as string;
        return other != null && contact.Ref("OnEvent") != null && OnlyAskedIfCharacter(index, other, 0)
            && ActsOnlyOnCharacters(index, contact, contact.Ref("OnEvent"), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), 0);
    }

    private static bool OnlyAskedIfCharacter(PackageNodeIndex index, string id, int depth)
    {
        var readers = ReadersOf(index, id);
        if (readers.Count == 0 || depth > 16)
            return false;
        foreach (var reader in readers)
        {
            if (reader.Class == "IsCharacterController")
                continue;
            if (reader.Class.StartsWith("ObjectRelay", StringComparison.Ordinal) && OnlyAskedIfCharacter(index, reader.Id, depth + 1))
                continue;
            return false;
        }
        return true;
    }

    private static bool ActsOnlyOnCharacters(PackageNodeIndex index, PackageNode contact, string? id, Dictionary<string, int> chosen, int depth)
    {
        if (id == null)
            return true;
        if (depth > 64 || !index.TryNode(id, out var node, out string member))
            return false;
        switch (node.Class)
        {
            case "ContinuationRelay":
                return ActsOnlyOnCharacters(index, contact, node.Ref("Next"), chosen, depth + 1);
            case "ImpulseDemultiplexer":
                if (!member.StartsWith("Operations[", StringComparison.Ordinal)
                    || !int.TryParse(member[11..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entry))
                    return false;
                chosen[node.Id] = entry;
                return ActsOnlyOnCharacters(index, contact, node.Ref("OnTriggered"), chosen, depth + 1);
            case "ImpulseMultiplexer":
            {
                var targets = node.Refs("Impulses");
                if (index.TryNode(node.Ref("Index"), out var from, out string output) && output == "Index"
                    && chosen.TryGetValue(from.Id, out int picked))
                    return picked < 0 || picked >= targets.Count || ActsOnlyOnCharacters(index, contact, targets[picked], chosen, depth + 1);
                foreach (var target in targets)
                {
                    if (!ActsOnlyOnCharacters(index, contact, target, chosen, depth + 1))
                        return false;
                }
                return true;
            }
            case "Sequence":
                foreach (var target in node.Refs("Calls"))
                {
                    if (!ActsOnlyOnCharacters(index, contact, target, chosen, depth + 1))
                        return false;
                }
                return true;
            case "If":
                return node.Ref("OnFalse") == null && AsksCharacter(index, node.Ref("Condition"), contact);
            default:
                return false;
        }
    }

    private static bool AsksCharacter(PackageNodeIndex index, string? id, PackageNode contact)
    {
        for (int depth = 0; depth < 32 && index.TryNode(id, out var node, out string member); depth++)
        {
            if (member.Length == 0 && node.Class.StartsWith("ValueRelay", StringComparison.Ordinal))
            {
                id = node.Ref("Input");
                continue;
            }
            return member.Length == 0 && node.Class == "IsCharacterController" && ReferenceEquals(ContactOf(index, node.Ref("Collider")), contact);
        }
        return false;
    }

    // Every package node holding an id in one of its members, worked out once per package.
    private static List<PackageNode> ReadersOf(PackageNodeIndex index, string? id)
    {
        var readers = ReadersByIndex.GetValue(index, static built =>
        {
            var map = new Dictionary<string, List<PackageNode>>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in built.Nodes.Values)
            {
                foreach (var held in PackageNodeIndex.References(node))
                {
                    if (!map.TryGetValue(held, out var list))
                        map[held] = list = new List<PackageNode>();
                    list.Add(node);
                }
            }
            return map;
        });
        return id != null && readers.TryGetValue(id, out var found) ? found : NoReaders;
    }
}
