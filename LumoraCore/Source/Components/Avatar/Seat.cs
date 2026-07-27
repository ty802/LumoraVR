// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

public delegate void SeatUserEvent(Seat seat, User user);

public enum SeatRestoreMode
{
    None,
    // relative to the seat, so a moved seat carries the exit spot with it
    SeatRelative,
    // exact world pose, even if the seat has since moved
    WorldTransform,
    Reference,
}

// SYNC / AUTHORITY. The CLAIM is the parenting itself: a user sits by moving their own rig under the
// seat's space, which is a write to their own body and therefore something every peer is allowed to
// make. Nothing about sitting writes world content, so a seat still works in a Social/Event world
// where the authored scene is frozen for everyone. Occupancy is DERIVED from that parenting on every
// peer - no claim field to fall out of step with the actual state.
//
// The VERDICT is host-only, and it exists to settle CONFLICTS. The authority polls the derived set and
// stamps the winner into Occupant, bumping a counter each time it rules. A peer that sat and then sees
// the counter move with somebody ELSE named has lost the race and stands itself back up. Two users
// grabbing the same chair in the same frame therefore resolve to one, identically on every peer, and a
// client cannot cheat it because a client cannot write either field.
//
// The BODY only ever moves on its owner's peer. There is no forced remove: nobody may reparent another
// user's rig, so throwing an occupant out is not a thing this implements - their own side stands them
// up. Release() says so rather than pretending.
//
// Occupant, the ruling counter and the restore snapshot all stay out of saves: a world must not come
// back with somebody still sitting in it, and a restore pose captured in a previous session points at
// slots that no longer exist. - xlinka
[ComponentCategory("Users/Avatar")]
public class Seat : Component, ICustomInspectorUI
{
    // OCCUPANCY

    // written by the host only; read OccupyingUser for the live answer, derived from actual parenting
    [NonPersistent]
    public readonly SyncRef<User> Occupant;

    // bumped every time the authority rules; a peer that sat before the last bump and isn't named by
    // Occupant has lost the seat
    [NonPersistent]
    private readonly Sync<int> _verdict;

    // POSE

    // defaults to this slot
    public readonly SyncRef<Slot> SeatSpace;

    public readonly Sync<UserRoot.UserNode> PositionNode;

    // defaults to this slot
    public readonly SyncRef<Slot> PositionReference;

    public readonly Sync<UserRoot.UserNode> RotationNode;

    // defaults to this slot
    public readonly SyncRef<Slot> RotationReference;

    public readonly Sync<float> MinScale;

    public readonly Sync<float> MaxScale;

    // levels the rig's up to the seat space on entry, so a tilted seat doesn't tip the user
    public readonly Sync<bool> PreserveUpOnEnter;

    // levels the rig's up to the world on exit
    public readonly Sync<bool> PreserveUpOnExit;

    // RESTORE

    public readonly Sync<SeatRestoreMode> RestoreMode;

    public readonly Sync<UserRoot.UserNode> RestoreNode;

    // used by SeatRestoreMode.Reference
    public readonly SyncRef<Slot> RestoreReference;

    // FILTERS

    // ignored when no owner can be resolved
    public readonly Sync<bool> OwnerOnly;

    // empty means anyone (subject to OwnerOnly)
    public readonly SyncRefList<User> AllowedUsers;

    // EVENTS

    public readonly SyncDelegate<SeatUserEvent> SatAction;

    public readonly SyncDelegate<SeatUserEvent> StayAction;

    public readonly SyncDelegate<SeatUserEvent> ReleasedAction;

    // raised on the occupant's peer
    public event SeatUserEvent? Sat;

    // raised on the occupant's peer every update while seated
    public event SeatUserEvent? Stay;

    // raised on the occupant's peer
    public event SeatUserEvent? Released;

    // RESTORE SNAPSHOT

    [NonPersistent]
    private readonly SyncRef<Slot> _restoreSpace;

    [NonPersistent]
    private readonly Sync<float3> _restorePosition;

    [NonPersistent]
    private readonly Sync<floatQ> _restoreRotation;

