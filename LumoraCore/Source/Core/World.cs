// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumora.Core.Networking.Session;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Components;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Represents a World instance containing Slots, Components, and Users.
/// </summary>
public class World
{
	public enum WorldState
	{
		/// <summary>
		/// World has been created but not initialized yet.
		/// </summary>
		Created,

		/// <summary>
		/// Setting up network connections and listeners.
		/// </summary>
		InitializingNetwork,

		/// <summary>
		/// Waiting for authority to grant join permission (client only).
		/// </summary>
		WaitingForJoinGrant,

		/// <summary>
		/// Initializing data model (slots, components, users).
		/// </summary>
		InitializingDataModel,

		/// <summary>
		/// World is fully initialized and running.
		/// </summary>
		Running,

		/// <summary>
		/// World failed to initialize or encountered fatal error.
		/// </summary>
		Failed,

		/// <summary>
		/// World has been destroyed and cleaned up.
		/// </summary>
		Destroyed
	}

	public enum InitializationState
	{
		Created,
		InitializingNetwork,
		WaitingForJoinGrant,
		InitializingDataModel,
		Finished,
		Failed
	}

	public enum WorldFocus
	{
		Background,
		Focused,
		Overlay,
		PrivateOverlay
	}

	public enum WorldEvent
	{
		OnFocusChanged,
		OnUserJoined,
		OnUserLeft,
		OnWorldDestroy
	}

	/// <summary>
	/// World statistics and metrics.
	/// </summary>
	public class WorldMetrics
	{
		/// <summary>Total slots in this world.</summary>
		public int SlotCount { get; internal set; }

		/// <summary>Total components in this world.</summary>
		public int ComponentCount { get; internal set; }

		/// <summary>Total sync elements (networked).</summary>
		public int SyncElementCount { get; internal set; }

		/// <summary>Total RefIDs allocated.</summary>
		public long RefIDsAllocated { get; internal set; }

		/// <summary>Network messages sent.</summary>
		public long MessagesSent { get; internal set; }

		/// <summary>Network messages received.</summary>
		public long MessagesReceived { get; internal set; }

		/// <summary>Total bytes sent.</summary>
		public long BytesSent { get; internal set; }

		/// <summary>Total bytes received.</summary>
		public long BytesReceived { get; internal set; }

		/// <summary>Updates processed.</summary>
		public long UpdatesProcessed { get; internal set; }

		/// <summary>Average update time in ms.</summary>
		public double AverageUpdateTimeMs { get; internal set; }

		/// <summary>Peak update time in ms.</summary>
		public double PeakUpdateTimeMs { get; internal set; }

		/// <summary>Godot-reported frames per second.</summary>
		public double GodotFps { get; set; }

		/// <summary>Godot-reported frame time in ms.</summary>
		public double GodotFrameTimeMs { get; set; }

		/// <summary>Godot CPU process time in ms. Kept as RenderTimeMs for compatibility.</summary>
		public double RenderTimeMs { get; set; }

		/// <summary>Physics time in ms.</summary>
		public double PhysicsTimeMs { get; set; }

		/// <summary>Video memory usage in bytes.</summary>
		public long VideoMemoryBytes { get; set; }

		/// <summary>Total Godot objects.</summary>
		public int GodotObjectCount { get; set; }

		/// <summary>Total Godot nodes.</summary>
		public int GodotNodeCount { get; set; }

		public override string ToString()
		{
			return $"Slots: {SlotCount}, Components: {ComponentCount}, Users: N/A, " +
			       $"Messages: {MessagesSent}/{MessagesReceived}, Updates: {UpdatesProcessed}";
		}
	}

	/// <summary>
	/// Who is allowed to join a world, from most to least restrictive.
	/// </summary>
	public enum WorldAccessLevel
	{
		Private,
		LAN,
		Contacts,
		ContactsPlus,
		/// <summary>Only members of the group hosting this world.</summary>
		GroupMembers,
		/// <summary>Group members plus guests they bring.</summary>
		GroupPlus,
		/// <summary>Anyone, but listed/hosted under the group.</summary>
		GroupPublic,
		RegisteredUsers,
		Anyone,
	}


	private readonly HashSet<IWorldElement> _dirtyElements = new();
	private readonly Dictionary<string, List<Slot>> _slotsByTag = new();
	private readonly List<Slot> _rootSlots = new();
	private readonly List<User> _users = new();
	private readonly List<User> _joinedUsers = new();
	private readonly List<User> _leftUsers = new();

	// Network replicators for world structure synchronization
	private Networking.Sync.ReplicatedSlotCollection? _slotCollection;
	private Networking.Sync.ReplicatedUserCollection? _userCollection;

	// The flat world slot registry. Exposed so the permission gate can tell a guest's own-byte slot
	// REGISTRATION (allowed - that's how a user spawns its own content) from an own-byte add onto a
	// host-owned per-slot collection like a component list (denied - that would bolt components onto host
	// geometry). See DataModelPermissions.Authorize. -xlinka
	internal IWorldElement? SlotRegistryElement => _slotCollection;
	private readonly List<IWorldEventReceiver>[] _worldEventReceivers;
	private WorldState _state = WorldState.Created;
	private InitializationState _initState = InitializationState.Created;
	private Session _session = null!;
	private HookManager _hookManager;
	private TrashBin _trashBin;
	private RefIDAllocator _refIDAllocator;
	private WorldFocus _focus = WorldFocus.Background;
	private HookTypeRegistry _hookTypes;
	private UpdateManager _updateManager;
	private Queue<Action> _synchronousActions = new Queue<Action>();
	private object _syncLock = new object();
	// Guards the WhenRunning/WhenDestroyed deferred-or-immediate accessors so a late subscriber can't
	// slip between the state flip and the fan-out. Separate from _syncLock on purpose - _syncLock is held
	// while running queued user actions and must not be entangled with this. -xlinka
	private readonly object _stateLock = new object();
	private readonly WorldMetrics _metrics = new WorldMetrics();
	private readonly DataModelPermissionController _dataModelPermissions;

	// Owned-namespace build is armed once the local user's allocation byte is known. Gated so a per-peer
	// spawn that runs a frame too early falls back to NOT building (and retries) instead of minting into
	// authority byte 0 and resurrecting the permission-bypass problem. -xlinka
	private bool _localAllocationReady;
	// First valid local-user assignment wins for the session. Guards a stray second SetLocalUser from
	// silently re-pointing the actor mid-session. -xlinka
	private bool _localUserSet;
	// When the client entered data-model init (join window opened). Diagnostics + bounded-wait logging. 0 = not joining. -xlinka
	private double _dataModelInitStartTime;
	// Bounded deferral of Running on a client while we wait for the local user to resolve. -xlinka
	private int _awaitingLocalUserFrames;
	private const int MaxAwaitLocalUserFrames = 120; // ~2s @60, generous for a slow initial decode
	// Missing-root respawn watchdog state - re-fires spawn if a set LocalUser never gets a body. -xlinka
	private int _missingRootFrames;
	private int _respawnAttempts;
	private const int MissingRootGraceFrames = 180; // ~3s
	private const int MaxRespawnAttempts = 3;

	/// <summary>Seconds since this client began data-model init, or 0 on the authority / before join. -xlinka</summary>
	public double TimeSinceDataModelInit => _dataModelInitStartTime <= 0 ? 0 : TotalTime - _dataModelInitStartTime;

	// Static global hook type registry (shared across all worlds)
	private static HookTypeRegistry _staticHookTypes = new HookTypeRegistry();

	// Platform hook for world rendering
	public IWorldHook Hook { get; set; } = null!;

	// Godot scene access - set by WorldHook
	public object GodotSceneRoot { get; set; } = null!;

	private Physics.WorldPhysics _physics = null!;

	/// <summary>
	/// Component-facing physics service for this world: collision queries (routed to the platform
	/// physics engine via the world hook) and physics settings. The platform owns the simulation.
	/// </summary>
	public Physics.WorldPhysics Physics => _physics ??= new Physics.WorldPhysics(this);

	// Reference to the WorldManager that owns this World
	public Management.WorldManager WorldManager { get; internal set; } = null!;

	private static int _worldEventTypeCount = Enum.GetValues(typeof(WorldEvent)).Length;

	/// <summary>
	/// Global hook type registry (static, shared across all worlds).
	/// </summary>
	public static HookTypeRegistry HookTypes => _staticHookTypes;

	/// <summary>
	/// Event fired when world state changes.
	/// </summary>
	public event Action<WorldState, WorldState>? OnStateChanged;

	private Action<World>? _whenRunning;

	/// <summary>
	/// Fires once the world is Running. If it's ALREADY running when you subscribe, your handler runs
	/// immediately (inline, on the calling thread); otherwise it's queued and fires at the transition.
	/// Either way a late subscriber never misses the running edge. Unsubscribe with -=. -xlinka
	/// </summary>
	public event Action<World> WhenRunning
	{
		add
		{
			// Fast path: already running, run it now without holding the lock.
			if (_state == WorldState.Running)
			{
				value(this);
				return;
			}
			lock (_stateLock)
			{
				// Re-check under the lock: the transition may have landed between the check above and here.
				if (_state == WorldState.Running)
					value(this);
				else
					_whenRunning += value;
			}
		}
		remove
		{
			lock (_stateLock)
			{
				_whenRunning -= value;
			}
		}
	}

	private Action<World>? _whenDestroyed;

	/// <summary>
	/// Fires once the world is destroyed. If it's ALREADY destroyed when you subscribe, your handler runs
	/// immediately; otherwise it's queued and fires at teardown. A late subscriber never misses the
	/// destroyed edge - handy for cleanup that may register after the world is already gone. -xlinka
	/// </summary>
	public event Action<World> WhenDestroyed
	{
		add
		{
			if (_state == WorldState.Destroyed)
			{
				value(this);
				return;
			}
			lock (_stateLock)
			{
				if (_state == WorldState.Destroyed)
					value(this);
				else
					_whenDestroyed += value;
			}
		}
		remove
		{
			lock (_stateLock)
			{
				_whenDestroyed -= value;
			}
		}
	}

