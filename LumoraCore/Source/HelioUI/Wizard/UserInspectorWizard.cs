using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Math;
using Lumora.Core.Components;

namespace Lumora.Core.HelioUI;

/// <summary>
/// View modes for the user inspector.
/// </summary>
public enum UserInspectorView
{
	/// <summary>Local user info and actions.</summary>
	LocalUser,
	/// <summary>All users in world.</summary>
	WorldUsers,
	/// <summary>Network streams and bandwidth.</summary>
	Network,
	/// <summary>Avatar components and tracking.</summary>
	Avatar,
	/// <summary>User permissions.</summary>
	Permissions
}

/// <summary>
/// User Inspector Wizard - comprehensive user and world information display.
/// Enhanced version with multiple views, user actions, and live updating.
/// </summary>
[ComponentCategory("HelioUI/Wizard")]
public class UserInspectorWizard : HelioWizardForm
{
	// ===== CONFIGURATION =====

	protected override float2 CanvasSize => new float2(900f, 1100f);
	protected override float WizardPixelScale => 800f;
	protected override string WizardTitle => "User Inspector";

	// ===== STATE =====

	public Sync<UserInspectorView> CurrentView { get; private set; }

	/// <summary>
	/// Selected user for inspection (null = local user).
	/// </summary>
	public SyncRef<User> SelectedUser { get; private set; }

	/// <summary>
	/// Auto-refresh interval in seconds. 0 = manual only.
	/// </summary>
	public Sync<float> RefreshInterval { get; private set; }

	// UI References
	private HelioText _headerText;
	private HelioText _mainInfoText;
	private HelioText _detailsText;
	private HelioText _statusText;
	private float _refreshTimer;

	// Performance tracking
	private float _lastPing;
	private float _pingSmooth;
	private ulong _lastSentBytes;
	private ulong _lastReceivedBytes;
	private float _sendRate;
	private float _receiveRate;
	private float _rateTimer;

	// ===== INITIALIZATION =====

	public override void OnAwake()
	{
		base.OnAwake();
		CurrentView = new Sync<UserInspectorView>(this, UserInspectorView.LocalUser);
		SelectedUser = new SyncRef<User>(this);
		RefreshInterval = new Sync<float>(this, 0.5f);
	}

	// ===== ROOT STEP =====

	protected override void BuildRootStep(HelioUIBuilder ui)
	{
		// Header with title and status
		ui.HorizontalLayout(spacing: 8f);
		_headerText = ui.Text("User Inspector", fontSize: 28f, textColor: new color(0.9f, 0.9f, 1f));
		ui.FlexibleSpacer();
		_statusText = ui.Text("", fontSize: 14f, textColor: new color(0.6f, 0.8f, 0.6f));
		ui.EndLayout();

		ui.Spacer(12f);

		// View mode tabs with visual feedback
		ui.HorizontalLayout(spacing: 4f);
		BuildTabButton(ui, "Local", UserInspectorView.LocalUser);
		BuildTabButton(ui, "Users", UserInspectorView.WorldUsers);
		BuildTabButton(ui, "Network", UserInspectorView.Network);
		BuildTabButton(ui, "Avatar", UserInspectorView.Avatar);
		BuildTabButton(ui, "Perms", UserInspectorView.Permissions);
		ui.EndLayout();

		ui.Spacer(12f);

		// Main info panel with header
		ui.VerticalLayout(spacing: 4f, padding: new float4(12f, 12f, 12f, 12f));
		ui.Panel(new color(0.12f, 0.14f, 0.18f, 1f));

		_mainInfoText = ui.Text("Loading...", fontSize: 16f);

		ui.EndLayout();

		ui.Spacer(8f);

		// Details panel
		ui.VerticalLayout(spacing: 4f, padding: new float4(12f, 12f, 12f, 12f));
		ui.Panel(new color(0.1f, 0.1f, 0.12f, 1f));

		_detailsText = ui.Text("", fontSize: 14f, textColor: new color(0.8f, 0.8f, 0.8f));

		ui.EndLayout();

		ui.Spacer(8f);

		// Actions panel
		ui.VerticalLayout(spacing: 4f, padding: new float4(8f, 8f, 8f, 8f));
		ui.Panel(new color(0.08f, 0.1f, 0.12f, 1f));

		ui.Text("Actions", fontSize: 16f, textColor: new color(0.7f, 0.7f, 0.9f));
		ui.Spacer(4f);

		ui.HorizontalLayout(spacing: 4f);
		ui.Button("Teleport To", OnTeleportToUser);
		ui.Button("Copy Info", OnCopyUserInfo);
		ui.Button("Respawn", OnRespawnUser);
		ui.EndLayout();

		ui.EndLayout();

		ui.FlexibleSpacer();

		// Footer
		ui.HorizontalLayout(spacing: 8f);

		ui.Text("Auto:", fontSize: 12f);
		ui.Button(RefreshInterval.Value > 0 ? "ON" : "OFF", ToggleAutoRefresh);

		ui.FlexibleSpacer();
		ui.Button("Refresh", () => UpdateDisplay());
		ui.Button("Close", Close);
		ui.EndLayout();

		// Initial update
		UpdateDisplay();
	}