    [NonPersistent]
    private readonly Sync<float> _restoreScale;

    public Seat()
    {
        Occupant = new SyncRef<User>(this);
        _verdict = new Sync<int>(this, 0);
        SeatSpace = new SyncRef<Slot>(this);
        PositionNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        PositionReference = new SyncRef<Slot>(this);
        RotationNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        RotationReference = new SyncRef<Slot>(this);
        MinScale = new Sync<float>(this, 1f);
        MaxScale = new Sync<float>(this, 1f);
        PreserveUpOnEnter = new Sync<bool>(this, false);
        PreserveUpOnExit = new Sync<bool>(this, true);

        RestoreMode = new Sync<SeatRestoreMode>(this, SeatRestoreMode.SeatRelative);
        RestoreNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        RestoreReference = new SyncRef<Slot>(this);

        OwnerOnly = new Sync<bool>(this, false);
        AllowedUsers = new SyncRefList<User>(this);

        SatAction = new SyncDelegate<SeatUserEvent>(this);
        StayAction = new SyncDelegate<SeatUserEvent>(this);
        ReleasedAction = new SyncDelegate<SeatUserEvent>(this);

        _restoreSpace = new SyncRef<Slot>(this);
        _restorePosition = new Sync<float3>(this, float3.Zero);
        _restoreRotation = new Sync<floatQ>(this, floatQ.Identity);
        _restoreScale = new Sync<float>(this, 1f);
    }

    // LOCAL STATE

    private bool _seatedLocally;
    private User _seatedUser = null!;
    private Slot _attachedSpace = null!;
    private int _verdictAtSit;
    private int _arbitrationCountdown;

    // The authority rules a few times a second rather than every frame: the derived scan is a walk of
    // the user list per seat, and a hundred-millisecond wait to learn you lost a race nobody else was
    // running is not something anyone can feel.
    private const int ArbitrationInterval = 6;

    public bool IsOccupied => FindSeatedUser() != null;

    // derived from the parenting
    public User? OccupyingUser => FindSeatedUser();

    public bool IsLocalUserSeated => _seatedLocally;

    // LIFECYCLE

    public override void OnAwake()
    {
        base.OnAwake();
        World?.RegisterEventReceiver(this);
    }

    public override void OnStart()
    {
        base.OnStart();

        // A seat that comes back from a save or a duplicate must come back empty. The fields are
        // non-persistent so this is belt-and-braces for a reference that survived some other route.
        if (World?.IsAuthority == true && Occupant.Target != null && !WorldStillHas(Occupant.Target))
            Rule(null);
    }

    public override void OnUserLeft(User user)
    {
        // The leaving peer can no longer vacate its own seat, so the host retires the ruling.
        if (World?.IsAuthority == true && user != null && Occupant.Target == user)
            Rule(null);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        CheckVerdict();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (World?.IsAuthority == true && --_arbitrationCountdown <= 0)
        {
            _arbitrationCountdown = ArbitrationInterval;
            Arbitrate();
        }

        if (!_seatedLocally)
            return;

        // The rig can be torn out from under a seat by an avatar swap or a disconnect. Notice rather
        // than firing Stay at a corpse.
        if (_seatedUser == null || _seatedUser.IsDestroyed || ResolveRoot(_seatedUser)?.Slot == null)
        {
            RestoreLocalUser();
            return;
        }

        CheckVerdict();
        if (!_seatedLocally)
            return;

        Stay?.Invoke(this, _seatedUser);
        StayAction.Target?.Invoke(this, _seatedUser);
    }

    // AUTHORITY

    // Poll who is actually parented in and stamp the winner. Ties go to whoever the user list hands
    // back first, which is arbitrary but CONSISTENT - every peer is told the same answer, which is the
    // only property that matters. - xlinka
    private void Arbitrate()
    {
        var seated = FindSeatedUser();

        if (seated == null)
        {
            if (Occupant.Target != null)
                Rule(null);
            return;
        }

        // The standing ruling still matches reality; leave it alone so the counter stays quiet.
        if (Occupant.Target == seated)
            return;

        Rule(seated);
    }

