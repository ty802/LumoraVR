// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Input;
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
public class Seat : Component, ICustomInspectorUI, IAvatarAnchor, IPoseFilter
{
    // OCCUPANCY

    // written by the host only; read OccupyingUser for the live answer, derived from actual parenting.
    // A copy of a seat starts empty, as does everything below about who sits in it.
    [NonPersistent, DontCopy]
    public readonly SyncRef<User> Occupant;

    // bumped every time the authority rules; a peer that sat before the last bump and isn't named by
    // Occupant has lost the seat
    [NonPersistent, DontCopy]
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

    // Turns the rotation and restore references before the user is turned to them. A user faces a
    // reference's forward (-Z); a seat authored to face its +Z carries a half turn here.
    public readonly Sync<floatQ> RotationOffset;

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

    // EXIT

    // On by default because sitting SUPPRESSES locomotion, so a seat with no way out is a trap: the
    // user cannot walk, cannot jump, and unless the builder happened to bolt a SeatReleaseOnMove onto
    // the same slot there is no input left that means "stand up". Jump is the one control everybody
    // already reaches for. Builders who want a ride nobody can leave mid-flight turn it off. -xlinka
    public readonly Sync<bool> ReleaseOnJump;

    // BODY POSES

    // The body parts this seat holds while somebody sits, each with the filters it lays on that part's socket:
    // a bench puts the hips and both feet on its own references, a footrest only the feet.
    public readonly SyncList<SeatBodyPose> BodyPoses;

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
    //
    // Where the sitter stood before sitting, kept on the sitter's machine alone: that machine takes it at sit
    // and is the only one that ever reads it, at stand-up. It used to be synced fields on the seat, which a
    // guest may not write on a seat the host owns, so a guest's snapshot was refused and a seat-relative
    // restore put them back from the defaults. Not synced, not saved, not copied: plain fields. The pose is
    // kept both in the seat's frame and in the world, so a seat destroyed with somebody in it still has
    // somewhere to put them. -xlinka
    private Slot? _restoreSpace;
    private float3 _restorePosition;
    private floatQ _restoreRotation = floatQ.Identity;
    private float3 _restoreWorldPosition;
    private floatQ _restoreWorldRotation = floatQ.Identity;
    private float _restoreScale = 1f;

    public Seat()
    {
        Occupant = new SyncRef<User>(this);
        _verdict = new Sync<int>(this, 0);
        SeatSpace = new SyncRef<Slot>(this);
        PositionNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        PositionReference = new SyncRef<Slot>(this);
        RotationNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        RotationReference = new SyncRef<Slot>(this);
        RotationOffset = new Sync<floatQ>(this, floatQ.Identity);
        MinScale = new Sync<float>(this, 1f);
        MaxScale = new Sync<float>(this, 1f);
        PreserveUpOnEnter = new Sync<bool>(this, false);
        PreserveUpOnExit = new Sync<bool>(this, true);

        RestoreMode = new Sync<SeatRestoreMode>(this, SeatRestoreMode.SeatRelative);
        RestoreNode = new Sync<UserRoot.UserNode>(this, UserRoot.UserNode.Root);
        RestoreReference = new SyncRef<Slot>(this);
        ReleaseOnJump = new Sync<bool>(this, true);
        BodyPoses = new SyncList<SeatBodyPose>();

        OwnerOnly = new Sync<bool>(this, false);
        AllowedUsers = new SyncRefList<User>(this);

        SatAction = new SyncDelegate<SeatUserEvent>(this);
        StayAction = new SyncDelegate<SeatUserEvent>(this);
        ReleasedAction = new SyncDelegate<SeatUserEvent>(this);
    }

    // LOCAL STATE

    private bool _seatedLocally;
    private User _seatedUser = null!;
    private Slot _attachedSpace = null!;
    private int _verdictAtSit;
    private int _arbitrationCountdown;
    private float _jumpGrace;

    // The jump that walked you into the seat is usually still held on the frame you land in it, so the
    // first half second of a sit ignores the button. Same window SeatReleaseOnMove uses.
    private const float JumpGracePeriod = 0.5f;