	private void BuildTabButton(HelioUIBuilder ui, string label, UserInspectorView view)
	{
		bool isActive = CurrentView.Value == view;
		var btn = ui.Button(label, () => SwitchView(view));

		// Visual feedback for active tab would be handled by button color
	}

	// ===== UPDATE =====

	public override void OnUpdate(float delta)
	{
		base.OnUpdate(delta);

		// Track network rates
		var user = GetInspectedUser();
		if (user != null)
		{
			_rateTimer += delta;
			if (_rateTimer >= 1f)
			{
				ulong sentDiff = user.SentBytes - _lastSentBytes;
				ulong recvDiff = user.ReceivedBytes - _lastReceivedBytes;

				_sendRate = sentDiff / _rateTimer;
				_receiveRate = recvDiff / _rateTimer;

				_lastSentBytes = user.SentBytes;
				_lastReceivedBytes = user.ReceivedBytes;
				_rateTimer = 0f;
			}

			// Smooth ping
			_pingSmooth = _pingSmooth * 0.8f + user.Ping.Value * 0.2f;
		}

		// Auto refresh
		if (RefreshInterval.Value > 0)
		{
			_refreshTimer += delta;
			if (_refreshTimer >= RefreshInterval.Value)
			{
				_refreshTimer = 0f;
				UpdateDisplay();
			}
		}

		// Update status
		if (_statusText != null)
		{
			string status = RefreshInterval.Value > 0 ? "LIVE" : "";
			if (user != null && user.Ping.Value > 0)
			{
				status += $" | {_pingSmooth:F0}ms";
			}
			_statusText.Content.Value = status;
		}
	}

	// ===== VIEW SWITCHING =====

	private void SwitchView(UserInspectorView view)
	{
		CurrentView.Value = view;
		UpdateDisplay();
	}

	private void UpdateDisplay()
	{
		switch (CurrentView.Value)
		{
			case UserInspectorView.LocalUser:
				UpdateLocalUserView();
				break;
			case UserInspectorView.WorldUsers:
				UpdateWorldUsersView();
				break;
			case UserInspectorView.Network:
				UpdateNetworkView();
				break;
			case UserInspectorView.Avatar:
				UpdateAvatarView();
				break;
			case UserInspectorView.Permissions:
				UpdatePermissionsView();
				break;
		}
	}

	// ===== VIEW IMPLEMENTATIONS =====

	private void UpdateLocalUserView()
	{
		if (_mainInfoText == null) return;

		var user = World?.LocalUser;
		string info = "";

		if (user != null)
		{
			info += $"Username: {user.UserName.Value}\n";
			info += $"User ID: {user.UserID.Value}\n";
			info += $"Reference ID: {user.ReferenceID}\n\n";

			info += $"Status:\n";
			info += $"  Is Host: {(user.IsHost ? "Yes" : "No")}\n";
			info += $"  Is Present: {(user.IsPresent.Value ? "Yes" : "No")}\n";
			info += $"  Is Silenced: {(user.IsSilenced.Value ? "Yes" : "No")}\n";
		}
		else
		{
			info = "No local user found\n\nWorld may not be fully initialized.";
		}

		_mainInfoText.Content.Value = info;

		// Details
		if (_detailsText != null && user != null)
		{
			string details = "Session Info:\n";
			details += $"  World: {World?.Name ?? "Unknown"}\n";
			details += $"  Users Online: {World?.UserCount ?? 0}\n";
			details += $"  Session Duration: {FormatDuration((float)(World?.TotalTime ?? 0))}\n\n";

			details += "Network:\n";
			details += $"  Ping: {user.Ping.Value}ms (avg: {_pingSmooth:F0}ms)\n";
			details += $"  Upload: {FormatBytesRate(_sendRate)}/s\n";
			details += $"  Download: {FormatBytesRate(_receiveRate)}/s";

			_detailsText.Content.Value = details;
		}
	}