    private void Rule(User? winner)
    {
        if (winner == null)
            Occupant.Clear();
        else
            Occupant.Target = winner;
        _verdict.Value = unchecked(_verdict.Value + 1);
    }

    // A ruling landed after we sat down and it names somebody else: we lost the race, stand up.
    //
    // A ruling that names NOBODY is never treated as one. The host emits those as bookkeeping when a
    // seat empties out, and one can still be in flight from before we sat - acting on it would throw a
    // user out of a seat nobody else wants, and the next poll would just hand it straight back. Only a
    // ruling with a name on it is a decision about who won. - xlinka
    private void CheckVerdict()
    {
        if (!_seatedLocally)
            return;
        if (_verdict.Value == _verdictAtSit)
            return;

        var ruled = Occupant.Target;
        if (ruled == null || ruled.IsDestroyed || ruled == _seatedUser)
            return;

        RestoreLocalUser();
    }

    public override void OnDestroying()
    {
        // Children are destroyed before components, so a seat that goes down while occupied would take
        // the occupant's whole rig with it. Get them out first.
        if (_seatedLocally)
            RestoreLocalUser();
        base.OnDestroying();
    }

    public override void OnDestroy()
    {
        if (_seatedLocally)
            RestoreLocalUser();
        DetachSpaceWatch();
        World?.UnregisterEventReceiver(this);
        base.OnDestroy();
    }

    // API

    public bool CanSit(User user)
    {
        if (user == null || user.IsDestroyed || !Enabled.Value || IsDestroyed)
            return false;

        // Somebody is physically in it, or the host has ruled it belongs to somebody else. Either is a
        // no, and checking both means a client does not have to wait for a verdict to see an obviously
        // taken seat.
        var seated = FindSeatedUser();
        if (seated != null && seated != user)
            return false;
        if (Occupant.Target != null && Occupant.Target != user && !Occupant.Target.IsDestroyed)
            return false;

        var userRoot = ResolveRoot(user);
        if (userRoot?.Slot == null || userRoot.Slot.IsDestroyed)
            return false;

        // A seat carried on your own avatar would parent your rig into itself.
        var space = ResolveSpace();
        if (space == null || space.IsDestroyed || space.IsDescendantOf(userRoot.Slot))
            return false;

        if (AllowedUsers.Count > 0 && !AllowedUsers.Contains(user))
            return false;

        if (OwnerOnly.Value)
        {
            var owner = Slot?.ActiveUserRoot?.ActiveUser;
            if (owner != null && owner != user)
                return false;
        }

        return true;
    }

    // only the user's own peer can call this; moving somebody else's body is not a thing any other peer may do
    public bool TrySit(User user)
    {
        if (!CanSit(user))
            return false;
        if (user != World?.LocalUser)
            return false;
        if (_seatedLocally)
            return true;

        var userRoot = ResolveRoot(user)!;
        var rootSlot = userRoot.Slot;
        var space = ResolveSpace()!;

        CaptureRestore(userRoot);

        // Remember the ruling in force as we sit. Any LATER ruling that does not name us takes the
        // seat away again; the one standing now is not about us and must not.
        _verdictAtSit = _verdict.Value;

        // Reparent WITHOUT moving anything, then pose deliberately. Dropping the global transform on
        // the reparent would fling the rig to wherever its old local numbers happen to land in the new
        // space, for the one frame before the pose lands.
        rootSlot.SetParent(space, preserveGlobalTransform: true);
        _attachedSpace = space;
        space.OnPrepareDestroy += OnAttachedSpaceDestroying;

        ApplySeatedScale(rootSlot);

        var rotationTarget = RotationReference.Target ?? Slot;
        if (rotationTarget != null && !rotationTarget.IsDestroyed)
            userRoot.SetGlobalRotation(RotationNode.Value, rotationTarget.GlobalRotation);

        if (PreserveUpOnEnter.Value)
            LevelRoot(rootSlot, space.GlobalRotation * float3.Up);

        var positionTarget = PositionReference.Target ?? Slot;
        if (positionTarget != null && !positionTarget.IsDestroyed)
            userRoot.SetGlobalPosition(PositionNode.Value, positionTarget.GlobalPosition);

        SuspendLocomotion(userRoot, true);

        _seatedLocally = true;
        _seatedUser = user;

        // Rule immediately when we ARE the authority, so a single-peer world never waits on the poll.
        if (World?.IsAuthority == true)
        {
            Rule(user);
            _verdictAtSit = _verdict.Value;
        }

        Sat?.Invoke(this, user);
        SatAction.Target?.Invoke(this, user);
        return true;
    }

