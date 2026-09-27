// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Scripting;
using Lumora.Script;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Interaction;

// Builds scripts in the world.
//
// The node browser has every node by folder and by search. Picking one readies it on the tool, and a
// double press on empty space puts it at the tool's tip (VR) or where the pointer is (desktop), in a group
// of its own that nothing shows of but its nodes. The browser stays open for the next one.
//
// On a card: press a port and drag onto another to wire them, or off into space to let go of an input's
// wire; press the header and drag to move the node. Wiring nodes of two groups puts the groups together,
// whichever tool placed them. Secondary while dragging a wire makes the node that wire wants where it
// ends, wired in: a Constant, a Display, a Call or a Pulse. Grab a node's body to carry it. Secondary on a
// node readies that node's type; secondary on empty space opens the browser on desktop, and in VR opens
// the menu, which has the browser on its ring.
//
// In VR a dragged wire's end is pulled onto the nearest port it fits within a few centimetres, through
// ScriptPortSnap, in its own graph or any other it could join. Two ports whose types differ offer the
// conversion nodes that fit between them. Pressing on a graph away from any card and swiping across wires
// cuts every wire the swipe crossed, in every graph it went over, when it lets go.
//
// Shader graphs are built the same way from shader nodes. The browser switches between the two lists; a
// material orb's graph opens from its menu; a shader node placed on empty space starts a loose group that
// joins a material's graph when it is wired into it.
//
// What the tool changes is the shared graph, gated like any other edit. When an edit is refused or two
// ports do not join, the reason floats by the laser for a moment. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class ScriptNodeTool : EquippableTool
{
    private const float NoticeSeconds = 2.5f;
    private const double DoublePressSeconds = 0.5;
    private const float TipReach = 0.075f;
    private const float GraphReach = 2.5f;

    // How close a wire end has to come to a port it fits before it is pulled on, in metres.
    public const float SnapReach = 0.035f;

    // How far a press on the graph has to move before it is a swipe that cuts, in metres.
    public const float CutStartDistance = 0.02f;

    // How far outside its cards a graph still takes a swipe, in metres.
    private const float GraphMargin = 0.15f;

    private ScriptCardHandle? _wireFrom;
    private ScriptGraphNode? _moving;
    private float3 _moveStart;
    private float3 _grabOffset;
    private ScriptGraphSpace? _lastSpace;
    private string? _readyType;
    private string? _readySignature;
    private GraphKind _readyKind;
    private double _lastEmptyPress = double.NegativeInfinity;
    private ScriptNodeBrowser? _browser;
    private Slot? _previewSlot;
    private ScriptWireMesh? _preview;
    private Slot? _noticeSlot;
    private TextRenderer? _notice;
    private double _noticeUntil;
    private Slot? _readySlot;
    private TextRenderer? _readyLabel;

    private ScriptGraphSpace? _cutSpace;
    private float3 _cutFrom;
    private float3 _cutTo;
    private bool _cutArmed;
    private bool _cutting;
    private readonly Dictionary<ScriptGraphSpace, float3> _cutStarts = new();
    private readonly List<ScriptWireKey> _cutWires = new();
    private readonly List<ScriptWireKey> _crossing = new();
    private readonly List<ScriptGraphSpace> _spaces = new();
    private readonly HashSet<ScriptGraphSpace> _marked = new();
    private Slot? _cutSlot;
    private ScriptWireMesh? _cutLine;

    private ScriptPortSnap? _snap;
    private Slot? _snapMarkSlot;
    private ScriptPulseMesh? _snapMark;

    protected override colorHDR VisualTint => new(0.86f, 0.52f, 0.25f, 1f);

    public string? ReadyType => _readyType;

    // Whether the readied node is a script node or a shader node: the two lists share names.
    public GraphKind ReadyKind => _readyKind;

    // The graph worked on last, while it is out: one joined into another is parked.
    public ScriptGraphSpace? CurrentSpace => _lastSpace is { IsDestroyed: false, Slot.IsActive: true } space ? space : null;

    // The port a dragged wire's end is pulled onto right now, if any.
    public (ScriptGraphNode Node, ScriptCardHandleKind Kind, string Port)? SnappedPort => _snap?.Snapped;

    // While a swipe is cutting: the wires it crosses so far.
    public IReadOnlyList<ScriptWireKey> CutWires => _cutWires;

    public bool IsCutting => _cutting;

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Script Node Tool";
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.011f, VisualTint);
    }

    public override bool OnPrimaryPress()
    {
        if (EditingBlocked)
            return false;

        var handle = HandleUnderLaser();
        if (handle?.Node is { IsDestroyed: false } node)
        {
            _lastSpace = node.Space ?? _lastSpace;
            if (handle.Kind == ScriptCardHandleKind.Header)
            {
                _moving = node;
                _moveStart = node.Slot.LocalPosition.Value;
                _grabOffset = RayOnGraph(node.Space, out var local) ? local - _moveStart : float3.Zero;
                return true;
            }
            _wireFrom = handle;
            BeginSnap(handle);
            return true;
        }

        // A node's body is for the hand to grab, and a panel's presses are the panel's; neither is empty
        // space.
        if (NodeUnderLaser() != null || ActiveTool?.Laser?.CurrentPointerTarget != null)
        {
            _lastEmptyPress = double.NegativeInfinity;
            return false;
        }

        // On a graph away from its cards: a swipe from here cuts, a press let go where it began is a tap.
        if (SpaceUnderRay(out var cutSpace, out var cutFrom) && ActiveTool?.Laser is { } laser)
        {
            _cutSpace = cutSpace;
            _cutFrom = _cutTo = cutFrom;
            BeginCut(cutSpace, cutFrom, laser);
            _cutArmed = true;
            _cutting = false;
            return true;
        }

        return _readyType != null && TapEmpty();
    }

    // The double press on empty space that puts the readied node there.
    private bool TapEmpty()
    {
        double now = World?.Time.TotalTime ?? 0.0;
        if (now - _lastEmptyPress <= DoublePressSeconds)
        {
            _lastEmptyPress = double.NegativeInfinity;
            PlaceReady(PlacementPoint());
            return true;
        }
        _lastEmptyPress = now;
        return false;
    }

    public override bool OnPrimaryHold()
    {
        if (_moving is { IsDestroyed: false } moving)
        {
            if (RayOnGraph(moving.Space, out var local))
            {
                var target = local - _grabOffset;
                target.z = 0f;
                if (moving.Slot.LocalPosition.Value != target)
                    Guarded(() => moving.Slot.LocalPosition.Value = target);
            }
            return true;
        }
        if (_wireFrom != null)
        {
            ShowPreview();
            return true;
        }
        if (_cutArmed)
        {
            UpdateCut();
            return true;
        }
        return false;
    }

    public override bool OnPrimaryRelease()
    {
        if (_moving != null)
        {
            var moving = _moving;
            _moving = null;
            if (!moving.IsDestroyed && moving.Slot.LocalPosition.Value != _moveStart)
                RecordUndo(new FieldEditUndoBatch(moving.Slot.LocalPosition, _moveStart, moving.Slot.LocalPosition.Value, ScriptGraphEditing.ValueStep));
            return true;
        }

        if (_cutArmed)
            return FinishCut();

        var from = _wireFrom;
        _wireFrom = null;
        var snapped = _snap?.SnappedHandle();
        EndSnap();
        HidePreview();
        if (from == null)
            return false;

        var to = snapped ?? HandleUnderLaser();
        if (to != null && to.IsPort && !ReferenceEquals(to, from))
            Join(from, to);
        else if (to == null)
            ScriptGraphEditing.Unwire(from);
        return true;
    }

    // Two ports joined as they are, or, when their types differ, the conversions that fit between them
    // offered on the menu first.
    private void Join(ScriptCardHandle from, ScriptCardHandle to)
    {
        var conversions = ScriptGraphEditing.ConversionsFor(from, to);
        if (conversions.Count > 0 && OfferConversions(from, to, conversions))
            return;
        var node = from.Node;
        if (!ScriptGraphEditing.Connect(from, to, out var error))
            Notice(error ?? "those do not join");
        else if (node?.Space is { } joined)
            _lastSpace = joined;
    }

    private bool OfferConversions(ScriptCardHandle from, ScriptCardHandle to, List<ScriptConversion> conversions)
    {
        var world = World;
        var menu = world?.LocalUser?.Root?.Slot?.GetComponentInChildren<ContextMenuSystem>();
        if (menu == null || menu.IsOpen.Value)
            return false;

        // The ends as they are now: the cards may be rebuilt before anything is picked.
        var output = from.Kind == ScriptCardHandleKind.DataOutput ? from : to;
        var input = ReferenceEquals(output, from) ? to : from;
        var producer = output.Node!;
        var consumer = input.Node!;
        string outputPort = output.Port;
        string inputPort = input.Port;

        var page = new ContextMenuPage("Convert to join");
        foreach (var conversion in conversions)
        {
            var picked = conversion;
            page.AddItem(new ContextMenuItem
            {
                Label = picked.Title,
                FillColor = ArmFill,
                OnPressed = _ =>
                {
                    menu.Close();
                    ScriptGraphNode? made = null;
                    string? error = null;
                    if (!Guarded(() => made = ScriptGraphEditing.ConnectThrough(producer, outputPort, consumer, inputPort, picked, out error))
                        || made == null || error != null)
                        Notice(error ?? "the conversion was not made");
                    else if (made.Space is { } joined)
                        _lastSpace = joined;
                },
            });
        }
        page.AddItem(new ContextMenuItem
        {
            Label = "Cancel",
            FillColor = ItemFill,
            OnPressed = _ => menu.Close(),
        });
        var laser = ActiveTool?.Laser;
        menu.OpenPage(page, new ContextMenuContext { Pointer = laser?.Slot ?? ActiveTool?.Slot, Side = ActiveTool?.Side.Value });
        return true;
    }

    public override bool OnSecondaryPress()
    {
        // Secondary with a wire in hand makes the node that wire wants where the wire ends, wired in.
        if (_wireFrom is { Node: { IsDestroyed: false } dragged } from)
        {
            _wireFrom = null;
            EndSnap();
            HidePreview();
            if (dragged.Space is { } space)
            {
                var local = space.Slot.GlobalPointToLocal(PlacementPoint());
                ScriptGraphNode? made = null;
                string? error = null;
                if (!Guarded(() => made = ScriptGraphEditing.AddHelper(from, local, out error)) || made == null)
                    Notice(error ?? "nothing was made");
            }
            return true;
        }

        var node = HandleUnderLaser()?.Node ?? NodeUnderLaser();
        if (node is { IsDestroyed: false })
        {
            Ready(node.NodeType.Value ?? string.Empty, node.Signature.Value, node.Space?.Kind.Value ?? GraphKind.Script);
            return true;
        }
        if (Engine.Current?.InputInterface?.IsVRActive == true)
            return false;
        ToggleBrowser();
        return true;
    }

    public override void OnDequipped()
    {
        _wireFrom = null;
        _moving = null;
        EndSnap();
        HidePreview();
        CancelCut();
        ShowReady(false);
        base.OnDequipped();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        double now = World?.Time.TotalTime ?? 0.0;
        if (_notice != null && now > _noticeUntil && _noticeSlot is { IsDestroyed: false } slot && slot.ActiveSelf.Value)
            slot.ActiveSelf.Value = false;
        if (_readyType != null && IsEquipped)
            ShowReady(true);
    }

    public override void OnDestroy()
    {
        EndSnap();
        foreach (var slot in new[] { _previewSlot, _noticeSlot, _readySlot, _cutSlot, _snapMarkSlot })
        {
            if (slot is { IsDestroyed: false })
                slot.Destroy();
        }
        if (_browser is { IsDestroyed: false } browser)
            browser.Slot.Destroy();
        base.OnDestroy();
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        page.AddItem(new ContextMenuItem
        {
            Label = _browser is { IsDestroyed: false } ? "Close node browser" : "Node browser",
            FillColor = ActiveFill,
            OnPressed = _ => ToggleBrowser(),
        });
        page.AddItem(new ContextMenuItem
        {
            Label = World != null && LoomDiagnostic.Find(World) != null ? "Close diagnostic" : "Diagnostic",
            FillColor = ItemFill,
            OnPressed = _ =>
            {
                if (World != null)
                    LoomDiagnostic.Toggle(World);
            },
        });
        if (_readyType != null)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Stop placing " + _readyType,
                FillColor = ItemFill,
                OnPressed = _ => Ready(null, null),
            });
        }

        var under = HandleUnderLaser()?.Node ?? NodeUnderLaser();
        var space = under?.Space ?? CurrentSpace;
        if (space is { IsDestroyed: false } && space.Kind.Value == GraphKind.Script)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Pack script",
                FillColor = ItemFill,
                OnPressed = _ => Pack(space),
            });
        }
        // A material's graph closes back onto its orb; the material keeps what it made.
        if (space is { IsDestroyed: false } && space.ShaderSource.Target is { IsDestroyed: false } material)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Close shader graph",
                FillColor = ItemFill,
                OnPressed = _ =>
                {
                    if (!ShaderGraphMaterials.SetOpen(material, false))
                        Notice("you may not close that graph");
                },
            });
        }
        if (under is { IsDestroyed: false } node)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Delete node",
                FillColor = ClearFill,
                OnPressed = _ =>
                {
                    if (!ScriptGraphEditing.DeleteNode(node, out var error))
                        Notice(error ?? "not deleted");
                },
            });
        }
        else if (ResolveObjectRoot(ActiveTool?.Laser?.CurrentHitSlot) is { } target && !IsOffLimits(target))
        {
            if (ShaderGraphMaterials.FindOn(target) is { } shader && !shader.IsGraphOpen)
            {
                page.AddItem(new ContextMenuItem
                {
                    Label = "Edit shader graph",
                    FillColor = ArmFill,
                    OnPressed = _ =>
                    {
                        if (ShaderGraphMaterials.SetOpen(shader, true))
                            _lastSpace = shader.Graph.Target;
                        else
                            Notice("you may not open that graph");
                    },
                });
            }
            page.AddItem(new ContextMenuItem
            {
                Label = target.GetComponent<ScriptHost>() != null ? "Edit script" : "New script here",
                FillColor = ArmFill,
                OnPressed = _ =>
                {
                    var opened = ScriptGraphEditing.EditScriptOn(target, out var error);
                    if (opened != null)
                        _lastSpace = opened;
                    else
                        Notice(error ?? "no script");
                },
            });
        }
    }

    // Readies a node type to place, or clears it with a null type.
    public void Ready(string? type, string? signature, GraphKind kind = GraphKind.Script)
    {
        _readyType = string.IsNullOrEmpty(type) ? null : type;
        _readySignature = string.IsNullOrEmpty(signature) ? null : signature;
        _readyKind = kind;
        _lastEmptyPress = double.NegativeInfinity;
        ShowReady(_readyType != null);
    }

    // The browser opens on the list for the graph worked on last, else for the node readied.
    public void ToggleBrowser()
    {
        if (_browser is { IsDestroyed: false } open)
        {
            open.Slot.Destroy();
            _browser = null;
            return;
        }
        if (World != null)
            _browser = ScriptNodeBrowser.Open(World, this, CurrentSpace?.Kind.Value ?? _readyKind);
    }

    // Puts the readied node at a point, in a group of its own there facing whoever placed it. Wiring it to a
    // node of any other group puts the two groups together.
    public ScriptGraphNode? PlaceReady(float3 point)
    {
        var world = World;
        if (world == null || _readyType == null)
            return null;
        if (EditingBlocked || SpawnBlocked)
        {
            Notice("building is not allowed here");
            return null;
        }

        var facing = world.LocalUser?.Root?.HeadPosition ?? point + float3.Backward;
        ScriptGraphNode? node = null;
        string? error = null;
        if (!Guarded(() => node = ScriptGraphEditing.PlaceNode(world, _readyType, _readySignature, point, facing, out error, _readyKind))
            || node == null)
        {
            Notice(error ?? "that node could not be added");
            return null;
        }
        _lastSpace = node.Space;
        return node;
    }

    // The tip in VR; on desktop, where the pointer meets the graph being worked on, else what it points at
    // within reach, else a metre out.
    private float3 PlacementPoint()
    {
        if (Engine.Current?.InputInterface?.IsVRActive == true)
            return Tip + TipForward * TipReach;
        var laser = ActiveTool?.Laser;
        if (laser == null)
            return Tip;
        if (CurrentSpace is { } space && RayOnGraph(space, out var onGraph)
            && (space.Slot.LocalPointToGlobal(onGraph) - laser.RayOrigin).Length <= GraphReach)
            return space.Slot.LocalPointToGlobal(onGraph);
        if (laser.HasAimPoint && (laser.AimPoint - laser.RayOrigin).Length <= GraphReach)
            return laser.AimPoint;
        return laser.RayOrigin + laser.RayDirection * 1f;
    }

    private void Pack(ScriptGraphSpace space)
    {
        var task = ScriptGraphEditing.PackAsync(space);
        task.ContinueWith(done =>
        {
            if (done.Result is string error)
                World?.RunSynchronously(() => Notice(error));
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private ScriptCardHandle? HandleUnderLaser()
    {
        var laser = ActiveTool?.Laser;
        if (laser?.CurrentTarget is not Canvas canvas)
            return null;
        // The element the beam presses, not the topmost rect: a port's label sits over its hit area.
        return canvas.TryHitTest(laser.RayOrigin, laser.RayDirection, out var hit) && hit.Interactable is Component pressed
            ? ScriptCardHandle.Find(pressed.Slot)
            : null;
    }

    private ScriptGraphNode? NodeUnderLaser()
    {
        for (var slot = ActiveTool?.Laser?.CurrentHitSlot; slot != null; slot = slot.Parent)
        {
            if (slot.GetComponent<ScriptGraphNode>() is { IsDestroyed: false } node)
                return node;
        }
        return null;
    }

    // Where the laser crosses a graph's plane, in the graph's own space.
    private bool RayOnGraph(ScriptGraphSpace? space, out float3 local)
    {
        local = default;
        var laser = ActiveTool?.Laser;
        return laser != null && OnPlane(space, laser.RayOrigin, laser.RayDirection, out local);
    }

    private static bool OnPlane(ScriptGraphSpace? space, float3 rayOrigin, float3 rayDirection, out float3 local)
    {
        local = default;
        if (space?.Slot == null)
            return false;
        var origin = space.Slot.GlobalPointToLocal(rayOrigin);
        var direction = space.Slot.GlobalDirectionToLocal(rayDirection);
        if (MathF.Abs(direction.z) < 1e-5f)
            return false;
        float t = -origin.z / direction.z;
        if (t < 0f)
            return false;
        local = origin + direction * t;
        local.z = 0f;
        return true;
    }

    private void ShowPreview()
    {
        var from = _wireFrom;
        var space = from?.Node?.Space;
        var view = space?.View;
        if (from?.Node == null || space == null || view == null || !view.TryPortPoint(from.Node, from.Kind, from.Port, out var portPoint))
            return;
        // Pulled onto a port in reach, else onto the port the pointer is on, which may be in another group
        // and off this graph's plane, else where the pointer meets the plane.
        bool onGraph = RayOnGraph(space, out var end);
        if (onGraph && Snap(space, end) is { } pulled)
            end = pulled;
        else if (PortUnderLaser(from) is { } port)
            end = space.Slot.GlobalPointToLocal(port);
        else if (!onGraph)
            return;

        if (_previewSlot == null || _previewSlot.IsDestroyed || !ReferenceEquals(_previewSlot.Parent, space.Slot))
        {
            if (_previewSlot is { IsDestroyed: false })
                _previewSlot.Destroy();
            _previewSlot = space.Slot.AddLocalSlot("Wire preview");
            _preview = _previewSlot.AttachComponent<ScriptWireMesh>();
            var material = _previewSlot.AttachComponent<UnlitMaterial>();
            material.UseVertexColor.Value = true;
            var renderer = _previewSlot.AttachComponent<MeshRenderer>();
            renderer.Mesh.Target = _preview;
            renderer.Material.Target = material;
            renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        }
        _previewSlot.ActiveSelf.Value = true;

        var start = space.Slot.GlobalPointToLocal(portPoint);
        var tint = from.Kind is ScriptCardHandleKind.FlowInput or ScriptCardHandleKind.FlowOutput
            ? ScriptEditorStyle.Flow
            : from.Kind == ScriptCardHandleKind.Variable ? DashTheme.Warning : ScriptEditorStyle.Type(PortType(from));
        bool leavesRight = from.Kind is ScriptCardHandleKind.DataOutput or ScriptCardHandleKind.FlowOutput;
        _preview!.SetWires(new[] { leavesRight ? new ScriptWireCurve(start, end, tint, tint) : new ScriptWireCurve(end, start, tint, tint) });
    }

    private void HidePreview()
    {
        if (_previewSlot is { IsDestroyed: false })
            _previewSlot.ActiveSelf.Value = false;
    }

    // Where the port the pointer is on meets its wires, when it is a port of another node.
    private float3? PortUnderLaser(ScriptCardHandle from)
    {
        if (HandleUnderLaser() is not { IsPort: true, Node: { IsDestroyed: false } node } handle || ReferenceEquals(node, from.Node))
            return null;
        return node.Space?.View is { } view && view.TryPortPoint(node, handle.Kind, handle.Port, out var point) ? point : null;
    }

    // SNAPPING, in VR only: small ports are hard to land on with a hand at arm's length, and a desktop
    // pointer lands where it is put.
    private void BeginSnap(ScriptCardHandle from)
    {
        EndSnap();
        if (Engine.Current?.InputInterface?.IsVRActive == true)
            _snap = ScriptPortSnap.Begin(from, SnapReach);
    }

    // The point in the graph a loose end at end is pulled onto, or null when no port it fits is in reach. A
    // port of another group may sit off this graph's plane, and the end goes to it there.
    private float3? Snap(ScriptGraphSpace space, float3 end)
    {
        if (_snap?.Pull(space.Slot.LocalPointToGlobal(end)) is not { } port)
        {
            ShowSnapMark(space, null);
            return null;
        }
        var pulled = space.Slot.GlobalPointToLocal(port);
        ShowSnapMark(space, pulled);
        return pulled;
    }

    private void EndSnap()
    {
        _snap?.Dispose();
        _snap = null;
        if (_snapMarkSlot is { IsDestroyed: false } mark && mark.ActiveSelf.Value)
            mark.ActiveSelf.Value = false;
    }

    // A bright bead on the port the end is pulled onto.
    private void ShowSnapMark(ScriptGraphSpace space, float3? at)
    {
        if (at == null)
        {
            if (_snapMarkSlot is { IsDestroyed: false } hidden && hidden.ActiveSelf.Value)
                hidden.ActiveSelf.Value = false;
            return;
        }
        if (_snapMarkSlot == null || _snapMarkSlot.IsDestroyed || !ReferenceEquals(_snapMarkSlot.Parent, space.Slot))
        {
            if (_snapMarkSlot is { IsDestroyed: false } old)
                old.Destroy();
            _snapMarkSlot = space.Slot.AddLocalSlot("Snap mark");
            _snapMark = _snapMarkSlot.AttachComponent<ScriptPulseMesh>();
            ScriptGraphView.Draw(_snapMarkSlot, _snapMark);
        }
        _snapMarkSlot.ActiveSelf.Value = true;
        _snapMark!.SetBeads(new[] { (at.Value, ScriptWireMesh.Radius * 3.5f, ScriptEditorStyle.Snap) });
    }

    // CUTTING. A press on a graph away from its cards arms a cut; once the pointer has moved a little it is
    // a swipe, drawn as a red line, and every wire it crosses goes red with it, in that graph and in any
    // other the swipe goes over. Letting go cuts them all as one undo step; letting go before it moved is a
    // tap.
    private void UpdateCut()
    {
        var space = _cutSpace;
        if (space == null || space.IsDestroyed || !RayOnGraph(space, out var local))
            return;
        _cutTo = local;
        if (!_cutting)
        {
            float moved = (space.Slot.LocalPointToGlobal(local) - space.Slot.LocalPointToGlobal(_cutFrom)).Length;
            if (moved < CutStartDistance)
                return;
            _cutting = true;
            _lastEmptyPress = double.NegativeInfinity;
        }
        FindCut();
        ShowCutLine(space);
    }

    // Where the swipe began on every graph whose plane the press met within reach, in each graph's own space,
    // so each can judge the swipe on its own plane.
    private void BeginCut(ScriptGraphSpace space, float3 from, InteractionLaser laser)
    {
        _cutStarts.Clear();
        ScriptGraphSpace.GetAll(World, _spaces);
        foreach (var other in _spaces)
        {
            if (other.Slot?.IsActive == true && RayOnGraph(other, out var start)
                && (other.Slot.LocalPointToGlobal(start) - laser.RayOrigin).Length <= GraphReach)
                _cutStarts[other] = start;
        }
        _cutStarts[space] = from;
    }

    // Every wire the swipe crosses so far, in every graph it began over: from where it began on that graph's
    // plane to where the pointer meets the plane now.
    private void FindCut()
    {
        _cutWires.Clear();
        var laser = ActiveTool?.Laser;
        if (laser == null)
            return;
        foreach (var (space, from) in _cutStarts)
        {
            if (space.IsDestroyed || space.View is not { } view)
                continue;
            _crossing.Clear();
            if (space.Slot?.IsActive == true && RayOnGraph(space, out var to)
                && (space.Slot.LocalPointToGlobal(to) - laser.RayOrigin).Length <= GraphReach)
                view.WiresCrossing(from, to, _crossing);
            if (_crossing.Count > 0)
            {
                _cutWires.AddRange(_crossing);
                view.SetCutMarks(_crossing);
                _marked.Add(space);
            }
            else if (_marked.Remove(space))
            {
                view.SetCutMarks(null);
            }
        }
    }

    private bool FinishCut()
    {
        var space = _cutSpace;
        bool cutting = _cutting;
        if (cutting && space is { IsDestroyed: false })
            FindCut();
        var wires = new List<ScriptWireKey>(_cutWires);
        CancelCut();
        _cutWires.Clear();
        if (!cutting)
        {
            if (_readyType != null)
                TapEmpty();
            return true;
        }
        if (space != null && !space.IsDestroyed && wires.Count > 0)
        {
            string? error = null;
            Guarded(() => ScriptGraphEditing.CutWires(space, wires, out error));
            if (error != null)
                Notice(error);
        }
        return true;
    }

    private void CancelCut()
    {
        foreach (var marked in _marked)
        {
            if (!marked.IsDestroyed)
                marked.View?.SetCutMarks(null);
        }
        _marked.Clear();
        _cutStarts.Clear();
        _cutArmed = false;
        _cutting = false;
        _cutSpace = null;
        if (_cutSlot is { IsDestroyed: false } slot && slot.ActiveSelf.Value)
            slot.ActiveSelf.Value = false;
    }

    private void ShowCutLine(ScriptGraphSpace space)
    {
        if (_cutSlot == null || _cutSlot.IsDestroyed || !ReferenceEquals(_cutSlot.Parent, space.Slot))
        {
            if (_cutSlot is { IsDestroyed: false } old)
                old.Destroy();
            _cutSlot = space.Slot.AddLocalSlot("Cut line");
            _cutLine = _cutSlot.AttachComponent<ScriptWireMesh>();
            ScriptGraphView.Draw(_cutSlot, _cutLine);
        }
        _cutSlot.ActiveSelf.Value = true;
        _cutLine!.SetWires(new[] { new ScriptWireCurve(_cutFrom, _cutTo, ScriptEditorStyle.Cut, ScriptEditorStyle.Cut, Straight: true) });
    }

    // The graph the laser meets away from any card, near enough to its cards to be working on it, and
    // where, in that graph's own space: any graph in the world, the one worked on last asked first.
    private bool SpaceUnderRay(out ScriptGraphSpace space, out float3 local)
    {
        space = null!;
        local = default;
        var laser = ActiveTool?.Laser;
        var world = World;
        if (laser == null || world == null)
            return false;
        float best = GraphReach;
        var current = CurrentSpace;
        if (current != null)
            Consider(current, laser, ref space, ref local, ref best);
        ScriptGraphSpace.GetAll(world, _spaces);
        foreach (var candidate in _spaces)
        {
            if (!ReferenceEquals(candidate, current))
                Consider(candidate, laser, ref space, ref local, ref best);
        }
        return space != null;
    }

    private void Consider(ScriptGraphSpace candidate, InteractionLaser laser, ref ScriptGraphSpace space, ref float3 local, ref float best)
    {
        if (candidate.IsDestroyed || candidate.Slot?.IsActive != true || !RayOnGraph(candidate, out var point))
            return;
        float distance = (candidate.Slot.LocalPointToGlobal(point) - laser.RayOrigin).Length;
        if (distance > best || !NearCards(candidate, point))
            return;
        best = distance;
        space = candidate;
        local = point;
    }

    // Within the margin around the graph's cards, in the graph's own space.
    private static bool NearCards(ScriptGraphSpace space, float3 point)
    {
        float margin = space.Slot.GlobalScaleToLocal(GraphMargin);
        foreach (var node in space.Nodes)
        {
            // One in a hand has its place in the hand.
            if (!ReferenceEquals(node.Slot.Parent, space.Slot))
                continue;
            var at = node.Slot.LocalPosition.Value;
            var layout = space.View?.CardOf(node)?.Layout ?? ScriptCardLayout.For(node, 0);
            float width = layout.Width * ScriptEditorStyle.CardScale;
            float height = layout.Height * ScriptEditorStyle.CardScale;
            if (point.x >= at.x - margin && point.x <= at.x + width + margin && point.y <= at.y + margin && point.y >= at.y - height - margin)
                return true;
        }
        return false;
    }

    private static ScriptType? PortType(ScriptCardHandle handle)
    {
        var overload = handle.Node?.Overload;
        if (overload == null)
            return null;
        if (handle.Kind == ScriptCardHandleKind.DataInput)
        {
            int index = overload.InputIndex(handle.Port);
            return index >= 0 ? overload.Inputs[index].Type : null;
        }
        int output = overload.OutputIndex(handle.Port);
        return output >= 0 ? overload.Outputs[output].Type : null;
    }

    // The readied node's name, floating just above the tip.
    private void ShowReady(bool show)
    {
        var world = World;
        if (world == null)
            return;
        if (!show)
        {
            if (_readySlot is { IsDestroyed: false } hidden && hidden.ActiveSelf.Value)
                hidden.ActiveSelf.Value = false;
            return;
        }
        if (_readySlot == null || _readySlot.IsDestroyed)
        {
            _readySlot = world.RootSlot.AddLocalSlot("Script tool ready node");
            _readyLabel = MakeLabel(world, _readySlot, DashTheme.Text);
        }
        if (!_readySlot.ActiveSelf.Value)
            _readySlot.ActiveSelf.Value = true;
        string name = GraphLanguage.For(_readyKind).Registry.TryGetDefinition(_readyType!, out var definition)
            ? definition.Path[(definition.Path.LastIndexOf('/') + 1)..]
            : _readyType!;
        string text = _readySignature == null ? name : $"{name} ({_readySignature})";
        if (_readyLabel!.Text.Value != text)
            _readyLabel.Text.Value = text;
        Face(_readySlot, Tip + new float3(0f, 0.035f, 0f));
    }

    private void Notice(string text)
    {
        LumoraLogger.Log($"Script Node Tool: {text}");
        var world = World;
        var laser = ActiveTool?.Laser;
        if (world == null || laser == null)
            return;

        if (_noticeSlot == null || _noticeSlot.IsDestroyed)
        {
            _noticeSlot = world.RootSlot.AddLocalSlot("Script tool notice");
            _notice = MakeLabel(world, _noticeSlot, DashTheme.Warning);
        }
        var at = laser.HasAimPoint ? laser.AimPoint : laser.RayOrigin + laser.RayDirection * 0.5f;
        Face(_noticeSlot, at + new float3(0f, 0.04f, 0f));
        _noticeSlot.ActiveSelf.Value = true;
        _notice!.Text.Value = text;
        _noticeUntil = world.Time.TotalTime + NoticeSeconds;
    }

    private static TextRenderer MakeLabel(World world, Slot slot, color tint)
    {
        var label = slot.AttachComponent<TextRenderer>();
        var font = slot.AddSlot("Font").AttachComponent<FontProvider>();
        font.URL.Value = ImportDialog.ResolveFontUrl(world) ?? DashTheme.FontRegular;
        label.Font.Target = font;
        label.Size.Value = 0.018f;
        label.Color.Value = tint;
        label.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        label.OutlineThickness.Value = 2f;
        label.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 1f);
        return label;
    }

    private void Face(Slot slot, float3 at)
    {
        slot.GlobalPosition = at;
        var head = World?.LocalUser?.Root?.HeadPosition;
        if (head == null)
            return;
        var toward = head.Value - at;
        toward.y = 0f;
        if (toward.LengthSquared > 1e-6f)
            slot.GlobalRotation = floatQ.LookRotation(toward.Normalized, float3.Up).Inverse;
    }
}