	/// <summary>
	/// Current state of the World.
	/// </summary>
	public WorldState State => _state;

	/// <summary>
	/// Edit mode of this world (Builder / Social / Event). Set at host time from the world's allowed
	/// modes and applied to the permission gate when the world starts running. Not a live toggle - the
	/// Social/Event lock is enforced host-authoritatively and cannot be turned off in-session.
	/// </summary>
	public WorldMode Mode
	{
		get => Configuration?.Mode?.Value ?? WorldMode.Builder;
		set
		{
			// Baked at host time, not a live toggle. Once the world is running the mode is fixed for
			// the session - re-host (or load a differently-moded world) to change it.
			if (_state == WorldState.Running)
			{
				if (value != Mode)
					LumoraLogger.Warn($"World.Mode is baked for the session and can't change live (ignored {value}).");
				return;
			}
			var c = Configuration;
			if (c != null)
				c.Mode.Value = value;
		}
	}

	/// <summary>
	/// Whether the authored world can be edited here (build tools, dev tools, inspectors, gizmos).
	/// False in Social/Event worlds. This is a UX/availability hint - the actual lock is the
	/// host-authoritative <see cref="DataModelPermissionController.SocialLock"/> gate.
	/// </summary>
	public bool AllowsWorldEditing => Mode == WorldMode.Builder;

	/// <summary>Whether users may spawn their own items here (true except in Event worlds).</summary>
	public bool AllowsItemSpawning => Mode != WorldMode.Event;

	/// <summary>
	/// Detailed initialization state for the World.
	/// </summary>
	public InitializationState InitState => _initState;

	/// <summary>
	/// Human-readable initialization failure reason, if initialization failed.
	/// </summary>
	public string InitializationFailureReason { get; private set; } = "";

	/// <summary>
	/// The root Slot of this World.
	/// </summary>
	public Slot RootSlot { get; private set; } = null!;

	/// <summary>
	/// Live collider registry for this world, maintained by Collider on attach/destroy. Lets raycasts
	/// iterate a flat list instead of walking the entire slot tree every frame, per laser. Holds ALL
	/// colliders (active or not); raycast callers filter by their own candidacy checks, so a momentarily
	/// stale entry (e.g. a just-destroyed collider) can't produce a wrong hit. -xlinka
	/// </summary>
	private readonly HashSet<Components.Collider> _colliders = new();

	public void RegisterCollider(Components.Collider collider)
	{
		if (collider != null)
			_colliders.Add(collider);
	}

	public void UnregisterCollider(Components.Collider collider)
	{
		if (collider != null)
			_colliders.Remove(collider);
	}

	/// <summary>Copy the live colliders into a caller-provided (reusable) buffer - allocation-free. -xlinka</summary>
	public void CopyCollidersTo(List<Components.Collider> buffer)
	{
		buffer.Clear();
		foreach (var c in _colliders)
			buffer.Add(c);
	}

	/// <summary>
	/// Live interaction-target registry for this world, maintained centrally by ComponentBase on init/destroy
	/// (so EVERY IInteractionTarget component is in here, no per-implementer opt-in to forget). Lets the laser
	/// iterate a flat list instead of walking the entire slot tree every frame, per laser. Holds ALL targets
	/// (active, disabled, even momentarily stale); the laser filters each candidate by enabled/active/hierarchy
	/// at use-site, so a stale entry can't produce a wrong hit. A target moved across worlds re-registers via
	/// its re-init (same caveat as the collider registry). -xlinka
	/// </summary>
	private readonly HashSet<Components.Interaction.IInteractionTarget> _interactionTargets = new();

	public void RegisterInteractionTarget(Components.Interaction.IInteractionTarget target)
	{
		if (target != null)
			_interactionTargets.Add(target);
	}

	public void UnregisterInteractionTarget(Components.Interaction.IInteractionTarget target)
	{
		if (target != null)
			_interactionTargets.Remove(target);
	}

	/// <summary>Copy the live interaction targets into a caller-provided (reusable) buffer - allocation-free. -xlinka</summary>
	public void CopyInteractionTargetsTo(List<Components.Interaction.IInteractionTarget> buffer)
	{
		buffer.Clear();
		foreach (var t in _interactionTargets)
			buffer.Add(t);
	}

	/// <summary>
	/// Display name of this World.
	/// </summary>
	public Sync<string> WorldName { get; private set; }

	/// <summary>
	/// Session ID for network identification.
	/// </summary>
	public Sync<string> SessionID { get; private set; }

	/// <summary>
	/// The authority ID (host) that validates all changes.
	/// -1 means local-only world.
	/// </summary>
	public int AuthorityID { get; set; } = -1;

	/// <summary>
	/// Whether this instance is the authority (host).
	/// Local ID for checking authority status.
	/// </summary>
	public int LocalID { get; set; } = -1;

	/// <summary>
	/// Whether this instance is the authority (host).
	/// </summary>
	public bool IsAuthority => AuthorityID == -1 || AuthorityID == LocalID;

	/// <summary>
	/// Session for networking.
	/// </summary>
	public Session Session => _session;

	/// <summary>
	/// URLs that can be used to connect to this session.
	/// </summary>
	public IReadOnlyList<Uri> SessionURLs => (_session?.Metadata?.SessionURLs as IReadOnlyList<Uri>) ?? Array.Empty<Uri>();

	/// <summary>
	/// Synchronization controller for this world.
	/// </summary>
	public SyncController SyncController { get; private set; } = null!;

	/// <summary>
	/// Tracks fields that just lost their driving link so the sync loop can re-broadcast their
	/// real current value to peers (authority only).
	/// </summary>
	public LinkManager LinkManager { get; private set; } = null!;

	/// <summary>
	/// Reference controller for object lookup and async resolution.
	/// </summary>
	public ReferenceController ReferenceController { get; private set; } = null!;

	/// <summary>
	/// Worker manager for type encoding/decoding during sync.
	/// </summary>
	public WorkerManager Workers { get; private set; } = null!;

	/// <summary>
	/// Thread-safe hook manager for world modifications.
	/// </summary>
	public HookManager HookManager => _hookManager;

	/// <summary>
	/// Trash bin for temporarily holding deleted objects.
	/// </summary>
	public TrashBin TrashBin => _trashBin;

	/// <summary>
	/// RefID allocator for preventing ID conflicts between users.
	/// </summary>
	public RefIDAllocator RefIDAllocator => _refIDAllocator;

	/// <summary>
	/// Hook type registry for mapping components to hooks (instance property for compatibility).
	/// </summary>
	public HookTypeRegistry InstanceHookTypes => _hookTypes;

	/// <summary>
	/// Update manager for coordinating hook updates.
	/// </summary>
	public UpdateManager UpdateManager => _updateManager;

	/// <summary>
	/// Local user (client's own user).
	/// </summary>
	public User LocalUser { get; private set; } = null!;

	/// <summary>
	/// Time scale for the World simulation.
	/// </summary>
	public float TimeScale { get; set; } = 1.0f;

	/// <summary>
	/// Total time this World has been running (in seconds).
	/// </summary>
	public double TotalTime { get; private set; }

	/// <summary>
	/// Local sync tick counter for ordering messages.
	/// Incremented every sync cycle.
	/// </summary>
	public ulong SyncTick { get; private set; }

	/// <summary>
	/// Authority state version for conflict detection.
	/// Incremented whenever the authority makes a state change.
	/// </summary>
	public ulong StateVersion { get; private set; }

	/// <summary>
	/// Whether this world has been destroyed.
	/// </summary>
	public bool IsDestroyed { get; internal set; }

	/// <summary>
	/// Whether this world has been disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <summary>
	/// Last frame delta time (seconds).
	/// </summary>
	public float LastDelta { get; private set; }

	/// <summary>
	/// Convenience property for WorldName.Value.
	/// </summary>
	public string Name => WorldName?.Value ?? "Unknown";

	/// <summary>
	/// Whether this world is currently focused.
	/// </summary>
	public bool IsFocused => _focus == WorldFocus.Focused;

	/// <summary>
	/// Number of users in the world.
	/// </summary>
	public int UserCount
	{
		get
		{
			lock (_users)
			{
				return _users.Count;
			}
		}
	}

	/// <summary>
	/// Current focus mode of this world.
	/// </summary>
	public WorldFocus Focus
	{
		get => _focus;
		set
		{
			if (_focus == value) return;
			_focus = value;

			// Notify world hook of focus change
			if (Hook is IWorldHook worldHook)
			{
				worldHook.ChangeFocus(value);
			}
		}
	}

	/// <summary>
	/// World statistics and metrics.
	/// </summary>
	public WorldMetrics Metrics => _metrics;

	/// <summary>
	/// World configuration settings, as a synced component on the root slot (replicates to clients +
	/// persists with the world tree). On the authority it's created on first access; on a client it's
	/// null until state-synced - callers that may run client-side should null-check.
	/// </summary>
	public WorldSettings Configuration
	{
		get
		{
			var root = RootSlot;
			if (root == null)
				return null!;
			var settings = root.GetComponent<WorldSettings>();
			if (settings == null && IsAuthority)
				settings = root.AttachComponent<WorldSettings>();
			return settings!;
		}
	}

	/// <summary>
	/// Hard permission gate for datamodel fields, collections, and replication.
	/// </summary>
	public DataModelPermissionController DataModelPermissions => _dataModelPermissions;

	// PERSISTENCE
	// Serialize/restore the whole world (its slot tree) to/from a data tree. Permissions are NOT
	// serialized: they're a hard runtime policy derived from ownership/authority, applied live.
	// The local home is hosted by the local user (authority), so save/load pass the permission gate.

	private const int WorldFormatVersion = 1;