    // The authority rules a few times a second rather than every frame: the derived scan is a walk of
    // the user list per seat, and a hundred-millisecond wait to learn you lost a race nobody else was
    // running is not something anyone can feel.
    private const int ArbitrationInterval = 6;

    public bool IsOccupied => FindSeatedUser() != null;

    // IAvatarAnchor. A chair is one kind of anchor; see IAvatarAnchor for why the others are not
    // chairs. Everything here forwards to what the seat already did - the contract exists so that
    // tools, menus and worlds can talk to a bed or a vehicle the same way. -xlinka
    Component IAvatarAnchor.Component => this;

    public User? AnchoredUser => FindSeatedUser() ?? Occupant.Target;

    // Local and cosmetic: a highlight is about where one person is pointing.
    public bool Highlight { get; set; }

    bool IAvatarAnchor.CanAnchor(User user) => CanSit(user);

    bool IAvatarAnchor.Anchor(User user) => TrySit(user);

    // derived from the parenting
    public User? OccupyingUser => FindSeatedUser();

    public bool IsLocalUserSeated => _seatedLocally;

    // LIFECYCLE

    public override void OnAwake()
    {
        base.OnAwake();
        World?.RegisterEventReceiver(this);
        SeatIndex.Add(this);
    }

    public override void OnStart()
    {
        base.OnStart();

        // A seat that comes back from a save or a duplicate must come back empty. The fields are
        // non-persistent so this is belt-and-braces for a reference that survived some other route.
        if (World?.IsAuthority == true && Occupant.Target != null && !WorldStillHas(Occupant.Target))
            Rule(null);

        WatchSpace();
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

        // A new space, or a change to the poses while somebody sits: lay them again from what is there now.
        WatchSpace();
        if (_posedRoot != null)
        {
            LiftPoses();
            LayPoses();
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (World?.IsAuthority == true && --_arbitrationCountdown <= 0)
        {
            _arbitrationCountdown = ArbitrationInterval;
            Arbitrate();
        }

        MaintainPoses();

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

        if (CheckJumpRelease(delta))
            return;

        Stay?.Invoke(this, _seatedUser);
        StayAction.Target?.Invoke(this, _seatedUser);
    }

    // Reads the raw action rather than a locomotion module, for the same reason SeatReleaseOnMove does:
    // sitting mutes the locomotion set, so anything routed through the movement path would be listening
    // to a channel this seat already silenced and nobody would ever get up. Returns true when it let go,
    // so the caller stops before firing Stay at a user who is already standing.
    //
    // SeatReleaseOnMove on the same slot is not a conflict: whichever fires first releases, and the
    // second Release lands on an empty seat and returns early.
    private bool CheckJumpRelease(float delta)
    {
        if (!ReleaseOnJump.Value)
            return false;

        if (_jumpGrace > 0f)
        {
            _jumpGrace -= delta;
            return false;
        }

        if (!LocomotionInputHelper.ReadJump(Engine.Current?.InputInterface!))
            return false;

        Release();
        return true;
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
        LiftPoses();
        _posedRoot = null;
        UnwatchSpace();
        World?.UnregisterEventReceiver(this);
        SeatIndex.Remove(World, this);
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

        // Anything else on this slot with a say. The two rules above are the chair's own and they
        // stay; this is for the ones that are not a chair's business - a door being open, a group,
        // a ticket. Filters can only refuse, never grant. -xlinka
        return AnchorFilters.Allow(Slot, user);
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
            userRoot.SetGlobalRotation(RotationNode.Value, (rotationTarget.GlobalRotation * RotationOffset.Value).Normalized);

        if (PreserveUpOnEnter.Value)
            LevelRoot(rootSlot, space.GlobalRotation * float3.Up);

        var positionTarget = PositionReference.Target ?? Slot;
        if (positionTarget != null && !positionTarget.IsDestroyed)
            userRoot.SetGlobalPosition(PositionNode.Value, positionTarget.GlobalPosition);

        SuspendLocomotion(userRoot, true);
        AdoptRootYaw(userRoot);
        PlaceStandIns(userRoot);
        RefreshPosedUser();
        LayPoses();

        _seatedLocally = true;
        _seatedUser = user;
        _jumpGrace = JumpGracePeriod;

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
        _restoreSpace = rootSlot.Parent;
        _restoreScale = rootSlot.LocalScale.Value.x;

        var node = RestoreNode.Value;
        var position = userRoot.GetGlobalPosition(node);
        var rotation = userRoot.GetGlobalRotation(node);
        _restoreWorldPosition = position;
        _restoreWorldRotation = rotation;

        // Seat-relative is kept in the seat's frame so a seat that moves (a vehicle, a lift) carries the exit
        // spot with it instead of dumping the user back at a world coordinate the seat left.
        _restorePosition = Slot.GlobalPointToLocal(position);
        _restoreRotation = Slot.GlobalRotationToLocal(rotation);
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
            var target = _restoreSpace;
            if (target == null || target.IsDestroyed)
                target = World?.RootSlot;
            if (target != null)
                rootSlot.SetParent(target, preserveGlobalTransform: true);

            ApplyRestorePose(userRoot!, rootSlot);

            if (PreserveUpOnExit.Value)
                LevelRoot(rootSlot, float3.Up);

            SuspendLocomotion(userRoot!, false);
            AdoptRootYaw(userRoot!);
        }

        RemoveStandIns();
        RefreshPosedUser();
        _restoreSpace = null;

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
                SetLocalScale(rootSlot, _restoreScale);
                // A seat on its way out may have no frame left to read; the world pose taken with it stands in.
                if (Slot == null || Slot.IsDestroyed)
                {
                    userRoot.SetGlobalRotation(node, _restoreWorldRotation);
                    userRoot.SetGlobalPosition(node, _restoreWorldPosition);
                    break;
                }
                userRoot.SetGlobalRotation(node, Slot.LocalRotationToGlobal(_restoreRotation));
                userRoot.SetGlobalPosition(node, Slot.LocalPointToGlobal(_restorePosition));
                break;

            case SeatRestoreMode.WorldTransform:
                SetLocalScale(rootSlot, _restoreScale);
                userRoot.SetGlobalRotation(node, _restoreWorldRotation);
                userRoot.SetGlobalPosition(node, _restoreWorldPosition);
                break;

            case SeatRestoreMode.Reference:
                var reference = RestoreReference.Target;
                if (reference == null || reference.IsDestroyed)
                    break;
                SetLocalScale(rootSlot, _restoreScale);
                userRoot.SetGlobalRotation(node, (reference.GlobalRotation * RotationOffset.Value).Normalized);
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

    // MOVEMENT goes quiet while seated, and nothing else. The character stops simulating (gravity would
    // drag a rig that is now parented to a moving seat) and the locomotion modules park their translation
    // on the movement flag.
    //
    // This used to raise the whole desktop suppression flag, which is what a modal dialog raises when it
    // wants the mouse and the keyboard to itself. Sitting down took mouse look, snap turn and blink with
    // it, so a seated user could not even look around - and with locomotion muted, jump was the only way
    // out of the chair. The narrow flag stops you walking away and leaves looking, turning and aiming
    // exactly as they were. -xlinka
    private void SuspendLocomotion(UserRoot userRoot, bool suspend)
    {
        if (userRoot == null)
            return;

        var character = userRoot.Slot?.GetComponent<CharacterController>();
        character?.SetSimulationEnabled(!suspend);
        if (suspend)
            character?.SetMovementDirection(float3.Zero);

        var inputState = userRoot.Slot?.GetComponent<UserInputState>();
        inputState?.SetMovementSuppressed(this, suspend);
    }

    // The seat poses the rig once, on sit, and once more on release - it does NOT re-pin per frame, which
    // is what lets a turn made while seated stay made. Desktop look is the catch: it rewrites the root's
    // yaw from its own accumulator every frame, so the pose has to be handed to that accumulator or it is
    // gone by the next update. -xlinka
    private static void AdoptRootYaw(UserRoot userRoot)
    {
        userRoot?.Slot?.GetComponent<LocomotionController>()?.SyncYawFromRoot();
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

    // BODY POSES ON EVERY MACHINE
    //
    // Who sits is read off the scene graph everywhere, so every machine lays this seat's filters on the
    // sitter's sockets itself, from the same state, and takes them off again when the sitter leaves: the
    // filtered pose of every part comes out the same on every machine and nothing about it travels. The
    // space's child events say when somebody arrives or leaves; a part whose socket has not turned up yet (a
    // stand-in still on its way from the sitter's machine) is looked for again each frame until it has, which
    // is only ever while somebody sits. -xlinka
    private const int ReferenceFacingPriority = int.MinValue + 1;
    private UserRoot? _posedRoot;
    private readonly List<(AvatarSocket Socket, IPoseFilter Filter)> _laidFilters = new();
    private readonly List<int> _posedParts = new();
    private bool _posesPending;
    private Slot? _watchedSpace;
    private readonly List<AvatarSocket> _socketScratch = new();

    // The sitter's machine only: one slot of its own under the sitter's root holding the stand-ins and the
    // quiet. The root itself is the host's, so nothing the seat adds may sit on it directly or it could never
    // be taken off again.
    private Slot? _seatedParts;
    private bool _occupancyDirty;

    private void WatchSpace()
    {
        var space = IsDestroyed ? null : ResolveSpace();
        if (ReferenceEquals(space, _watchedSpace))
            return;
        UnwatchSpace();
        _watchedSpace = space;
        if (space != null)
        {
            space.OnChildAdded += OnSpaceChildAdded;
            space.OnChildRemoved += OnSpaceChildRemoved;
        }
        RefreshPosedUser();
    }

    private void UnwatchSpace()
    {
        if (_watchedSpace == null)
            return;
        _watchedSpace.OnChildAdded -= OnSpaceChildAdded;
        _watchedSpace.OnChildRemoved -= OnSpaceChildRemoved;
        _watchedSpace = null;
    }

    // Both fire while the child is still on its way in or out, before its parent reads the new value, so the
    // look happens on the next update.
    private void OnSpaceChildAdded(Slot space, Slot child)
    {
        if (child != null && child.GetComponent<UserRoot>() != null)
            _occupancyDirty = true;
    }

    private void OnSpaceChildRemoved(Slot space, Slot child)
    {
        if (_posedRoot != null && ReferenceEquals(_posedRoot.Slot, child))
            _occupancyDirty = true;
    }

    private void RefreshPosedUser()
    {
        var user = IsDestroyed ? null : FindSeatedUser();
        var root = user != null ? ResolveRoot(user) : null;
        if (ReferenceEquals(root, _posedRoot))
            return;
        LiftPoses();
        _posedRoot = root;
        LayPoses();
    }

    private void MaintainPoses()
    {
        if (_occupancyDirty)
        {
            _occupancyDirty = false;
            RefreshPosedUser();
        }
        var root = _posedRoot;
        if (root == null)
            return;
        if (root.IsDestroyed || root.Slot == null || !ReferenceEquals(root.Slot.Parent, _watchedSpace))
        {
            RefreshPosedUser();
            return;
        }
        for (int i = 0; i < _laidFilters.Count; i++)
        {
            if (_laidFilters[i].Socket.IsDestroyed)
            {
                LiftPoses();
                break;
            }
        }
        if (_posesPending)
            LayPoses();
    }

    private void LayPoses()
    {
        var root = _posedRoot;
        if (root == null || root.IsDestroyed)
        {
            _posesPending = false;
            return;
        }
        bool missing = false;
        _socketScratch.Clear();
        root.GetRegisteredComponents(_socketScratch);
        for (int i = 0; i < BodyPoses.Count; i++)
        {
            if (_posedParts.Contains(i))
                continue;
            var pose = BodyPoses[i];
            var socket = pose != null ? FindSocket(pose.Node.Value) : null;
            if (socket == null)
            {
                missing |= pose != null;
                continue;
            }
            for (int k = 0; k < pose!.Filters.Count; k++)
            {
                var entry = pose.Filters[k];
                if (entry?.Filter.Target is { } filter && !ReferenceEquals(filter, this))
                    Lay(socket, filter, entry.Priority.Value);
            }
            Lay(socket, this, ReferenceFacingPriority);
            _posedParts.Add(i);
        }
        _socketScratch.Clear();
        _posesPending = missing;
    }

    private void Lay(AvatarSocket socket, IPoseFilter filter, int priority)
    {
        socket.AddFilter(filter, priority);
        _laidFilters.Add((socket, filter));
    }

    private void LiftPoses()
    {
        for (int i = 0; i < _laidFilters.Count; i++)
        {
            var (socket, filter) = _laidFilters[i];
            if (!socket.IsDestroyed)
                socket.RemoveFilter(filter);
        }
        _laidFilters.Clear();
        _posedParts.Clear();
        _posesPending = _posedRoot != null;
    }

    // Reads the list LayPoses and PlaceStandIns fill.
    private AvatarSocket? FindSocket(BodyNode node)
    {
        for (int i = 0; i < _socketScratch.Count; i++)
        {
            var socket = _socketScratch[i];
            if (!socket.IsDestroyed && socket.Node.Value == node)
                return socket;
        }
        return null;
    }

    // The last step on every socket this seat poses. A pose the seat's filters supplied (the socket's own
    // source was not tracking it) is one of the seat's references, and those face the way RotationOffset says
    // they do; turned by it, the pose faces our forward like any other body frame. A tracked pose is the
    // tracker's and is left alone.
    void IPoseFilter.ProcessPose(AvatarSocket socket, Slot space, ref float3 position, ref floatQ rotation, ref bool isTracking)
    {
        if (isTracking && socket != null && !socket.IsTracking.Value)
            rotation = (rotation * RotationOffset.Value).Normalized;
    }

    // The sitter's machine gives every Required part it has no socket for a stand-in: an untracked socket under
    // the sitter's root for the filters to pose, which the worn avatar's IK takes for that part. The walking
    // animation goes quiet as well, the feet with it when the seat holds both. All of it goes on standing up.
    private void PlaceStandIns(UserRoot userRoot)
    {
        RemoveStandIns();
        var parts = _seatedParts = userRoot.Slot.AddSlot("Seat Pose");
        _socketScratch.Clear();
        userRoot.GetRegisteredComponents(_socketScratch);
        bool leftFoot = false, rightFoot = false;
        for (int i = 0; i < BodyPoses.Count; i++)
        {
            var pose = BodyPoses[i];
            if (pose == null)
                continue;
            var node = pose.Node.Value;
            leftFoot |= node == BodyNode.LeftFoot;
            rightFoot |= node == BodyNode.RightFoot;
            if (!pose.Required.Value || node == BodyNode.NONE || FindSocket(node) != null || HasStandIn(node))
                continue;
            var socket = parts.AddSlot(node.ToString()).AttachComponent<AvatarSocket>();
            socket.IsTracking.Value = false;
            socket.Node.Value = node;
        }
        _socketScratch.Clear();

        var quiet = parts.AttachComponent<LocomotionAnimationSuppressor>();
        quiet.SuppressFeet.Value = leftFoot && rightFoot;
    }

    private bool HasStandIn(BodyNode node)
    {
        if (_seatedParts == null || _seatedParts.IsDestroyed)
            return false;
        foreach (var child in _seatedParts.Children)
        {
            if (child.GetComponent<AvatarSocket>()?.Node.Value == node)
                return true;
        }
        return false;
    }

    private void RemoveStandIns()
    {
        var parts = _seatedParts;
        _seatedParts = null;
        if (parts == null || parts.IsDestroyed)
            return;
        foreach (var child in parts.Children)
            child.GetComponent<AvatarSocket>()?.Dequip(null!);
        parts.Destroy();
    }

    // PERSISTENCE

    // The runtime state stays out of saves: a world must not come back with somebody still sitting in
    // it, and a restore pose captured last session points at slots that no longer exist. [NonPersistent]
    // on the occupancy fields is enough on its own - the base ShouldSerializeMember excludes them - and the
    // restore snapshot is plain fields that never reach a save. It was non-persistent while it was synced
    // too, so no save carries it and older ones load as they are. - xlinka

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