    // returns false when the occupant is somebody else; no peer can move another user's body
    public bool Release()
    {
        if (_seatedLocally)
        {
            var user = _seatedUser;
            RestoreLocalUser();
            if (World?.IsAuthority == true && Occupant.Target == user)
                Rule(null);
            return true;
        }

        // Somebody else's body, and no peer may move another user's rig. Their side stands them up:
        // SeatReleaseOnMove, a trigger press, or a script on their peer. Reporting that honestly beats
        // returning true and quietly doing nothing.
        return false;
    }

    // SEATING MECHANICS

    private void CaptureRestore(UserRoot userRoot)
    {
        var rootSlot = userRoot.Slot;
        _restoreSpace.Target = rootSlot.Parent;
        _restoreScale.Value = rootSlot.LocalScale.Value.x;

        var node = RestoreNode.Value;
        var position = userRoot.GetGlobalPosition(node);
        var rotation = userRoot.GetGlobalRotation(node);

        switch (RestoreMode.Value)
        {
            case SeatRestoreMode.SeatRelative:
                // Stored in the seat's frame so a seat that moves (a vehicle, a lift) carries the exit
                // spot with it instead of dumping the user back at a world coordinate the seat left.
                _restorePosition.Value = Slot.GlobalPointToLocal(position);
                _restoreRotation.Value = Slot.GlobalRotationToLocal(rotation);
                break;

            case SeatRestoreMode.WorldTransform:
                _restorePosition.Value = position;
                _restoreRotation.Value = rotation;
                break;

            default:
                _restorePosition.Value = position;
                _restoreRotation.Value = rotation;
                break;
        }
    }

    private void RestoreLocalUser()
    {
        if (!_seatedLocally)
            return;

        var user = _seatedUser;
        var userRoot = user != null ? ResolveRoot(user) : null;

        // Clear the flags first: the restore writes below can re-enter through OnChanges, and a second
        // pass would restore from an already-consumed snapshot.
        _seatedLocally = false;
        _seatedUser = null!;
        DetachSpaceWatch();

        var rootSlot = userRoot?.Slot;
        if (rootSlot != null && !rootSlot.IsDestroyed)
        {
            var target = _restoreSpace.Target;
            if (target == null || target.IsDestroyed)
                target = World?.RootSlot;
            if (target != null)
                rootSlot.SetParent(target, preserveGlobalTransform: true);

            ApplyRestorePose(userRoot!, rootSlot);

            if (PreserveUpOnExit.Value)
                LevelRoot(rootSlot, float3.Up);

            SuspendLocomotion(userRoot!, false);
        }

        _restoreSpace.Clear();

        // The ruling is the host's to retire: the derived scan will see the seat is empty on its next
        // pass and clear it. A client clearing it here would be a write it is not entitled to make.
        if (World?.IsAuthority == true && Occupant.Target == user)
            Rule(null);

        if (user != null)
        {
            Released?.Invoke(this, user);
            ReleasedAction.Target?.Invoke(this, user);
        }
    }