	/// <summary>Serialize this world's data (name + slot tree) into a data tree.</summary>
	public DataTreeDictionary SaveWorld()
	{
		var translator = new ReferenceTranslator();
		var control = new SaveControl(RootSlot, translator);

		// Save the tree first so type versions are collected before they're stored.
		var rootNode = RootSlot.Save(control);

		var dictionary = new DataTreeDictionary();
		dictionary.Add("FormatVersion", WorldFormatVersion);
		dictionary.Add("Name", WorldName.Value);

		var typeVersions = new DataTreeDictionary();
		control.StoreTypeVersions(typeVersions);
		dictionary.Add("TypeVersions", typeVersions);

		// World settings persist as the WorldSettings component on the root slot (part of rootNode),
		// so there's no separate "Config" blob.
		dictionary.Add("Root", rootNode);
		return dictionary;
	}

	/// <summary>Restore this world's contents from a data tree into the (already-created) root slot.</summary>
	public void LoadWorld(DataTreeDictionary dictionary)
	{
		var translator = new ReferenceTranslator();
		var control = new LoadControl(this, translator);

		try
		{
			if (dictionary.TryGetDictionary("TypeVersions") is { } typeVersions)
				control.LoadTypeVersions(typeVersions);

			if (dictionary.ContainsKey("Name"))
				WorldName.Value = dictionary.ExtractOrDefault("Name", WorldName.Value);

			if (dictionary.TryGetNode("Root") is { } rootNode)
				RootSlot.Load(rootNode, control);

			// The WorldSettings component (with the persisted Mode) is now loaded under the root. If
			// we loaded into an already-running authority world, re-apply the mode's permission preset
			// (otherwise StartRunning applies it).
			if (_state == WorldState.Running && IsAuthority)
				WorldModePermissions.Apply(this, Mode);
		}
		finally
		{
			// Always resolve deferred refs + report leftovers, even if a sub-load threw, so a
			// partial load doesn't leave dangling waiters. A thrown load still propagates to the
			// caller (WorldStorage.LoadFromFile) so it can fall back to the template.
			control.FinishLoad();
		}
	}