	private void UpdateWorldUsersView()
	{
		if (_mainInfoText == null) return;

		string info = $"World Users ({World?.UserCount ?? 0}):\n\n";

		if (World != null)
		{
			var users = World.GetAllUsers();
			int idx = 1;
			foreach (var user in users)
			{
				string status = "";
				if (user.IsHost) status += "[H]";
				if (!user.IsPresent.Value) status += "[Away]";
				if (user.IsSilenced.Value) status += "[S]";

				string prefix = user == World.LocalUser ? ">" : " ";
				info += $"{prefix}{idx}. {user.UserName.Value} {status}\n";
				info += $"    Ping: {user.Ping.Value}ms\n";
				idx++;
			}
		}
		else
		{
			info += "World not available";
		}

		_mainInfoText.Content.Value = info;

		// Details - selected user info
		if (_detailsText != null)
		{
			string details = "Legend:\n";
			details += "  [H] = Host\n";
			details += "  [Away] = Not Present\n";
			details += "  [S] = Silenced\n";
			details += "  > = You";

			_detailsText.Content.Value = details;
		}
	}

	private void UpdateNetworkView()
	{
		if (_mainInfoText == null) return;

		var user = GetInspectedUser();
		string info = "Network Statistics:\n\n";

		if (user != null)
		{
			info += $"Latency:\n";
			info += $"  Current Ping: {user.Ping.Value}ms\n";
			info += $"  Average Ping: {_pingSmooth:F1}ms\n\n";

			info += $"Bandwidth:\n";
			info += $"  Total Sent: {FormatBytes(user.SentBytes)}\n";
			info += $"  Total Received: {FormatBytes(user.ReceivedBytes)}\n";
			info += $"  Send Rate: {FormatBytesRate(_sendRate)}/s\n";
			info += $"  Receive Rate: {FormatBytesRate(_receiveRate)}/s\n\n";

			info += $"Last Sync: {user.LastSyncMessage:HH:mm:ss}";
		}
		else
		{
			info += "No user data available";
		}

		_mainInfoText.Content.Value = info;

		// Stream details
		if (_detailsText != null && user != null)
		{
			string details = "Stream Counts:\n";
			details += $"  Immediate Delta: {user.ImmediateDeltaCount}\n";
			details += $"  Immediate Control: {user.ImmediateControlCount}\n";
			details += $"  Immediate Stream: {user.ImmediateStreamCount}\n";
			details += $"  Receive Streams: {user.ReceiveStreams}\n\n";

			details += "Connection Quality:\n";
			string quality = _pingSmooth < 50 ? "Excellent" :
							 _pingSmooth < 100 ? "Good" :
							 _pingSmooth < 200 ? "Fair" : "Poor";
			details += $"  Quality: {quality}";

			_detailsText.Content.Value = details;
		}
	}

	private void UpdateAvatarView()
	{
		if (_mainInfoText == null) return;

		var user = GetInspectedUser();
		string info = "Avatar Information:\n\n";

		if (user?.UserRootSlot != null)
		{
			var userRoot = user.UserRootSlot.GetComponent<UserRoot>();
			if (userRoot != null)
			{
				info += $"Tracking:\n";
				info += $"  Has Data: {(userRoot.ReceivedFirstPositionalData ? "Yes" : "No")}\n";
				info += $"  Global Scale: {userRoot.GlobalScale:F3}\n\n";

				info += $"Positions:\n";
				info += $"  Head: {FormatFloat3(userRoot.HeadPosition)}\n";
				info += $"  Feet: {FormatFloat3(userRoot.FeetPosition)}";
			}
			else
			{
				info += "UserRoot component not found";
			}
		}
		else
		{
			info += "No avatar slot available";
		}

		_mainInfoText.Content.Value = info;

		// Component details
		if (_detailsText != null && user?.UserRootSlot != null)
		{
			string details = "Avatar Components:\n";

			int slotCount = CountSlotsInHierarchy(user.UserRootSlot);
			int componentCount = CountComponentsInHierarchy(user.UserRootSlot);

			details += $"  Total Slots: {slotCount}\n";
			details += $"  Total Components: {componentCount}\n\n";

			details += "Avatar Slot:\n";
			details += $"  Name: {user.UserRootSlot.SlotName.Value}\n";
			details += $"  Active: {user.UserRootSlot.ActiveSelf}";

			_detailsText.Content.Value = details;
		}
	}

