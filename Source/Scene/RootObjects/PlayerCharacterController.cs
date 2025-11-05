using System;
using System.Collections.Generic;
using Aquamarine.Source.Input;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Scene;
using Aquamarine.Source.Scene.Assets;
using Aquamarine.Source.Scene.UI;
using Godot;
using Logger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Scene.RootObjects;

/// <summary>
/// Character controller responsible for representing a player inside a world.
/// Combines locomotion, avatar parenting and limb pose sampling so child animators
/// can drive bones using runtime input data.
/// </summary>
public partial class PlayerCharacterController : CharacterBody3D, IRootObject, ICharacterController
{
	private const float DefaultGravity = -24.0f;

	[Export] private NodePath _nametagPath;
	[Export] private NodePath _head;
	[Export] private NodePath _leftHand;
	[Export] private NodePath _rightHand;
	[Export] private NodePath _hip;
	[Export] private NodePath _leftFoot;
	[Export] private NodePath _rightFoot;

	[Export] public float WalkSpeed { get; set; } = 4.5f;
	[Export] public float SprintSpeed { get; set; } = 7.5f;
	[Export] public float Acceleration { get; set; } = 12.0f;
	[Export] public float JumpVelocity { get; set; } = 6.5f;
	[Export] public float Gravity { get; set; } = DefaultGravity;
	[Export] public float PlayspaceFollowStrength { get; set; } = 8.0f;

	public static PackedScene PackedScene { get; } =
		ResourceLoader.Load<PackedScene>("res://Scenes/Objects/RootObjects/PlayerCharacterController.tscn");

	public Node Self => this;
	public IDictionary<ushort, IChildObject> ChildObjects { get; } = new Dictionary<ushort, IChildObject>();
	public IDictionary<ushort, IAssetProvider> AssetProviders { get; } = new Dictionary<ushort, IAssetProvider>();

	public Avatar Avatar { get; private set; }
	public Nameplate Nametag { get; private set; }
	public string DisplayName { get; private set; }
	public int Authority { get; private set; }

	private Node3D _headNode;
	private Node3D _leftHandNode;
	private Node3D _rightHandNode;
	private Node3D _hipNode;
	private Node3D _leftFootNode;
	private Node3D _rightFootNode;

	private Vector3 _targetHorizontalVelocity;
	private Vector3 _playspaceOffset;
	private Camera3D _cachedCamera;
	private bool _ready;

	public override void _Ready()
	{
		base._Ready();

		CacheNodes();
		UpdateNametagVisibility();
		_ready = true;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (!IsInstanceValid(this))
		{
			return;
		}

		UpdateLocalLimbNodes();
		UpdateNametagBillboard();
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		if (!HasLocalAuthority())
		{
			return;
		}

		var provider = IInputProvider.Instance;
		if (provider == null)
		{
			return;
		}

		var dt = (float)delta;
		ApplyHorizontalMovement(provider, dt);
		ApplyVerticalMovement(provider, dt);
		ApplyPlayspaceOffset(provider, dt);

		try
		{
			MoveAndSlide();
		}
		catch (Exception ex)
		{
			Logger.Error($"PlayerCharacterController.MoveAndSlide failed: {ex.Message}");
		}
	}

	public void SetPlayerAuthority(int id)
	{
		Authority = id;
		UpdateNametagVisibility();
	}

	public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
	{
		if (data == null)
		{
			return;
		}

		if (data.TryGetValue("name", out var nameValue) && nameValue.VariantType == Variant.Type.String)
		{
			SetDisplayName(nameValue.AsString());
		}
	}

	public void AddChildObject(ISceneObject obj)
	{
		if (obj?.Self is not Node node)
		{
			return;
		}

		switch (obj)
		{
			case Avatar avatar:
				if (IsInstanceValid(Avatar) && Avatar != avatar)
				{
					Avatar.QueueFree();
				}

				Avatar = avatar;
				Avatar.Parent = this;
				AddChild(avatar);
				break;

			case IChildObject childObject when Avatar != null:
				Avatar.AddChildObject(childObject);
				break;

			default:
				AddChild(node);
				break;
		}
	}

	public Vector3 GetLimbPosition(IInputProvider.InputLimb limb) =>
		GetLimbNode(limb)?.GlobalPosition ?? GlobalPosition;

	public Quaternion GetLimbRotation(IInputProvider.InputLimb limb) =>
		GetLimbNode(limb)?.GlobalTransform.Basis.GetRotationQuaternion() ?? GlobalTransform.Basis.GetRotationQuaternion();

	public void SetDisplayName(string name)
	{
		DisplayName = name ?? string.Empty;

		if (Nametag != null && !string.IsNullOrEmpty(DisplayName))
		{
			Nametag.SetText(DisplayName);
		}
	}

	/// <summary>
	/// Simple respawn helper used by debug tooling. Resets velocity and moves to the given position.
	/// </summary>
	public void Respawn(Vector3? position = null)
	{
		var target = position ?? Vector3.Zero;
		GlobalPosition = target;
		Velocity = Vector3.Zero;
		_targetHorizontalVelocity = Vector3.Zero;
		_playspaceOffset = Vector3.Zero;
	}