	/// <summary>
	/// Get a diagnostic summary of this world.
	/// </summary>
	public string GetDiagnostics()
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"World: {Name}");
		sb.AppendLine($"State: {State}");
		sb.AppendLine($"SessionID: {SessionID?.Value ?? "N/A"}");
		sb.AppendLine($"IsAuthority: {IsAuthority}");
		sb.AppendLine($"Users: {UserCount}");
		sb.AppendLine($"TotalTime: {TotalTime:F2}s");
		sb.AppendLine($"SyncTick: {SyncTick}");
		sb.AppendLine($"StateVersion: {StateVersion}");
		sb.AppendLine($"Metrics: {_metrics}");
		return sb.ToString();
	}

	/// <summary>
	/// Update world metrics (call periodically).
	/// </summary>
	internal void UpdateMetrics()
	{
		_metrics.SlotCount = RootSlot?.GetDescendants(true).Count() ?? 0;
		_metrics.ComponentCount = RootSlot?.GetDescendants(true).Sum(s => s.ComponentCount) ?? 0;
		_metrics.RefIDsAllocated = ReferenceController?.ObjectCount ?? 0;
	}

	/// <summary>
	/// Event triggered when a Slot is added to the World.
	/// </summary>
	public event Action<Slot> OnSlotAdded = null!;

	/// <summary>
	/// Event triggered when a Slot is removed from the World.
	/// </summary>
	public event Action<Slot> OnSlotRemoved = null!;

	public World()
	{
		WorldName = new Sync<string>(null, "New World");
		SessionID = new Sync<string>(null, Guid.NewGuid().ToString());
		_hookManager = new HookManager(this);
		_trashBin = new TrashBin(this);
		_refIDAllocator = new RefIDAllocator(this);
		_hookTypes = new HookTypeRegistry();
		_updateManager = new UpdateManager(this);
		_dataModelPermissions = new DataModelPermissionController(this);

		// Initialize event receiver arrays
		int length = Enum.GetValues(typeof(WorldEvent)).Length;
		_worldEventReceivers = new List<IWorldEventReceiver>[length];
		for (int i = 0; i < length; i++)
		{
			_worldEventReceivers[i] = new List<IWorldEventReceiver>();
		}
	}

	/// <summary>
	/// Create a local-only world (single user, no networking).
	/// </summary>
	public static World LocalWorld(Engine engine, string name, Action<World> init = null!)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.AuthorityID = -1; // Local-only (no authority)
		world.LocalID = -1;
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world
		world.Initialize();

		// Run initialization callback first so event receivers (like SimpleUserSpawn) are registered
		init?.Invoke(world);

		// Create local user (this triggers OnUserJoined after SimpleUserSpawn is ready)
		world.CreateHostUser("LocalUser");

		// Start running
		world.StartRunning();
		LumoraLogger.Log($"Local world '{name}' created and started");

		return world;
	}

	/// <summary>
	/// Start a hosted session (authority/server).
	/// </summary>
	public static World StartSession(Engine engine, string name, ushort port, string hostUserName = null!, Action<World> init = null!)
	{
		return StartSession(engine, name, port, hostUserName, SessionVisibility.Private, 16, init);
	}

	/// <summary>
	/// Start a hosted session (authority/server) with visibility settings.
	/// </summary>
	public static World StartSession(
		Engine engine,
		string name,
		ushort port,
		string hostUserName,
		SessionVisibility visibility,
		int maxUsers = 16,
		Action<World> init = null!)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.AuthorityID = 0; // This instance is authority
		world.LocalID = 0;
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world
		world.Initialize();

		world.Configuration.MaxUsers.Value = global::System.Math.Max(1, maxUsers);
		world.Configuration.AllowJoin.Value = true;
		world.Configuration.IsPublic.Value = visibility == SessionVisibility.Public;
		// Seed the access level from the hosted visibility so the Settings radio shows the right selection and
		// later live toggles have the correct baseline. This runs before the world is Running, so the
		// AccessLevel.OnChanged beacon handler (Running-gated) won't fire here - the initial beacon still comes
		// from the metadata visibility below. -xlinka
		world.Configuration.AccessLevel.Value = visibility switch
		{
			SessionVisibility.LAN => WorldAccessLevel.LAN,
			SessionVisibility.Contacts => WorldAccessLevel.Contacts,
			SessionVisibility.Public => WorldAccessLevel.Anyone,
			_ => WorldAccessLevel.Private,
		};

		// Build session metadata
		var metadata = new SessionMetadata
		{
			Name = name,
			HostUsername = hostUserName ?? Environment.MachineName,
			HostMachineId = Environment.MachineName,
			Visibility = visibility,
			MaxUsers = world.Configuration.MaxUsers.Value
		};

		// Start session network (creates LNL listener) but don't create user yet
		world.StartSessionNetwork(port, metadata);

		// Set session ID from the generated metadata
		world.SessionID.Value = world._session?.Metadata?.SessionId ?? SessionIdentifier.Generate();

		// Run initialization callback first so event receivers (like SimpleUserSpawn) are registered
		init?.Invoke(world);

		// Now create the host user (triggers OnUserJoined after SimpleUserSpawn is ready)
		world.CreateHostUser(hostUserName!);

		// Start running
		world.StartRunning();
		LumoraLogger.Log($"Session '{name}' started on port {port} with visibility {visibility}");

		return world;
	}

	/// <summary>
	/// Join a remote session (client).
	/// </summary>
	public static World JoinSession(Engine engine, string name, Uri address)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.SessionID.Value = "Unknown"; // Will be set by server
		world.AuthorityID = 0; // Server is authority
		world.LocalID = -1; // Will be assigned by server
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world as CLIENT (uses LOCAL RefID space to avoid collisions with host's Authority RefIDs)
		world.Initialize(isAuthority: false);

		// Join session (connects to LNL server)
		world.JoinSession(address);

		// World will transition to Running when connection succeeds
		LumoraLogger.Log($"Joining session at {address}");

		return world;
	}

	/// <summary>
	/// Join a remote session (client) asynchronously.
	/// </summary>
	public static async Task<World?> JoinSessionAsync(Engine engine, string name, Uri address)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.SessionID.Value = "Unknown"; // Will be set by server
		world.AuthorityID = 0; // Server is authority
		world.LocalID = -1; // Will be assigned by server
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world as CLIENT (uses LOCAL RefID space to avoid collisions with host's Authority RefIDs)
		world.Initialize(isAuthority: false);

		// Join session (connects to LNL server)
		var joined = await world.JoinSessionAsync(address);
		if (!joined)
		{
			return null;
		}

		LumoraLogger.Log($"Joining session at {address}");
		return world;
	}

	/// <summary>
	/// Initialize the World and create the root Slot.
	/// </summary>
	/// <param name="isAuthority">True if this is the authority/host, false for clients.
	/// Clients use LOCAL RefID space to avoid conflicts with network-received Authority RefIDs.</param>
	public void Initialize(bool isAuthority = true)
	{
		if (_state != WorldState.Created) return;

		_initState = InitializationState.Created;
		InitializationFailureReason = "";
		LumoraLogger.Log($"World initializing data model (isAuthority={isAuthority})");

		// Create reference controller BEFORE anything else
		ReferenceController = new ReferenceController(this);

		// Create sync controller first (doesn't need RefID)
		SyncController = new SyncController(this);
		LumoraLogger.Log("SyncController initialized");

		// Released-drive tracker rides alongside the sync controller. -xlinka
		LinkManager = new LinkManager(this);
		LumoraLogger.Log("LinkManager initialized");

		// Create worker manager for type encoding/decoding during sync
		// Needs SyncController available so the type index table registers for replication.
		Workers = new WorkerManager(this);

		// Always create network replicators with consistent RefIDs
		// This ensures both client and host use the same RefIDs for replicators
		_slotCollection = new Networking.Sync.ReplicatedSlotCollection();
		_slotCollection.Initialize(this, null);
		_userCollection = new Networking.Sync.ReplicatedUserCollection();
		_userCollection.Initialize(this, null);
		LumoraLogger.Log($"Network Collection initialized: SlotCollection={_slotCollection.ReferenceID}, UserCollection={_userCollection.ReferenceID}");

		// Clients must create shared world structures (RootSlot) in authority RefID space
		// so incoming ParentSlotRef and other references resolve correctly.
		// Local-only structures should explicitly use LocalAllocationBlockBegin where created.
		if (!isAuthority)
		{
			LumoraLogger.Log("Client: Using authority RefID space for shared world structures");
		}

		// Create root Slot (uses Authority RefID space on both host and client)
		RootSlot = new Slot();
		RootSlot.SlotName.Value = "Root";
		RootSlot.Initialize(this);
		RegisterSlot(RootSlot);

		// Note: Godot scene attachment handled by WorldDriver wrapper
		// World itself is pure C# and doesn't use AddChild

		// Network session is started via StartSession() or JoinSession()
		// No automatic network initialization

		// DON'T transition to Running here for clients!
		// Clients need to wait for full state download.
		// Only local/authority worlds can go to Running immediately.
		if (_slotCollection != null && _slotCollection.IsInInitPhase)
		{
			_slotCollection.EndInitPhase();
		}
		if (_userCollection != null && _userCollection.IsInInitPhase)
		{
			_userCollection.EndInitPhase();
		}
		LumoraLogger.Log($"World '{WorldName.Value}' initialized successfully - state={_state}, initState={_initState}");
	}

	/// <summary>
	/// Get or create the Users container slot (NOT a UserRoot component!).
	/// This is just a container - each user gets their own UserRootComponent via SimpleUserSpawn.
	/// </summary>
	public Slot GetOrCreateUsersSlot()
	{
		// Try to find existing Users slot
		var usersSlot = FindSlotsByTag("UserRoot").FirstOrDefault();

		if (usersSlot == null)
		{
			// Create Users container slot under Root
			usersSlot = RootSlot.AddSlot("Users");
			usersSlot.Tag.Value = "UserRoot";
			LumoraLogger.Log("Created Users container slot in world");
		}

		// NO UserRootComponent here! Users container is just a parent slot.
		// Each individual user gets UserRootComponent via SimpleUserSpawn!

		return usersSlot;
	}


	/// <summary>
	/// Register a Slot with the World.
	/// </summary>
	internal void RegisterSlot(Slot slot)
	{
		if (slot == null) return;

		// Only register if not already registered (InitializeFromReplicator may have already registered)
		if (!ReferenceController.ContainsObject(slot.ReferenceID))
		{
			ReferenceController?.RegisterObject(slot);
		}
		Metrics.SlotCount++;

		// Add to slot replicator for network sync (skip local-only slots)
		if (!slot.IsLocalElement && _slotCollection != null && !_slotCollection.ContainsKey(slot.ReferenceID))
		{
			_slotCollection.Add(slot.ReferenceID, slot, isNewlyCreated: true, skipSync: false);
		}

		if (!string.IsNullOrEmpty(slot.Tag.Value))
		{
			if (!_slotsByTag.TryGetValue(slot.Tag.Value, out var slots))
			{
				slots = new List<Slot>();
				_slotsByTag[slot.Tag.Value] = slots;
			}
			slots.Add(slot);
		}

		if (slot.IsRootSlot)
		{
			_rootSlots.Add(slot);
		}

		OnSlotAdded?.Invoke(slot);
	}

	/// <summary>
	/// Unregister a Slot from the World.
	/// </summary>
	internal void UnregisterSlot(Slot slot)
	{
		if (slot == null) return;

		ReferenceController?.UnregisterObject(slot.ReferenceID);
		Metrics.SlotCount--;

		// Remove from slot replicator for network sync
		if (!slot.IsLocalElement)
		{
			_slotCollection?.Remove(slot.ReferenceID);
		}

		if (!string.IsNullOrEmpty(slot.Tag.Value))
		{
			if (_slotsByTag.TryGetValue(slot.Tag.Value, out var slots))
			{
				slots.Remove(slot);
			}
		}

		_rootSlots.Remove(slot);
		OnSlotRemoved?.Invoke(slot);
	}

	/// <summary>
	/// Register a Component with the World.
	/// </summary>
	internal void RegisterComponent(Component component)
	{
		if (component == null) return;
		if (!ReferenceController.ContainsObject(component.ReferenceID))
		{
			ReferenceController?.RegisterObject(component);
		}
		Metrics.ComponentCount++;
	}

	/// <summary>
	/// Unregister a Component from the World.
	/// </summary>
	internal void UnregisterComponent(Component component)
	{
		if (component == null) return;
		ReferenceController?.UnregisterObject(component.ReferenceID);
		Metrics.ComponentCount--;
	}

	/// <summary>
	/// Mark an element as dirty (needs network synchronization).
	/// </summary>
	internal void MarkElementDirty(IWorldElement element)
	{
		if (element != null && !element.IsDestroyed)
		{
			_dirtyElements.Add(element);
		}
	}

	/// <summary>
	/// Get all elements that have been modified since last sync.
	/// </summary>
	public IEnumerable<IWorldElement> GetDirtyElements()
	{
		return _dirtyElements.ToArray();
	}

	/// <summary>
	/// Clear the dirty elements list (after synchronization).
	/// </summary>
	public void ClearDirtyElements()
	{
		_dirtyElements.Clear();
	}

	/// <summary>
	/// Get all elements in the world.
	/// </summary>
	public IEnumerable<KeyValuePair<RefID, IWorldElement>> GetAllElements()
	{
		return ReferenceController?.AllObjects ?? Array.Empty<KeyValuePair<RefID, IWorldElement>>();
	}

	/// <summary>
	/// Find a world element by its RefID.
	/// </summary>
	public IWorldElement FindElement(RefID refID)
	{
		return (ReferenceController?.GetObjectOrNull(refID)) ?? null!;
	}

	/// <summary>
	/// Find a world element by its RefID (legacy ulong overload).
	/// </summary>
	public IWorldElement FindElement(ulong refID)
	{
		return FindElement(new RefID(refID));
	}

	/// <summary>
	/// Try to retrieve a trashed object for the given tick and ID (used during confirmations).
	/// </summary>
	public IWorldElement TryRetrieveFromTrash(ulong tick, RefID id)
	{
		return (ReferenceController?.TryRetrieveFromTrash(tick, id)) ?? null!;
	}

	/// <summary>
	/// Find all Slots with the specified tag.
	/// </summary>
	public IEnumerable<Slot> FindSlotsByTag(string tag)
	{
		if (_slotsByTag.TryGetValue(tag, out var slots))
		{
			return slots.ToArray();
		}
		return Array.Empty<Slot>();
	}

	/// <summary>
	/// Find a Slot by name (searches entire hierarchy).
	/// </summary>
	public Slot FindSlotByName(string name)
	{
		return FindSlotByNameRecursive(RootSlot, name);
	}

	private Slot FindSlotByNameRecursive(Slot slot, string name)
	{
		if (slot.SlotName.Value == name)
			return slot;

		foreach (var child in slot.Children)
		{
			var found = FindSlotByNameRecursive(child, name);
			if (found != null) return found;
		}
		foreach (var child in slot.LocalChildren)
		{
			var found = FindSlotByNameRecursive(child, name);
			if (found != null) return found;
		}

		return null!;
	}

	/// <summary>
	/// Destroy the World and all its contents.
	/// </summary>
	public void DestroyWorld()
	{
		if (_state == WorldState.Destroyed) return;

		LumoraLogger.Log($"Destroying world '{WorldName.Value}'...");

		_state = WorldState.Destroyed;

		// Destroy all root slots (which will cascade to children)
		foreach (var slot in _rootSlots.ToArray())
		{
			slot.Destroy();
		}

		_dirtyElements.Clear();
		_slotsByTag.Clear();
		_rootSlots.Clear();
		_users.Clear();

		_hookManager?.Dispose();

		ReferenceController?.Reset();

		// Note: Godot scene cleanup handled by WorldDriver wrapper
	}

	/// <summary>
	/// Add a user to the world.
	/// </summary>
	public void AddUser(User user)
	{
		if (user == null) return;

		// Reject banned users (host-authoritative). Never ban-check our own/host user.
		if (IsAuthority && LocalUser != null && user != LocalUser
			&& Security.BanManager.IsBanned(user.UserID?.Value, user.MachineID?.Value, WorldName?.Value))
		{
			LumoraLogger.Warn($"Rejecting banned user '{user.UserName.Value}'");
			return;
		}

		lock (_users)
		{
			if (!_users.Contains(user))
			{
				_users.Add(user);
				// Only register if not already registered (User constructor may have already registered)
				if (!ReferenceController.ContainsObject(user.ReferenceID))
				{
					ReferenceController?.RegisterObject(user);
				}

				// Add to user replicator for network sync (only if not already present)
				if (_userCollection != null && !_userCollection.ContainsKey(user.ReferenceID))
				{
					_userCollection.Add(user.ReferenceID, user, isNewlyCreated: true, skipSync: false);
				}

				LumoraLogger.Log($"User added to world: {user.UserName.Value}");

				// Trigger user joined event - ONLY on authority!
				// Only host fires OnUserJoined events. Host's SimpleUserSpawn creates
				// avatar slots in authority namespace, which are then synced to clients.
				// If clients also fired OnUserJoined, they'd create duplicate slots.
				if (IsAuthority)
				{
					TriggerUserJoinedEvent(user);
				}

				// Notify session of user count change
				_session?.OnUserCountChanged(_users.Count);
			}
		}
	}

	internal void AddUserToCollection(User user, RefID id, bool isNewlyCreated)
	{
		if (user == null || _userCollection == null)
		{
			return;
		}

		if (!_userCollection.ContainsKey(id))
		{
			_userCollection.Add(id, user, isNewlyCreated, skipSync: false);
		}
	}

	/// <summary>
	/// Remove a user from the world.
	/// </summary>
	public void RemoveUser(User user)
	{
		if (user == null) return;

		lock (_users)
		{
			_users.Remove(user);
			ReferenceController?.UnregisterObject(user.ReferenceID);

			// Remove from user replicator for network sync
			_userCollection?.Remove(user.ReferenceID);

			LumoraLogger.Log($"User removed from world: {user.UserName.Value}");

			// Trigger user left event - ONLY on authority!
			// Same reason as OnUserJoined - host handles user lifecycle events.
			if (IsAuthority)
			{
				var userByte = user.AllocationID.Value;
				if (!RefIDConstants.IsValidUserByte(userByte))
					userByte = user.ReferenceID.GetUserByte();

				// Purge ALL of this user's objects (streams, body slots, etc.) from the RefID registry, not
				// just the User object above. Otherwise the leftovers stay registered and the next joiner who
				// recycles this byte collides ("RefID collision! User[NNN]:... already registered"). -xlinka
				ReferenceController?.PurgeUserByte(userByte);

				// Hand this user's byte back to the allocator so a long session with join/leave churn
				// doesn't run the 253 user-byte space dry. Authority owns allocation, so only it reclaims. -xlinka
				_refIDAllocator?.ReleaseUserAllocation(userByte);

				TriggerUserLeftEvent(user);
			}

			// Notify session of user count change
			_session?.OnUserCountChanged(_users.Count);
		}
	}

	/// <summary>
	/// Register a user with the world (called by UserCollection).
	/// Alias for AddUser.
	/// </summary>
	internal void RegisterUser(User user) => AddUser(user);

	/// <summary>
	/// Unregister a user from the world (called by UserCollection).
	/// Alias for RemoveUser.
	/// </summary>
	internal void UnregisterUser(User user) => RemoveUser(user);

	/// <summary>
	/// Set the local user (client's own user). First valid assignment wins for the session.
	/// </summary>
	public void SetLocalUser(User user)
	{
		if (user == null)
		{
			LumoraLogger.Warn("SetLocalUser(null) ignored.");
			return;
		}

		// One-shot: first valid assignment wins. A redundant call with the SAME user is a quiet no-op (the
		// grant-claim path and User.Initialize both race to set it), a DIFFERENT user is a bug (mismatched
		// grant / wrong RefID) and is rejected loudly rather than silently re-pointing the actor. -xlinka
		if (_localUserSet)
		{
			if (!ReferenceEquals(LocalUser, user))
				LumoraLogger.Error($"SetLocalUser called again with a DIFFERENT user " +
					$"(have '{LocalUser?.UserName.Value}' {LocalUser?.ReferenceID}, got '{user.UserName.Value}' {user.ReferenceID}). Ignoring.");
			return;
		}

		_localUserSet = true;
		LocalUser = user;

		// Only configure streams if world is already Running. For clients, streams are decoded AFTER
		// SetLocalUser during FullBatch processing, so StartRunning() configures them instead.
		if (_state == WorldState.Running)
			user.ConfigureLocalTrackingStreams();

		AddUser(user);
		LumoraLogger.Log($"Local user set: {user.UserName.Value}");

		// Arm owned-namespace building now we have the local user. No-op until the allocation byte is
		// resolvable, the spawn driver retries if AllocationID hasn't synced yet. -xlinka
		InitializeAllocationForLocalUser();

		// On a client this is the moment we definitively know our own user - fire the joined event so the
		// per-peer spawn builds OUR equipment under the (replicated) scaffold. The host already fires it for
		// every user via AddUser, so only do this on a client to avoid a double-fire. -xlinka
		if (!IsAuthority)
			TriggerUserJoinedEvent(user);
	}

	/// <summary>
	/// Resolve which RefID byte the local user's own content should mint into. On the host the local user IS
	/// the authority, so this returns the authority byte (host owns everything anyway). On a client it's the
	/// local user's allocation byte once known, else the byte carried in the user's own RefID (set even
	/// before AllocationID syncs), else authority as a last resort. -xlinka
	/// </summary>
	private byte ResolveOwnedAllocationByte()
	{
		var u = LocalUser;
		if (u == null || IsAuthority)
			return RefIDConstants.AUTHORITY_BYTE;

		var b = u.AllocationID.Value;
		if (RefIDConstants.IsValidUserByte(b))
			return b;

		b = u.ReferenceID.GetUserByte();
		return RefIDConstants.IsValidUserByte(b) ? b : RefIDConstants.AUTHORITY_BYTE;
	}

	/// <summary>
	/// Arm owned-namespace building for the local user. Idempotent. Must run AFTER LocalUser is set and its
	/// allocation byte is resolvable, BEFORE any per-peer spawn builds the user's own equipment, so that
	/// equipment lands in the owned namespace. Host short-circuits true (it authors in authority byte 0). -xlinka
	/// </summary>
	public bool InitializeAllocationForLocalUser()
	{
		if (IsAuthority)
		{
			_localAllocationReady = true;
			return true;
		}

		var u = LocalUser;
		if (u == null)
			return false;

		var b = ResolveOwnedAllocationByte();
		if (!RefIDConstants.IsValidUserByte(b))
		{
			LumoraLogger.Warn($"InitializeAllocationForLocalUser: local user '{u.UserName.Value}' has no valid allocation byte yet");
			return false;
		}

		_localAllocationReady = true;
		return true;
	}

	/// <summary>True once owned-namespace building is armed for the local user (or we're the host). -xlinka</summary>
	public bool IsLocalAllocationReady => _localAllocationReady || IsAuthority;

	/// <summary>
	/// Run an action with the allocation context scoped to the local user's own namespace, so everything
	/// built inside - slots, components, sub-slots - is minted into and OWNED by the local user. The entry
	/// point a per-peer spawn uses to build its own equipment. On the host this is the authority byte (a
	/// no-op scope). -xlinka
	/// </summary>
	public IDisposable EnterLocalUserAllocation() => new OwnedAllocationScope(this, ResolveOwnedAllocationByte());

	/// <summary>
	/// Create a slot in the local user's own RefID namespace under the given parent. Networked (others see
	/// it) but OWNED by the local user, so the permission gate lets them keep mutating it with no system
	/// bypass. For a joining user's own equipment - NOT shared world content (that stays host-authoritative
	/// via AddSlot). -xlinka
	/// </summary>
	public Slot AddLocalUserSlot(Slot parent, string name = "Slot")
	{
		if (parent == null) throw new ArgumentNullException(nameof(parent));
		var b = ResolveOwnedAllocationByte();
		if (b == RefIDConstants.AUTHORITY_BYTE)
			return parent.AddSlot(name);

		ReferenceController.OwnedAllocationBlockBegin(b);
		try { return parent.AddSlot(name); }
		finally { ReferenceController.OwnedAllocationBlockEnd(b); }
	}

	// Scoped owned-allocation block. Authority byte means no scope (host already owns everything), so we
	// skip begin/end entirely and the dispose is a no-op. -xlinka
	private sealed class OwnedAllocationScope : IDisposable
	{
		private readonly World _world;
		private readonly byte _byte;
		private bool _active;

		public OwnedAllocationScope(World world, byte userByte)
		{
			_world = world;
			_byte = userByte;
			if (RefIDConstants.IsValidUserByte(userByte))
			{
				_world.ReferenceController.OwnedAllocationBlockBegin(userByte);
				_active = true;
			}
		}

		public void Dispose()
		{
			if (_active)
			{
				_active = false;
				_world.ReferenceController.OwnedAllocationBlockEnd(_byte);
			}
		}
	}

	/// <summary>
	/// Get all users in the world.
	/// </summary>
	public List<User> GetAllUsers()
	{
		lock (_users)
		{
			return new List<User>(_users);
		}
	}

	/// <summary>
	/// Start a new session as host (authority).
	/// </summary>
	public void StartSession(ushort port = 7777, string hostUserName = null!)
	{
		StartSessionNetwork(port);
		CreateHostUser(hostUserName);
	}

	/// <summary>
	/// Start the session network without creating the host user.
	/// Used internally to allow init callback to run before user creation.
	/// </summary>
	private void StartSessionNetwork(ushort port, SessionMetadata metadata = null!)
	{
		if (_session != null)
		{
			LumoraLogger.Warn("Session already started");
			return;
		}

		try
		{
			NetworkInitStart();

			if (metadata != null)
			{
				_session = Session.NewSession(this, port, metadata);
			}
			else
			{
				_session = Session.NewSession(this, port);
			}
			AuthorityID = -1; // This is the host

			_refIDAllocator.Reset();

			// NOTE: State remains InitializingNetwork here!
			// The caller (StartSession factory) will set Running AFTER init callback completes.
			// This allows the init callback to modify the world before the DataModel lock check kicks in.

			LumoraLogger.Log($"Started session network on port {port}");
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"Failed to start session network: {ex.Message}");
			InitializationFailed();
		}
	}

	/// <summary>
	/// Create the host user for a locally hosted session.
	/// </summary>
	public User CreateHostUser(string userName = null!)
	{
		var (rangeStart, rangeEnd) = _refIDAllocator.GetAuthorityIDRange();

		// Reserve the next RefID without advancing the allocation cursor.
		// User.InitializeWorker will advance the cursor for the user and its sync members.
		var userRefId = ReferenceController.PeekID();
		var hostUser = new User();
		var resolvedName = string.IsNullOrWhiteSpace(userName) ? System.Environment.MachineName : userName;

		hostUser.UserName.Value = resolvedName;
		hostUser.UserID.Value = userRefId.ToString();
		hostUser.MachineID.Value = System.Environment.MachineName;
		hostUser.AllocationIDStart.Value = rangeStart;
		hostUser.AllocationIDEnd.Value = rangeEnd;
		hostUser.AllocationID.Value = userRefId.GetUserByte();
		hostUser.IsPresent.Value = true;
		hostUser.PresentInWorld.Value = true;
		hostUser.IsSilenced.Value = false;

		// Set head device type based on VR status
		var inputInterface = Engine.Current?.InputInterface;
		hostUser.HeadDevice.Value = inputInterface?.CurrentHeadOutputDevice ?? HeadOutputDevice.Screen;
		hostUser.VRActive.Value = inputInterface?.IsVRActive ?? false;

		// Set platform
		hostUser.UserPlatform.Value = GetCurrentPlatform();

		LocalUser = hostUser;
		_localUserSet = true; // host's local user is final - keep SetLocalUser's one-shot guard consistent. -xlinka
		AddUserToCollection(hostUser, userRefId, isNewlyCreated: true);
		hostUser.ConfigureLocalTrackingStreams();
		LumoraLogger.Log($"Created host user '{resolvedName}' with RefID {userRefId}");
		return hostUser;
	}

	/// <summary>
	/// Get the current platform type.
	/// </summary>
	private static Platform GetCurrentPlatform()
	{
		if (OperatingSystem.IsWindows())
			return Platform.Windows;
		if (OperatingSystem.IsLinux())
			return Platform.Linux;
		if (OperatingSystem.IsAndroid())
			return Platform.Android;
		return Platform.Other;
	}

	/// <summary>
	/// Join an existing session as client.
	/// </summary>
	public void JoinSession(Uri address)
	{
		if (_session != null)
		{
			LumoraLogger.Warn("Session already active");
			return;
		}

		try
		{
			NetworkInitStart();

			_session = Session.JoinSession(this, new[] { address });

			// Client now waits for JoinGrant from authority
			WaitForJoinGrant();

			// Create loading indicator in current world
			CreateSessionJoinIndicator();
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"Failed to join session: {ex.Message}");
			InitializationFailed();
		}
	}

	/// <summary>
	/// Join an existing session as client (async).
	/// </summary>
	public async Task<bool> JoinSessionAsync(Uri address)
	{
		if (_session != null)
		{
			LumoraLogger.Warn("Session already active");
			return false;
		}

		try
		{
			NetworkInitStart();

			_session = (await Session.JoinSessionAsync(this, new[] { address }))!;
			if (_session == null)
			{
				InitializationFailed();
				return false;
			}

			// Client now waits for JoinGrant from authority
			WaitForJoinGrant();
			return true;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"Failed to join session: {ex.Message}");
			InitializationFailed();
			return false;
		}
	}

	/// <summary>
	/// Called when client receives JoinGrant from authority.
	/// Transitions from WaitingForJoinGrant to InitializingDataModel.
	/// Note: Allocation context switch is handled in SessionSyncManager before SetLocalUser.
	/// </summary>
	public void OnJoinGrantReceived()
	{
		if (_state == WorldState.WaitingForJoinGrant)
		{
			StartDataModelInit();
		}

		// The full-state batch (data channel) can be applied BEFORE this JoinGrant (control channel) sets the
		// local-user target. When that happens our own User was added to the collection while the target was
		// still null, so User.Initialize() skipped the local-user match and nothing retried it - the client
		// ends up with no LocalUser (no actor -> its avatar/hands/laser/movement get permission-denied). Now
		// that the target RefID is known, claim an already-present user here. If it hasn't synced yet this is a
		// no-op and User.Initialize() catches it on add. -xlinka
		TryClaimPendingLocalUser();
	}

	/// <summary>
	/// Assign LocalUser from an already-synced user matching the join grant's target RefID, if it arrived
	/// before the grant. Order-independent companion to the per-user check in User.Initialize().
	/// </summary>
	private void TryClaimPendingLocalUser()
	{
		if (LocalUser != null)
			return;

		var target = _session?.Sync?.LocalUserRefIDToInit ?? RefID.Null;
		if (target.IsNull)
			return;

		foreach (var user in GetAllUsers())
		{
			if (user != null && user.ReferenceID == target)
			{
				SetLocalUser(user);
				LumoraLogger.Log($"OnJoinGrantReceived: claimed already-synced local user '{user.UserName.Value}' (RefID: {target})");
				return;
			}
		}
	}

	/// <summary>
	/// Called when client receives full world state from authority.
	/// Transitions from InitializingDataModel to Running.
	/// </summary>
	public void OnFullStateReceived()
	{
		if (_state != WorldState.InitializingDataModel)
			return;

		// A client needs an actor before going Running, or the whole session is headless (no LocalUser ->
		// no permission actor -> avatar/equipment denied). If the user isn't claimed yet, defer Running a
		// bounded number of pumps and keep retrying. The host never reaches here. -xlinka
		if (IsAuthority || TryReadyLocalUserForRunning())
		{
			StartRunning();
			return;
		}

		_awaitingLocalUserFrames = 0;
		LumoraLogger.Warn($"OnFullStateReceived: full state in but LocalUser not resolved yet " +
			$"(target {_session?.Sync?.LocalUserRefIDToInit}). Deferring Running, retrying up to {MaxAwaitLocalUserFrames} pumps " +
			$"(t+{TimeSinceDataModelInit:F1}s).");
	}

	private bool TryReadyLocalUserForRunning()
	{
		if (LocalUser != null)
			return true;
		TryClaimPendingLocalUser();
		return LocalUser != null;
	}

	/// <summary>
	/// Pumped from the session control-drain while we deferred Running waiting for the local user. Promotes
	/// to Running the moment the user lands, after the bound it goes Running anyway but logs a LOUD error so
	/// a genuinely-missing local user is a visible failure, never silent. -xlinka
	/// </summary>
	private void TickAwaitingLocalUser()
	{
		if (_state != WorldState.InitializingDataModel || IsAuthority)
			return;

		if (TryReadyLocalUserForRunning())
		{
			LumoraLogger.Log($"Local user resolved after {_awaitingLocalUserFrames} deferred pump(s), going Running.");
			StartRunning();
			return;
		}

		if (++_awaitingLocalUserFrames >= MaxAwaitLocalUserFrames)
		{
			LumoraLogger.Error($"JOIN: LocalUser never resolved after {_awaitingLocalUserFrames} pumps " +
				$"(target {_session?.Sync?.LocalUserRefIDToInit}, t+{TimeSinceDataModelInit:F1}s). Entering world WITHOUT a local " +
				$"actor - avatar/equipment will be permission-denied until it heals. This is a join ordering/decode failure.");
			StartRunning();
		}
	}

	/// <summary>Pumped each session control-drain so join progress advances while the world is still pre-Running. -xlinka</summary>
	public void PumpJoinProgress()
	{
		if (_state == WorldState.InitializingDataModel)
			TickAwaitingLocalUser();
	}

	// If LocalUser is set but never gets a Root (spawn never ran, or the body tree never decoded), re-fire
	// spawn after a grace window so a one-frame race doesn't leave the user permanently bodyless. Bounded so
	// a genuinely un-spawnable user doesn't loop forever. Healthy sessions reset every frame and never fire. -xlinka
	private void TickMissingRootWatchdog()
	{
		var lu = LocalUser;
		if (lu == null || lu.Root != null)
		{
			_missingRootFrames = 0;
			return;
		}

		if (++_missingRootFrames < MissingRootGraceFrames)
			return;
		_missingRootFrames = 0;

		if (_respawnAttempts >= MaxRespawnAttempts)
		{
			LumoraLogger.Error($"Missing-root watchdog gave up after {_respawnAttempts} attempts for local user " +
				$"'{lu.UserName.Value}' ({lu.ReferenceID}); user remains bodyless.");
			return;
		}

		_respawnAttempts++;
		LumoraLogger.Warn($"Missing-root watchdog: local user '{lu.UserName.Value}' has no Root after grace - " +
			$"re-firing spawn (attempt {_respawnAttempts}/{MaxRespawnAttempts}).");
		TriggerUserJoinedEvent(lu);
	}

	/// <summary>
	/// Enter network initialization stage.
	/// </summary>
	public void NetworkInitStart()
	{
		var oldState = _state;
		_initState = InitializationState.InitializingNetwork;
		_state = WorldState.InitializingNetwork;
		LumoraLogger.Log("World entering network initialization");
		OnStateChanged?.Invoke(oldState, _state);
	}

	/// <summary>
	/// Enter join grant wait stage (clients only).
	/// </summary>
	public void WaitForJoinGrant()
	{
		if (IsAuthority)
		{
			LumoraLogger.Warn("Authority cannot wait for join grant");
			return;
		}

		var oldState = _state;
		_initState = InitializationState.WaitingForJoinGrant;
		_state = WorldState.WaitingForJoinGrant;
		LumoraLogger.Log("Waiting for join grant");
		OnStateChanged?.Invoke(oldState, _state);
	}

	/// <summary>
	/// Enter data model initialization stage (clients only).
	/// </summary>
	public void StartDataModelInit()
	{
		if (IsAuthority)
		{
			LumoraLogger.Warn("Authority cannot enter data model init");
			return;
		}

		var oldState = _state;
		_initState = InitializationState.InitializingDataModel;
		_state = WorldState.InitializingDataModel;
		_dataModelInitStartTime = TotalTime; // join window opened - clock for bounded local-user wait. -xlinka
		LumoraLogger.Log("Starting data model initialization");
		OnStateChanged?.Invoke(oldState, _state);
	}

	/// <summary>
	/// Mark the world as running.
	/// </summary>
	public void StartRunning()
	{
		if (_state == WorldState.Destroyed)
			return;

		if (!IsAuthority && LocalUser == null)
			LumoraLogger.Error("StartRunning: client going Running with no LocalUser - see prior JOIN error.");

		var oldState = _state;
		_initState = InitializationState.Finished;
		lock (_stateLock)
		{
			_state = WorldState.Running;
			// Fan out to anyone who subscribed before we flipped. Snapshot-and-null so it can't re-fire. -xlinka
			var running = _whenRunning;
			_whenRunning = null;
			running?.Invoke(this);
		}
		LumoraLogger.Log("World is now running");

		// Configure the permission gate for this world's mode. Authority-only: the lock is enforced on
		// the authority (it rejects unauthorized client deltas), and a client never escapes it because
		// the host is the one that accepts/rebroadcasts changes.
		if (IsAuthority)
			WorldModePermissions.Apply(this, Mode);

		// For clients: configure local user's tracking streams now that all sync members are decoded.
		// This was deferred from SetLocalUser because streams weren't decoded yet during FullBatch processing.
		if (!IsAuthority && LocalUser != null)
		{
			LocalUser.ConfigureLocalTrackingStreams();
			LumoraLogger.Log($"Configured tracking streams for local user '{LocalUser.UserName.Value}' on world Running");
		}

		OnStateChanged?.Invoke(oldState, _state);
	}

	/// <summary>
	/// Mark initialization failure.
	/// </summary>
	public void InitializationFailed(string? reason = null)
	{
		var oldState = _state;
		_initState = InitializationState.Failed;
		_state = WorldState.Failed;
		InitializationFailureReason = string.IsNullOrWhiteSpace(reason) ? "World initialization failed" : reason;
		LumoraLogger.Log($"World initialization failed: {InitializationFailureReason}");
		OnStateChanged?.Invoke(oldState, _state);
	}

	/// <summary>
	/// Queue action to run synchronously on next update.
	/// Thread-safe for cross-thread calls.
	/// </summary>
	public void RunSynchronously(Action action)
	{
		if (IsDisposed) return;

		lock (_syncLock)
		{
			_synchronousActions.Enqueue(action);
		}
	}

	/// <summary>
	/// Queue action to run after a number of update cycles.
	/// </summary>
	/// <param name="updateCount">Number of updates to wait</param>
	/// <param name="action">Action to execute</param>
	public void RunInUpdates(int updateCount, Action action)
	{
		if (IsDisposed || action == null) return;

		if (updateCount <= 0)
		{
			RunSynchronously(action);
			return;
		}

		// Wrap to count down updates
		int remaining = updateCount;
		void CountdownAction()
		{
			remaining--;
			if (remaining <= 0)
				action();
			else
				RunSynchronously(CountdownAction);
		}
		RunSynchronously(CountdownAction);
	}

	/// <summary>
	/// Process all queued synchronous actions.
	/// </summary>
	private void ProcessSynchronousActions()
	{
		lock (_syncLock)
		{
			while (_synchronousActions.Count > 0)
			{
				try
				{
					_synchronousActions.Dequeue()?.Invoke();
				}
				catch (Exception ex)
				{
					LumoraLogger.Error($"World: Error in synchronous action: {ex}");
				}
			}
		}
	}

	/// <summary>
	/// Leave the current session.
	/// </summary>
	public void LeaveSession()
	{
		if (_session != null)
		{
			_session.Dispose();
			_session = null!;
			LumoraLogger.Log("Left session");
		}
	}

	/// <summary>
	/// Increment the sync tick counter.
	/// Called once per sync cycle by SessionSyncManager.
	/// </summary>
	public void IncrementSyncTick()
	{
		SyncTick++;
	}

	/// <summary>
	/// Increment the state version counter. Only the authority owns the state version, and only by
	/// incrementing it - a non-authority calling this is a bug.
	/// </summary>
	public void IncrementStateVersion()
	{
		if (!IsAuthority)
		{
			throw new InvalidOperationException("Only the host can increment the state version");
		}
		StateVersion++;
	}

	/// <summary>
	/// Adopt an authority state version (clients only, applying host updates). The host never adopts a
	/// foreign version (it only increments), and the version can only move forward - a stale/reordered
	/// or malicious lower version is rejected so a peer can't roll our view of authority state backward.
	/// We log-and-ignore rather than throw, so a stale batch doesn't abort the rest of the sync drain.
	/// </summary>
	public void SetStateVersion(ulong version)
	{
		if (IsAuthority)
		{
			LumoraLogger.Warn("SetStateVersion called on the host - the host increments its own version, it does not adopt one. Ignoring.");
			return;
		}
		if (version < StateVersion)
		{
			LumoraLogger.Warn($"Rejecting a backward state version: have {StateVersion}, asked to set {version}. Ignoring (anti-rewind).");
			return;
		}
		StateVersion = version;
	}

	/// <summary>
	/// Process the World update loop with comprehensive stages.
	/// Handles all update phases for the world simulation.
	/// Called by EngineDriver or similar wrapper.
	/// </summary>
	public void Update(double delta)
	{
		if (_state != WorldState.Running) return;

		// Acquire Implementer lock for main thread modifications
		_hookManager?.ImplementerLock(System.Threading.Thread.CurrentThread);
		try
		{
			var scaledDelta = delta * TimeScale;
			TotalTime += scaledDelta;
			LastDelta = (float)scaledDelta;

			// Per-stage timing so a lock-up frame logs WHICH stage ate it (instead of guessing). GetTimestamp is
			// allocation-free; only logs on a genuinely slow frame so it's not spam. -xlinka
			long _ts = System.Diagnostics.Stopwatch.GetTimestamp();
			double _mspt = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			double Lap() { long n = System.Diagnostics.Stopwatch.GetTimestamp(); double ms = (n - _ts) * _mspt; _ts = n; return ms; }

			// Stage 0: Poll network transport so packets are dispatched before any world logic runs
			_session?.Poll();

			// Stage 1: Process synchronous actions (immediate state changes)
			ProcessSynchronousActions();
			double msSync = Lap();

			// Stage 1.5: Process completed asset fetch tasks
			Networking.AssetFetcher.ProcessQueue();

			// Stage 2: Process world events (user joined/left, focus changes)
			RunWorldEvents();

			// Stage 2.5: Register any newly used worker types (authority only)
			if (IsAuthority)
			{
				Workers?.RegisterTypes();
			}

			// Stage 3: Process input for this world (if focused)
			if (_focus == WorldFocus.Focused)
			{
				ProcessInput((float)scaledDelta);
			}

			// Stage 4: Update coroutines
			UpdateCoroutines((float)scaledDelta);
			double msPre = Lap();

			// Stage 5: Update components (main update)
			UpdateComponents((float)scaledDelta);
			double msComp = Lap();

			// Stage 5.5: Apply component changes (from sync field updates)
			_updateManager?.RunChangeApplications();
			double msChange = Lap();

			// Stage 5.6: Fire deferred WorldTransformChanged events. Before hooks, so a handler
			// that re-drives a transform gets pushed to the engine this same frame.
			_updateManager?.ProcessMovedSlots();

			_updateManager?.ProcessHookUpdates((float)scaledDelta);
			double msHooks = Lap();

			// Stage 6: Process changed elements
			ProcessChangedElements();

			// Stage 7: Process destructions
			ProcessDestructions();

			// Stage 9: Clean up trash bin
			_trashBin?.Update();
			double msEnd = Lap();

			double msTotal = msSync + msPre + msComp + msChange + msHooks + msEnd;
			if (msTotal > 25.0)
				LumoraLogger.Warn($"World.Update SLOW {msTotal:F0}ms: sync={msSync:F0} pre={msPre:F0} comp={msComp:F0} change={msChange:F0} hooks={msHooks:F0} end={msEnd:F0}");

			// Stage 10: Signal sync manager that world refresh is complete
			// Sync thread waits for this before new-user initialization
			// This ensures OnUserJoined events have fired and avatars are created
			if (_session?.Sync != null)
			{
				_session.Sync.SignalRefreshFinished();
			}

			// Stage 11: Update local user stats (FPS)
			if (LocalUser != null && delta > 0)
			{
				LocalUser.FPS.Value = (float)(1.0 / delta);
			}

			// Stage 12: missing-root respawn watchdog - heals a bodyless local user. -xlinka
			TickMissingRootWatchdog();
		}
		finally
		{
			_hookManager?.ImplementerUnlock();
		}
	}

	/// <summary>
	/// Fixed update for physics and deterministic operations.
	/// </summary>
	public void FixedUpdate(double fixedDelta)
	{
		if (_state != WorldState.Running) return;

		_hookManager?.ImplementerLock(System.Threading.Thread.CurrentThread);
		try
		{
			var scaledDelta = fixedDelta * TimeScale;

			// Update physics for all physics components
			UpdatePhysics((float)scaledDelta);
		}
		finally
		{
			_hookManager?.ImplementerUnlock();
		}
	}

	/// <summary>
	/// Late update for cameras and final positioning.
	/// </summary>
	public void LateUpdate(double delta)
	{
		if (_state != WorldState.Running) return;

		_hookManager?.ImplementerLock(System.Threading.Thread.CurrentThread);
		try
		{
			var scaledDelta = delta * TimeScale;

			// Update cameras and final transforms
			UpdateCameras((float)scaledDelta);

			_updateManager?.ProcessHookUpdates((float)scaledDelta);
		}
		finally
		{
			_hookManager?.ImplementerUnlock();
		}
	}

	/// <summary>
	/// Process input for this world.
	/// </summary>
	private void ProcessInput(float delta)
	{
		// Process input through input manager
		// This would handle VR controllers, keyboard, mouse for this world
	}

	/// <summary>
	/// Update coroutines for this world.
	/// </summary>
	private void UpdateCoroutines(float delta)
	{
		// Update world-specific coroutines
	}

	/// <summary>
	/// Update all components in the world.
	/// </summary>
	private void UpdateComponents(float delta)
	{
		_updateManager?.RunStartups();
		_updateManager?.RunStartupRetries(); // re-drive anything whose startup threw (transient join-window denials)
		_updateManager?.RunUpdates(delta);
	}

	/// <summary>
	/// Recursively update slots and their components.
	/// </summary>
	private void UpdateSlotsRecursive(Slot slot, float delta)
	{
		if (slot == null || !slot.ActiveSelf)
			return;

		// Update components on this slot
		foreach (var component in slot.Components)
		{
			if (component.Enabled)
			{
				component.OnUpdate(delta);
			}
		}

		// Update child slots
		foreach (var child in slot.Children)
		{
			UpdateSlotsRecursive(child, delta);
		}
		foreach (var child in slot.LocalChildren)
		{
			UpdateSlotsRecursive(child, delta);
		}
	}

	/// <summary>
	/// Process elements that have changed this frame.
	/// </summary>
	private void ProcessChangedElements()
	{
		lock (_syncLock)
		{
			foreach (var element in _dirtyElements)
			{
				// Process changed elements for network sync
				if (_session != null && IsAuthority)
				{
					// Queue state changes for network broadcast
				}
			}
			_dirtyElements.Clear();
		}
	}

	/// <summary>
	/// Process pending destructions.
	/// </summary>
	private void ProcessDestructions()
	{
		_updateManager?.RunDestructions();
	}

	/// <summary>
	/// Update physics components.
	/// </summary>
	private void UpdatePhysics(float fixedDelta)
	{
		// Update all physics components with fixed timestep
		// This will be called by physics components that register for fixed updates
		UpdatePhysicsRecursive(RootSlot, fixedDelta);
	}

	/// <summary>
	/// Recursively update physics on slots.
	/// </summary>
	private void UpdatePhysicsRecursive(Slot slot, float fixedDelta)
	{
		if (slot == null || !slot.ActiveSelf)
			return;

		// Update physics components on this slot
		foreach (var component in slot.Components)
		{
			if (component.Enabled)
			{
				// Check if component has physics
				// For now, call a virtual method that components can override
				component.OnFixedUpdate(fixedDelta);
			}
		}

		// Update child slots
		foreach (var child in slot.Children)
		{
			UpdatePhysicsRecursive(child, fixedDelta);
		}
		foreach (var child in slot.LocalChildren)
		{
			UpdatePhysicsRecursive(child, fixedDelta);
		}
	}

	/// <summary>
	/// Update camera components.
	/// </summary>
	private void UpdateCameras(float delta)
	{
		// Update all camera components for final positioning
		UpdateCamerasRecursive(RootSlot, delta);
	}

	/// <summary>
	/// Recursively update cameras on slots.
	/// </summary>
	private void UpdateCamerasRecursive(Slot slot, float delta)
	{
		if (slot == null || !slot.ActiveSelf)
			return;

		// Update camera components on this slot
		foreach (var component in slot.Components)
		{
			if (component.Enabled)
			{
				// Call late update for cameras and final positioning
				component.OnLateUpdate(delta);
			}
		}

		// Update child slots
		foreach (var child in slot.Children)
		{
			UpdateCamerasRecursive(child, delta);
		}
		foreach (var child in slot.LocalChildren)
		{
			UpdateCamerasRecursive(child, delta);
		}
	}

	/// <summary>
	/// Register a component to receive world events.
	/// </summary>
	public void RegisterEventReceiver(IWorldEventReceiver receiver)
	{
		foreach (WorldEvent eventType in Enum.GetValues(typeof(WorldEvent)))
		{
			if (receiver.HasEventHandler(eventType))
			{
				_worldEventReceivers[(int)eventType].Add(receiver);
			}
		}
	}

	/// <summary>
	/// Unregister a component from receiving world events.
	/// </summary>
	public void UnregisterEventReceiver(IWorldEventReceiver receiver)
	{
		foreach (WorldEvent eventType in Enum.GetValues(typeof(WorldEvent)))
		{
			if (receiver.HasEventHandler(eventType))
			{
				_worldEventReceivers[(int)eventType].Remove(receiver);
			}
		}
	}

	/// <summary>
	/// Trigger user joined events to all registered receivers.
	/// Should be called from AddUser.
	/// </summary>
	private void TriggerUserJoinedEvent(User user)
	{
		_joinedUsers.Add(user);
	}

	/// <summary>
	/// Trigger user left events to all registered receivers.
	/// Should be called from RemoveUser.
	/// </summary>
	private void TriggerUserLeftEvent(User user)
	{
		_leftUsers.Add(user);
	}

	/// <summary>
	/// Process all pending world events and notify receivers.
	/// Called during world update cycle.
	/// </summary>
	private void RunWorldEvents()
	{
		// Process user joined events
		if (_joinedUsers.Count > 0)
		{
			foreach (var user in _joinedUsers)
			{
				foreach (var receiver in _worldEventReceivers[(int)WorldEvent.OnUserJoined])
				{
					try
					{
						receiver.OnUserJoined(user);
					}
					catch (Exception ex)
					{
						LumoraLogger.Error($"Error in OnUserJoined handler: {ex.Message}");
					}
				}
			}
			_joinedUsers.Clear();
		}

		// Process user left events
		if (_leftUsers.Count > 0)
		{
			foreach (var user in _leftUsers)
			{
				foreach (var receiver in _worldEventReceivers[(int)WorldEvent.OnUserLeft])
				{
					try
					{
						receiver.OnUserLeft(user);
					}
					catch (Exception ex)
					{
						LumoraLogger.Error($"Error in OnUserLeft handler: {ex.Message}");
					}
				}
			}
			_leftUsers.Clear();
		}
	}

	/// <summary>
	/// Add a slot to the world.
	/// </summary>
	public Slot AddSlot(string name = "Slot")
	{
		return RootSlot.AddSlot(name);
	}

	/// <summary>
	/// Create a SessionJoinIndicator in the current world.
	/// Shows a loading indicator in the currently focused world.
	/// </summary>
	private void CreateSessionJoinIndicator()
	{
		// Only create indicator for client worlds that have a local user
		if (IsAuthority || LocalUser == null || WorldManager?.FocusedWorld == null)
			return;

		try
		{
			// Create indicator in the currently focused world
			var currentWorld = WorldManager.FocusedWorld;
			
			RunSynchronously(() =>
			{
				SessionJoinIndicator.CreateIndicatorAsync(currentWorld, this, _session!.Sync, indicator =>
				{
					if (indicator != null)
					{
						LumoraLogger.Log($"Created session join indicator in world '{currentWorld.Name}' for joining '{Name}'");
					}
					else
					{
						LumoraLogger.Warn("Failed to create session join indicator");
					}
				});
			});
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"Error creating session join indicator: {ex.Message}");
		}
	}

	/// <summary>
	/// Dispose of the world and clean up resources.
	/// </summary>
	public void Dispose()
	{
		// 1. Check double-dispose
		if (IsDisposed)
		{
			LumoraLogger.Warn($"World: Already disposed world '{WorldName.Value}'");
			return;
		}

		LumoraLogger.Log($"World: Disposing world '{WorldName.Value}'");

		// 2. Mark as destroyed
		IsDestroyed = true;
		IsDisposed = true;

		// Fire the destroyed edge for late-or-early subscribers. This is the real teardown path (the
		// WorldManager drains its destroy queue into Dispose), so it's the load-bearing fire site.
		// Snapshot-and-null under the lock means it fires exactly once even if something flipped the
		// state earlier. -xlinka
		Action<World>? destroyed;
		lock (_stateLock)
		{
			if (_state != WorldState.Destroyed)
				_state = WorldState.Destroyed;
			destroyed = _whenDestroyed;
			_whenDestroyed = null;
		}
		try { destroyed?.Invoke(this); }
		catch (Exception ex) { LumoraLogger.Error($"World: Error in WhenDestroyed handler during dispose: {ex}"); }

		// 3. Process remaining synchronous actions
		lock (_syncLock)
		{
			while (_synchronousActions.Count > 0)
			{
				try
				{
					_synchronousActions.Dequeue()?.Invoke();
				}
				catch (Exception ex)
				{
					LumoraLogger.Error($"World: Error in disposal synchronous action: {ex}");
				}
			}
		}

		// 4. Dispose session
		try
		{
			_session?.Dispose();
			_session = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error disposing session: {ex.Message}");
		}

		// Dispose sync controller
		try
		{
			SyncController?.Dispose();
			SyncController = null!;
			LinkManager?.Dispose();
			LinkManager = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error disposing sync controller: {ex.Message}");
		}

		// 5. Dispose all users
		foreach (var user in _users.ToList())
		{
			try
			{
				user?.Dispose();
			}
			catch (Exception ex)
			{
				LumoraLogger.Error($"World: Error disposing user: {ex.Message}");
			}
		}

		// 8. Dispose all components in all slots
		// 9. Dispose all slots
		try
		{
			RootSlot?.Destroy();
			// Destroy queues component OnDestroy callbacks; flush them before managers
			// and the ReferenceController are cleared so components can unregister cleanly.
			_updateManager?.RunDestructions();
			RootSlot = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error disposing root slot: {ex.Message}");
		}

		// 10. Clear all collections
		_users?.Clear();
		_dirtyElements?.Clear();
		_slotsByTag?.Clear();
		_rootSlots?.Clear();
		_joinedUsers?.Clear();
		_leftUsers?.Clear();

		// Clear event receivers
		if (_worldEventReceivers != null)
		{
			for (int i = 0; i < _worldEventReceivers.Length; i++)
			{
				_worldEventReceivers[i]?.Clear();
			}
		}

		// 11. Dispose managers
		try
		{
			_hookManager?.Dispose();
			_hookManager = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error disposing hook manager: {ex.Message}");
		}

		try
		{
			// TrashBin doesn't have Dispose, just clear reference
			_trashBin = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error clearing trash bin: {ex.Message}");
		}

		try
		{
			// UpdateManager doesn't have Dispose, just clear reference
			_updateManager = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error clearing update manager: {ex.Message}");
		}

		try
		{
			_refIDAllocator = null!;
			_hookTypes = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error clearing managers: {ex.Message}");
		}

		try
		{
			ReferenceController?.Dispose();
			ReferenceController = null!;
		}
		catch (Exception ex)
		{
			LumoraLogger.Error($"World: Error disposing reference controller: {ex.Message}");
		}

		LumoraLogger.Log($"World: Disposed world '{WorldName.Value}'");
	}
}