    private void ApplyRestorePose(UserRoot userRoot, Slot rootSlot)
    {
        var node = RestoreNode.Value;

        switch (RestoreMode.Value)
        {
            case SeatRestoreMode.None:
                break;

            case SeatRestoreMode.SeatRelative:
                if (Slot == null || Slot.IsDestroyed)
                    break;
                SetLocalScale(rootSlot, _restoreScale.Value);
                userRoot.SetGlobalRotation(node, Slot.LocalRotationToGlobal(_restoreRotation.Value));
                userRoot.SetGlobalPosition(node, Slot.LocalPointToGlobal(_restorePosition.Value));
                break;

            case SeatRestoreMode.WorldTransform:
                SetLocalScale(rootSlot, _restoreScale.Value);
                userRoot.SetGlobalRotation(node, _restoreRotation.Value);
                userRoot.SetGlobalPosition(node, _restorePosition.Value);
                break;

            case SeatRestoreMode.Reference:
                var reference = RestoreReference.Target;
                if (reference == null || reference.IsDestroyed)
                    break;
                SetLocalScale(rootSlot, _restoreScale.Value);
                userRoot.SetGlobalRotation(node, reference.GlobalRotation);
                userRoot.SetGlobalPosition(node, reference.GlobalPosition);
                break;
        }
    }

    private void ApplySeatedScale(Slot rootSlot)
    {
        float min = MathF.Min(MinScale.Value, MaxScale.Value);
        float max = MathF.Max(MinScale.Value, MaxScale.Value);
        if (max <= 0f)
            return;

        float current = rootSlot.LocalScale.Value.x;
        SetLocalScale(rootSlot, System.Math.Clamp(current, MathF.Max(min, 0.0001f), max));
    }

    private static void SetLocalScale(Slot rootSlot, float scale)
    {
        if (scale <= 0f || float.IsNaN(scale))
            return;
        rootSlot.LocalScale.Value = float3.One * scale;
    }

    // Locomotion goes quiet while seated: the character stops simulating (gravity would drag a rig
    // that is now parented to a moving seat) and every module bails on the shared suppression flag,
    // which is the same channel the context menu uses to borrow the sticks. Tracked head and hands
    // keep working - only locomotion INPUT is off, not the body. - xlinka
    private void SuspendLocomotion(UserRoot userRoot, bool suspend)
    {
        if (userRoot == null)
            return;

        var character = userRoot.Slot?.GetComponent<CharacterController>();
        character?.SetSimulationEnabled(!suspend);
        if (suspend)
            character?.SetMovementDirection(float3.Zero);

        var inputState = userRoot.Slot?.GetComponent<UserInputState>();
        inputState?.SetDesktopInputSuppressed(this, suspend);
    }

    // The rig hangs off the seat space, and Slot.Destroy takes children down before components, so a
    // space that is going away has to hand the occupant back BEFORE that happens or the user's whole
    // body is destroyed with the furniture. - xlinka
    private void OnAttachedSpaceDestroying(Slot slot)
    {
        if (_seatedLocally)
            RestoreLocalUser();
    }

    private void DetachSpaceWatch()
    {
        if (_attachedSpace != null)
        {
            _attachedSpace.OnPrepareDestroy -= OnAttachedSpaceDestroying;
            _attachedSpace = null!;
        }
    }

    // PERSISTENCE

    // The runtime state stays out of saves: a world must not come back with somebody still sitting in
    // it, and a restore pose captured last session points at slots that no longer exist. [NonPersistent]
    // on the fields above is enough on its own - the base ShouldSerializeMember excludes them. - xlinka

    // HELPERS

    // Occupancy read straight off the scene graph: a user is in this seat when their rig's PARENT is
    // the seat space, which is exactly the state TrySit establishes and Release undoes. Deriving it
    // instead of storing it means there is no claim field a client would need write access to, and no
    // way for the stored answer to disagree with where the body actually is. Direct parent, not
    // descendant, so a seat nested inside another seat's space does not report both as taken. -xlinka
    private User? FindSeatedUser()
    {
        var space = ResolveSpace();
        if (space == null || World == null)
            return null;

        foreach (var user in World.GetAllUsers())
        {
            if (user == null || user.IsDestroyed)
                continue;
            var rootSlot = ResolveRoot(user)?.Slot;
            if (rootSlot != null && !rootSlot.IsDestroyed && ReferenceEquals(rootSlot.Parent, space))
                return user;
        }
        return null;
    }

    private Slot? ResolveSpace()
    {
        var space = SeatSpace.Target;
        if (space != null && !space.IsDestroyed)
            return space;
        return Slot != null && !Slot.IsDestroyed ? Slot : null;
    }