	private void UpdatePermissionsView()
	{
		if (_mainInfoText == null) return;

		var user = GetInspectedUser();
		string info = "User Permissions:\n\n";

		if (user != null)
		{
			info += $"Role: {(user.IsHost ? "Host" : "Guest")}\n\n";

			info += "Capabilities:\n";
			info += $"  Can Modify World: {user.IsHost}\n";
			info += $"  Can Kick Users: {user.IsHost}\n";
			info += $"  Can Change Settings: {user.IsHost}\n";
		}
		else
		{
			info += "No user data available";
		}

		_mainInfoText.Content.Value = info;

		if (_detailsText != null)
		{
			string details = "World Settings:\n";
			details += $"  World Authority: {World?.IsAuthority}\n";
			details += $"  World Name: {World?.Name ?? "Unknown"}";

			_detailsText.Content.Value = details;
		}
	}

	// ===== ACTIONS =====

	private void OnTeleportToUser()
	{
		var targetUser = SelectedUser.Target ?? World?.LocalUser;
		if (targetUser?.UserRootSlot == null) return;

		var localUser = World?.LocalUser;
		if (localUser?.UserRootSlot == null) return;

		var userRoot = localUser.UserRootSlot.GetComponent<UserRoot>();
		var targetRoot = targetUser.UserRootSlot.GetComponent<UserRoot>();

		if (userRoot != null && targetRoot != null)
		{
			// Teleport to target's feet position
			// userRoot.TeleportTo(targetRoot.FeetPosition);
		}
	}

	private void OnCopyUserInfo()
	{
		var user = GetInspectedUser();
		if (user == null) return;

		string info = $"Username: {user.UserName.Value}\n";
		info += $"User ID: {user.UserID.Value}\n";
		info += $"Reference ID: {user.ReferenceID}";

		// Copy to clipboard would go here
		// Platform.CopyToClipboard(info);
	}

	private void OnRespawnUser()
	{
		var user = World?.LocalUser;
		if (user?.UserRootSlot == null) return;

		var userRoot = user.UserRootSlot.GetComponent<UserRoot>();
		// userRoot?.Respawn();
	}

	private void ToggleAutoRefresh()
	{
		RefreshInterval.Value = RefreshInterval.Value > 0 ? 0f : 0.5f;
	}

	// ===== UTILITY =====

	private User GetInspectedUser()
	{
		return SelectedUser.Target ?? World?.LocalUser;
	}

	private int CountSlotsInHierarchy(Slot slot)
	{
		if (slot == null) return 0;
		int count = 1;
		foreach (var child in slot.Children)
			count += CountSlotsInHierarchy(child);
		return count;
	}

	private int CountComponentsInHierarchy(Slot slot)
	{
		if (slot == null) return 0;
		int count = slot.ComponentCount;
		foreach (var child in slot.Children)
			count += CountComponentsInHierarchy(child);
		return count;
	}

	private static string FormatBytes(ulong bytes)
	{
		if (bytes < 1024) return $"{bytes} B";
		if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
		if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
		return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
	}

	private static string FormatBytesRate(float bytesPerSec)
	{
		if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B";
		if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024f:F1} KB";
		return $"{bytesPerSec / (1024f * 1024f):F1} MB";
	}

	private static string FormatFloat3(float3 v)
	{
		return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
	}

	private static string FormatDuration(float seconds)
	{
		var ts = TimeSpan.FromSeconds(seconds);
		if (ts.TotalHours >= 1)
			return $"{(int)ts.TotalHours}h {ts.Minutes}m";
		if (ts.TotalMinutes >= 1)
			return $"{ts.Minutes}m {ts.Seconds}s";
		return $"{ts.Seconds}s";
	}
}