	private void CacheNodes()
	{
		if (_ready)
		{
			return;
		}

		Nametag = GetNodeOrNull<Nameplate>(_nametagPath);
		_headNode = GetNodeOrNull<Node3D>(_head);
		_leftHandNode = GetNodeOrNull<Node3D>(_leftHand);
		_rightHandNode = GetNodeOrNull<Node3D>(_rightHand);
		_hipNode = GetNodeOrNull<Node3D>(_hip);
		_leftFootNode = GetNodeOrNull<Node3D>(_leftFoot);
		_rightFootNode = GetNodeOrNull<Node3D>(_rightFoot);

		if (Nametag == null)
		{
			Logger.Warn($"{nameof(PlayerCharacterController)}: Nametag node not assigned.");
		}
	}

	private void ApplyHorizontalMovement(IInputProvider provider, float delta)
	{
		var input = provider.GetMovementInputAxis;
		var hasInput = input.LengthSquared() > 0.0001f;

		if (!hasInput)
		{
			_targetHorizontalVelocity = _targetHorizontalVelocity.Lerp(Vector3.Zero, Acceleration * delta);
		}
		else
		{
			var headBasis = _headNode?.GlobalBasis ?? GlobalTransform.Basis;
			var forward = -headBasis.Z;
			var right = headBasis.X;

			var desiredDirection = (forward * input.Y) + (right * input.X);
			if (desiredDirection.LengthSquared() > 0.001f)
			{
				desiredDirection = desiredDirection.Normalized();
			}

			var targetSpeed = provider.GetSprintInput ? SprintSpeed : WalkSpeed;
			var desiredVelocity = desiredDirection * targetSpeed;
			_targetHorizontalVelocity = _targetHorizontalVelocity.Lerp(desiredVelocity, Acceleration * delta);
		}

		Velocity = new Vector3(_targetHorizontalVelocity.X, Velocity.Y, _targetHorizontalVelocity.Z);
	}

	private void ApplyVerticalMovement(IInputProvider provider, float delta)
	{
		var velocity = Velocity;

		if (!IsOnFloor())
		{
			velocity.Y += Gravity * delta;
		}
		else if (provider.GetJumpInput)
		{
			velocity.Y = JumpVelocity;
		}
		else if (velocity.Y < 0)
		{
			velocity.Y = 0;
		}

		Velocity = velocity;
	}

	private void ApplyPlayspaceOffset(IInputProvider provider, float delta)
	{
		var deltaOffset = provider.GetPlayspaceMovementDelta;
		if (deltaOffset == Vector3.Zero)
		{
			_playspaceOffset = _playspaceOffset.Lerp(Vector3.Zero, PlayspaceFollowStrength * delta);
		}
		else
		{
			_playspaceOffset += deltaOffset;
		}

		if (_playspaceOffset != Vector3.Zero)
		{
			GlobalPosition += _playspaceOffset;
			_playspaceOffset = Vector3.Zero;
		}
	}

	private void UpdateLocalLimbNodes()
	{
		if (!HasLocalAuthority())
		{
			return;
		}

		var provider = IInputProvider.Instance;
		if (provider == null)
		{
			return;
		}

		UpdateLimbNode(_headNode, provider, IInputProvider.InputLimb.Head);
		UpdateLimbNode(_leftHandNode, provider, IInputProvider.InputLimb.LeftHand);
		UpdateLimbNode(_rightHandNode, provider, IInputProvider.InputLimb.RightHand);
		UpdateLimbNode(_hipNode, provider, IInputProvider.InputLimb.Hip);
		UpdateLimbNode(_leftFootNode, provider, IInputProvider.InputLimb.LeftFoot);
		UpdateLimbNode(_rightFootNode, provider, IInputProvider.InputLimb.RightFoot);
	}

	private void UpdateLimbNode(Node3D node, IInputProvider provider, IInputProvider.InputLimb limb)
	{
		if (node == null)
		{
			return;
		}

		var (pos, rot) = IInputProvider.LimbTransform(limb);
		node.Transform = new Transform3D(new Basis(rot), pos);
	}

	private void UpdateNametagBillboard()
	{
		if (Nametag == null || _headNode == null)
		{
			return;
		}

		Nametag.GlobalPosition = _headNode.GlobalPosition + Vector3.Up * 0.15f;

		var camera = GetActiveCamera();
		if (camera != null)
		{
			Nametag.LookAt(camera.GlobalPosition, Vector3.Up);
		}
	}

	private Camera3D GetActiveCamera()
	{
		if (_cachedCamera != null && IsInstanceValid(_cachedCamera))
		{
			return _cachedCamera;
		}

		_cachedCamera = GetViewport()?.GetCamera3D();
		return _cachedCamera;
	}

	private Node3D GetLimbNode(IInputProvider.InputLimb limb) =>
		limb switch
		{
			IInputProvider.InputLimb.Head => _headNode,
			IInputProvider.InputLimb.LeftHand => _leftHandNode,
			IInputProvider.InputLimb.RightHand => _rightHandNode,
			IInputProvider.InputLimb.Hip => _hipNode,
			IInputProvider.InputLimb.LeftFoot => _leftFootNode,
			IInputProvider.InputLimb.RightFoot => _rightFootNode,
			_ => null
		};

	private bool HasLocalAuthority()
	{
		if (!IsInsideTree())
		{
			return false;
		}

		var peer = Multiplayer?.MultiplayerPeer;
		if (peer == null)
		{
			// No networking – treat as local authority.
			return true;
		}

		try
		{
			return Multiplayer.GetUniqueId() == Authority;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}

	private void UpdateNametagVisibility()
	{
		if (Nametag == null)
		{
			return;
		}

		Nametag.SetVisible(!HasLocalAuthority());

		if (!string.IsNullOrEmpty(DisplayName))
		{
			Nametag.SetText(DisplayName);
		}
	}
}