    // On a client a remote user's Root backing field is never set - only the synced reference is - so
    // fall through to it rather than reporting the user has no body.
    private static UserRoot? ResolveRoot(User user)
    {
        if (user == null)
            return null;
        var root = user.Root;
        if (root != null && !root.IsDestroyed)
            return root;
        root = user.UserRootRef.Target;
        return root != null && !root.IsDestroyed ? root : null;
    }

    private bool WorldStillHas(User user)
    {
        if (user == null || user.IsDestroyed || World == null)
            return false;
        foreach (var candidate in World.GetAllUsers())
        {
            if (candidate == user)
                return true;
        }
        return false;
    }

    // Swing the rig's up onto `desiredUp` without touching its heading. Built as an axis-angle swing
    // rather than through floatQ.LookRotation, which assembles from basis ROWS and returns the inverse
    // rotation - using it here would leave a seated user lying on their side at oblique seat angles.
    // - xlinka
    private static void LevelRoot(Slot rootSlot, float3 desiredUp)
    {
        if (desiredUp.LengthSquared < 1e-6f)
            return;
        desiredUp = desiredUp.Normalized;

        var currentUp = rootSlot.GlobalRotation * float3.Up;
        if (currentUp.LengthSquared < 1e-6f)
            return;
        currentUp = currentUp.Normalized;

        float d = System.Math.Clamp(float3.Dot(currentUp, desiredUp), -1f, 1f);
        if (d > 0.99999f)
            return;

        floatQ swing;
        if (d < -0.99999f)
        {
            var fallback = float3.Cross(currentUp, float3.Right);
            if (fallback.LengthSquared < 1e-6f)
                fallback = float3.Cross(currentUp, float3.Forward);
            swing = floatQ.AxisAngleRad(fallback.Normalized, MathF.PI);
        }
        else
        {
            var axis = float3.Cross(currentUp, desiredUp);
            if (axis.LengthSquared < 1e-9f)
                return;
            swing = floatQ.AxisAngleRad(axis.Normalized, MathF.Acos(d));
        }

        rootSlot.GlobalRotation = (swing * rootSlot.GlobalRotation).Normalized;
    }

    // INSPECTOR

    public void BuildInspectorBody(UIBuilder ui)
    {
        var occupant = FindSeatedUser();
        var ruled = Occupant.Target;
        AddStatRow(ui, "Occupant", occupant?.UserName?.Value ?? "empty");
        AddStatRow(ui, "Host ruling", ruled == null || ruled.IsDestroyed ? "none" : ruled.UserName?.Value ?? "unnamed");
        AddStatRow(ui, "Local user", _seatedLocally ? "seated" : "not seated");
        AddStatRow(ui, "Restore", RestoreMode.Value.ToString());
        AddStatRow(ui, "Restore node", RestoreNode.Value.ToString());
        AddStatRow(ui, "Space", ResolveSpace()?.SlotName?.Value ?? "none");
        AddStatRow(ui, "Scale clamp", $"{MathF.Min(MinScale.Value, MaxScale.Value):0.##} - {MathF.Max(MinScale.Value, MaxScale.Value):0.##}");
        AddStatRow(ui, "Allow list", AllowedUsers.Count == 0 ? "anyone" : $"{AllowedUsers.Count} user(s)");
    }

    private static void AddStatRow(UIBuilder ui, string label, string value)
    {
        // Theme from the hosting panel's UI tree, NOT this component's world slot: a seat prop has no
        // UITheme above it, and text without a font renders nothing.
        InspectorUI.FixedRow(ui.Root, label, 24f, out var rowUi, ui.Root);
        rowUi.PushStyle();
        rowUi.MinWidth(150f);
        rowUi.PreferredWidth(190f);
        rowUi.FlexibleWidth(0f);
        var labelText = rowUi.Text(label, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(labelText.RectTransform!);
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
        rowUi.PushStyle();
        rowUi.FlexibleWidth(1f);
        var valueText = rowUi.Text(value, InspectorUI.FontSize - 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(valueText.RectTransform!);
        valueText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        valueText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
    }
}
