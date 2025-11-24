using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Networking.Session;
using AquaLogger = Lumora.Core.Logging.Logger;

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

	private readonly Dictionary<ulong, IWorldElement> _elements = new();
	private readonly HashSet<IWorldElement> _dirtyElements = new();
	private readonly Dictionary<string, List<Slot>> _slotsByTag = new();
	private readonly List<Slot> _rootSlots = new();
	private readonly List<User> _users = new();
	private readonly List<User> _joinedUsers = new();
	private readonly List<User> _leftUsers = new();
	private readonly List<IWorldEventReceiver>[] _worldEventReceivers;
	private WorldState _state = WorldState.Created;
	private Session _session;
	private HookManager _hookManager;
	private TrashBin _trashBin;
	private RefIDAllocator _refIDAllocator;
	private WorldFocus _focus = WorldFocus.Background;
	private HookTypeRegistry _hookTypes;
	private UpdateManager _updateManager;
	private Queue<Action> _synchronousActions = new Queue<Action>();
	private object _syncLock = new object();

	// Static global hook type registry (shared across all worlds)
	private static HookTypeRegistry _staticHookTypes = new HookTypeRegistry();

	// Platform hook for world rendering
	public IWorldHook Hook { get; set; }

	// Godot scene access - set by WorldHook
	public object GodotSceneRoot { get; set; }

	// Reference to the WorldManager that owns this World
	public Management.WorldManager WorldManager { get; internal set; }

	private static int _worldEventTypeCount = Enum.GetValues(typeof(WorldEvent)).Length;

	/// <summary>
	/// Global hook type registry (static, shared across all worlds).
	/// </summary>
	public static HookTypeRegistry HookTypes => _staticHookTypes;

	/// <summary>
	/// Current state of the World.
	/// </summary>
	public WorldState State => _state;

	/// <summary>
	/// The root Slot of this World.
	/// </summary>
	public Slot RootSlot { get; private set; }

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
	public User LocalUser { get; private set; }

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
	/// Event triggered when a Slot is added to the World.
	/// </summary>
	public event Action<Slot> OnSlotAdded;

	/// <summary>
	/// Event triggered when a Slot is removed from the World.
	/// </summary>
	public event Action<Slot> OnSlotRemoved;

	public World()
	{
		WorldName = new Sync<string>(null, "New World");
		SessionID = new Sync<string>(null, Guid.NewGuid().ToString());
		_hookManager = new HookManager(this);
		_trashBin = new TrashBin(this);
		_refIDAllocator = new RefIDAllocator(this);
		_hookTypes = new HookTypeRegistry();
		_updateManager = new UpdateManager(this);

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
	public static World LocalWorld(Engine engine, string name, Action<World> init = null)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.AuthorityID = -1; // Local-only (no authority)
		world.LocalID = -1;
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world
		world.Initialize();

		// Create local user
		world.CreateHostUser("LocalUser");

		// Run initialization callback
		init?.Invoke(world);

		// Start running
		world._state = WorldState.Running;
		AquaLogger.Log($"Local world '{name}' created and started");

		return world;
	}

	/// <summary>
	/// Start a hosted session (authority/server).
	/// </summary>
	public static World StartSession(Engine engine, string name, ushort port, string hostUserName = null, Action<World> init = null)
	{
		var world = new World();
		world.WorldName.Value = name;
		world.SessionID.Value = "S-" + Guid.NewGuid().ToString();
		world.AuthorityID = 0; // This instance is authority
		world.LocalID = 0;
		world.IsDestroyed = false;
		world.IsDisposed = false;

		// Initialize world
		world.Initialize();

		// Start session (creates LNL listener)
		world._state = WorldState.InitializingNetwork;
		world.StartSession(port, hostUserName);

		// Run initialization callback
		init?.Invoke(world);

		// Start running
		world._state = WorldState.Running;
		AquaLogger.Log($"Session '{name}' started on port {port}");

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

		// Initialize world
		world.Initialize();

		// Join session (connects to LNL server)
		world._state = WorldState.InitializingNetwork;
		world.JoinSession(address);

		// World will transition to Running when connection succeeds
		AquaLogger.Log($"Joining session at {address}");

		return world;
	}

	/// <summary>
	/// Initialize the World and create the root Slot.
	/// </summary>
	public void Initialize()
	{
		if (_state != WorldState.Created) return;

		_state = WorldState.InitializingDataModel;
		AquaLogger.Log("World entering InitializingDataModel stage");

		// Create root Slot
		RootSlot = new Slot();
		RootSlot.SlotName.Value = "Root";
		RootSlot.Initialize(this);
		RegisterSlot(RootSlot);

		// Note: Godot scene attachment handled by WorldDriver wrapper
		// World itself is pure C# and doesn't use AddChild

		// Network session is started via StartSession() or JoinSession()
		// No automatic network initialization

		_state = WorldState.Running;
		AquaLogger.Log($"World '{WorldName.Value}' initialized successfully - now Running");
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
			AquaLogger.Log("Created Users container slot in world");
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

		_elements[slot.RefID] = slot;

		if (!string.IsNullOrEmpty(slot.Tag.Value))
		{
			if (!_slotsByTag.TryGetValue(slot.Tag.Value, out var slots))
			{
				slots = new List<Slot>();
				_slotsByTag[slot.Tag.Value] = slots;
			}
			slots.Add(slot);
		}

		if (slot.Parent == null)
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

		_elements.Remove(slot.RefID);

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
		_elements[component.RefID] = component;
	}

	/// <summary>
	/// Unregister a Component from the World.
	/// </summary>
	internal void UnregisterComponent(Component component)
	{
		if (component == null) return;
		_elements.Remove(component.RefID);
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
	/// Find a world element by its RefID.
	/// </summary>
	public IWorldElement FindElement(ulong refID)
	{
		_elements.TryGetValue(refID, out var element);
		return element;
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

		return null;
	}

	/// <summary>
	/// Destroy the World and all its contents.
	/// </summary>
	public void DestroyWorld()
	{
		if (_state == WorldState.Destroyed) return;

		AquaLogger.Log($"Destroying world '{WorldName.Value}'...");

		_state = WorldState.Destroyed;

		// Destroy all root slots (which will cascade to children)
		foreach (var slot in _rootSlots.ToArray())
		{
			slot.Destroy();
		}

		_elements.Clear();
		_dirtyElements.Clear();
		_slotsByTag.Clear();
		_rootSlots.Clear();
		_users.Clear();

		_hookManager?.Dispose();

		// Note: Godot scene cleanup handled by WorldDriver wrapper
	}

	/// <summary>
	/// Add a user to the world.
	/// </summary>
	public void AddUser(User user)
	{
		if (user == null) return;

		lock (_users)
		{
			if (!_users.Contains(user))
			{
				_users.Add(user);
				_elements[user.ReferenceID] = user;
				AquaLogger.Log($"User added to world: {user.UserName.Value}");
				
				// Trigger user joined event
				TriggerUserJoinedEvent(user);
			}
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
			_elements.Remove(user.ReferenceID);
			AquaLogger.Log($"User removed from world: {user.UserName.Value}");
			
			// Trigger user left event
			TriggerUserLeftEvent(user);
		}
	}

	/// <summary>
	/// Set the local user (client's own user).
	/// </summary>
	public void SetLocalUser(User user)
	{
		LocalUser = user;
		AddUser(user);
		AquaLogger.Log($"Local user set: {user.UserName.Value}");
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
	public void StartSession(ushort port = 7777, string hostUserName = null)
	{
		if (_session != null)
		{
			AquaLogger.Warn("Session already started");
			return;
		}

		try
		{
			_state = WorldState.InitializingNetwork;
			AquaLogger.Log("World entering InitializingNetwork stage (host)");

			_session = Session.NewSession(this, port);
			AuthorityID = -1; // This is the host

			_refIDAllocator.Reset();
			CreateHostUser(hostUserName);

			// Host doesn't need to wait for join grant, go straight to running
			if (_state == WorldState.InitializingNetwork)
			{
				_state = WorldState.Running;
			}

			AquaLogger.Log($"Started session as host on port {port} - now Running");
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"Failed to start session: {ex.Message}");
			_state = WorldState.Failed;
		}
	}

	/// <summary>
	/// Create the host user for a locally hosted session.
	/// </summary>
	public User CreateHostUser(string userName = null)
	{
		var (rangeStart, rangeEnd) = _refIDAllocator.GetAuthorityIDRange();

		var hostUser = new User(this, rangeStart);
		var resolvedName = string.IsNullOrWhiteSpace(userName) ? System.Environment.MachineName : userName;

		hostUser.UserName.Value = resolvedName;
		hostUser.UserID.Value = rangeStart.ToString();
		hostUser.AllocationIDStart.Value = rangeStart;
		hostUser.AllocationIDEnd.Value = rangeEnd;
		hostUser.IsPresent.Value = true;
		hostUser.IsSilenced.Value = false;

		SetLocalUser(hostUser);
		AquaLogger.Log($"Created host user '{resolvedName}' with RefID {rangeStart:X16}");
		return hostUser;
	}

	/// <summary>
	/// Join an existing session as client.
	/// </summary>
	public void JoinSession(Uri address)
	{
		if (_session != null)
		{
			AquaLogger.Warn("Session already active");
			return;
		}

		try
		{
			_state = WorldState.InitializingNetwork;
			AquaLogger.Log("World entering InitializingNetwork stage (client)");

			_session = Session.JoinSession(this, new[] { address });

			// Client now waits for JoinGrant from authority
			_state = WorldState.WaitingForJoinGrant;
			AquaLogger.Log($"Joined session at {address} - waiting for JoinGrant");
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"Failed to join session: {ex.Message}");
			_state = WorldState.Failed;
		}
	}

	/// <summary>
	/// Called when client receives JoinGrant from authority.
	/// Transitions from WaitingForJoinGrant to Running.
	/// </summary>
	public void OnJoinGrantReceived()
	{
		if (_state == WorldState.WaitingForJoinGrant)
		{
			_state = WorldState.Running;
			AquaLogger.Log("World received JoinGrant - now Running");
		}
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
					AquaLogger.Error($"World: Error in synchronous action: {ex.Message}");
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
			_session = null;
			AquaLogger.Log("Left session");
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
	/// Increment the state version counter.
	/// Called whenever the authority makes a state change.
	/// </summary>
	public void IncrementStateVersion()
	{
		StateVersion++;
	}

	/// <summary>
	/// Set the state version (used when receiving authority updates).
	/// </summary>
	public void SetStateVersion(ulong version)
	{
		StateVersion = version;
	}

	/// <summary>
	/// Process the World update loop.
	/// Called by EngineDriver or similar wrapper.
	/// </summary>
	public void Update(double delta)
	{
		if (_state != WorldState.Running) return;

		var scaledDelta = delta * TimeScale;
		TotalTime += scaledDelta;

		// Process queued synchronous actions
		ProcessSynchronousActions();

		// Run world events (user joined/left, etc.)
		RunWorldEvents();

		// Update trash bin (clean up expired entries)
		_trashBin?.Update();

		// Process hook updates (creates/updates Godot nodes)
		_updateManager?.ProcessHookUpdates();
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
						AquaLogger.Error($"Error in OnUserJoined handler: {ex.Message}");
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
						AquaLogger.Error($"Error in OnUserLeft handler: {ex.Message}");
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
	/// Dispose of the world and clean up resources.
	/// </summary>
	public void Dispose()
	{
		// 1. Check double-dispose
		if (IsDisposed)
		{
			AquaLogger.Warn($"World: Already disposed world '{WorldName.Value}'");
			return;
		}

		AquaLogger.Log($"World: Disposing world '{WorldName.Value}'");

		// 2. Mark as destroyed
		IsDestroyed = true;
		IsDisposed = true;

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
					AquaLogger.Error($"World: Error in disposal synchronous action: {ex.Message}");
				}
			}
		}

		// 4. Dispose session
		try
		{
			_session?.Dispose();
			_session = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error disposing session: {ex.Message}");
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
				AquaLogger.Error($"World: Error disposing user: {ex.Message}");
			}
		}

		// 8. Dispose all components in all slots
		if (_elements != null)
		{
			foreach (var kvp in _elements.ToList())
			{
				try
				{
					if (kvp.Value is Component component)
					{
						component.Destroy();
					}
				}
				catch (Exception ex)
				{
					AquaLogger.Error($"World: Error disposing component: {ex.Message}");
				}
			}
		}

		// 9. Dispose all slots
		try
		{
			RootSlot?.Destroy();
			RootSlot = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error disposing root slot: {ex.Message}");
		}

		if (_elements != null)
		{
			foreach (var kvp in _elements.ToList())
			{
				try
				{
					if (kvp.Value is Slot slot && slot != RootSlot)
					{
						slot.Destroy();
					}
				}
				catch (Exception ex)
				{
					AquaLogger.Error($"World: Error disposing slot: {ex.Message}");
				}
			}
		}

		// 10. Clear all collections
		_users?.Clear();
		_elements?.Clear();
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
			_hookManager = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error disposing hook manager: {ex.Message}");
		}

		try
		{
			// TrashBin doesn't have Dispose, just clear reference
			_trashBin = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error clearing trash bin: {ex.Message}");
		}

		try
		{
			// UpdateManager doesn't have Dispose, just clear reference
			_updateManager = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error clearing update manager: {ex.Message}");
		}

		try
		{
			_refIDAllocator = null;
			_hookTypes = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"World: Error clearing managers: {ex.Message}");
		}

		AquaLogger.Log($"World: Disposed world '{WorldName.Value}'");
	}
}
