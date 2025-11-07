using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Aquamarine.Source.Networking.Session;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// Represents a World instance that contains Slots, Components, and Users.
/// Implements authority-based synchronization model.
/// </summary>
public partial class World : Node
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
		Overlay
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
	private ConnectorManager _connectorManager;
	private TrashBin _trashBin;
	private RefIDAllocator _refIDAllocator;
	private WorldFocus _focus = WorldFocus.Background;
	private static int _worldEventTypeCount = Enum.GetValues(typeof(WorldEvent)).Length;

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
	/// </summary>
	public bool IsAuthority => AuthorityID == -1 || AuthorityID == Multiplayer.GetUniqueId();

	/// <summary>
	/// Session for networking.
	/// </summary>
	public Session Session => _session;

	/// <summary>
	/// Thread-safe connector manager for world modifications.
	/// </summary>
	public ConnectorManager ConnectorManager => _connectorManager;

	/// <summary>
	/// Trash bin for temporarily holding deleted objects.
	/// </summary>
	public TrashBin TrashBin => _trashBin;

	/// <summary>
	/// RefID allocator for preventing ID conflicts between users.
	/// </summary>
	public RefIDAllocator RefIDAllocator => _refIDAllocator;

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
		_connectorManager = new ConnectorManager(this);
		_trashBin = new TrashBin(this);
		_refIDAllocator = new RefIDAllocator(this);
		
		// Initialize event receiver arrays
		int length = Enum.GetValues(typeof(WorldEvent)).Length;
		_worldEventReceivers = new List<IWorldEventReceiver>[length];
		for (int i = 0; i < length; i++)
		{
			_worldEventReceivers[i] = new List<IWorldEventReceiver>();
		}
	}

	public override void _Ready()
	{
		base._Ready();
		Initialize();
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
		AddChild(RootSlot);

		// Network session is started via StartSession() or JoinSession()
		// No automatic network initialization

		_state = WorldState.Running;
		AquaLogger.Log($"World '{WorldName.Value}' initialized successfully - now Running");
	}

	/// <summary>
	/// Get the UserRoot component for managing users in this world.
	/// Creates it if it doesn't exist .
	/// </summary>
	public Components.UserRootComponent GetOrCreateUserRoot()
	{
		// Try to find existing UserRoot slot
		var userRootSlot = FindSlotsByTag("UserRoot").FirstOrDefault();

		if (userRootSlot == null)
		{
			// Create UserRoot slot under Root 
			userRootSlot = RootSlot.AddSlot("Users");
			userRootSlot.Tag.Value = "UserRoot";
			AquaLogger.Log("Created UserRoot slot in world");
		}

		// Get or attach UserRootComponent
		var userRootComponent = userRootSlot.GetComponent<Components.UserRootComponent>();
		if (userRootComponent == null)
		{
			userRootComponent = userRootSlot.AttachComponent<Components.UserRootComponent>();
			AquaLogger.Log("Attached UserRootComponent");
		}

		return userRootComponent;
	}

	/// <summary>
	/// Get all spawn points in the world .
	/// </summary>
	public IEnumerable<Components.SpawnPointComponent> GetSpawnPoints()
	{
		var spawnSlots = FindSlotsByTag("spawn");
		foreach (var slot in spawnSlots)
		{
			var spawnPoint = slot.GetComponent<Components.SpawnPointComponent>();
			if (spawnPoint != null && spawnPoint.CanSpawn())
			{
				yield return spawnPoint;
			}
		}
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

		_connectorManager?.Dispose();

		QueueFree();
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
	/// </summary>
	public override void _Process(double delta)
	{
		if (_state != WorldState.Running) return;

		var scaledDelta = delta * TimeScale;
		TotalTime += scaledDelta;

		// Run world events (user joined/left, etc.)
		RunWorldEvents();

		// Update trash bin (clean up expired entries)
		_trashBin?.Update();

		// Note: Slots and Components handle their own updates via _Process
		// This is where we would batch network synchronization
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
}
