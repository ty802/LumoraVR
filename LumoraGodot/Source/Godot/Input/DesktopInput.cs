// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core.Components;
using LumoraLogger = Lumora.Core.Logging.Logger;
using Lumora.Source.UI;
using Lumora.Source.Godot.UI;

namespace Lumora.Source.Input;

// Desktop camera-side scene overlay: cursor reticle, UI raycast, and a cheap
// idle/reach hand pose. Input data (movement axis, buttons) lives on the
// InputInterface drivers now, this node is purely scene-side concerns.
// - xlinka
public partial class DesktopInput : Node3D
{
    private const float MaxRayDistance = 100f;
    private const uint UICollisionLayer = 1u << 3;

    private Camera3D _camera = null!;
    private Control _cursorUI = null!;
    private CircleCursor _cursorDot = null!;
    private RayCast3D _interactionRay = null!;

    private Vector3 _leftHandPosition;
    private Vector3 _rightHandPosition;
    private Quaternion _leftHandRotation = Quaternion.Identity;
    private Quaternion _rightHandRotation = Quaternion.Identity;

    private const float HandIdleOffset = 0.3f;
    private const float HandInteractionLerpSpeed = 8f;

    private float _interactionLerp;
    private Vector3 _interactionTargetPoint;
    private bool _isHoveringUI;
    private Vector3 _headPosition;
    private Quaternion _headRotation = Quaternion.Identity;
    private float _playerHeight = 1.8f;

    public Camera3D Camera => _camera;
    public bool IsHoveringUI => _isHoveringUI;
    public Vector3 InteractionPoint => _interactionTargetPoint;

    public Vector3 LeftHandPosition => _leftHandPosition;
    public Vector3 RightHandPosition => _rightHandPosition;
    public Quaternion LeftHandRotation => _leftHandRotation;
    public Quaternion RightHandRotation => _rightHandRotation;

    public override void _Ready()
    {
        CreateCursorUI();
        CreateInteractionRay();
        LumoraLogger.Log("DesktopInput initialized");
    }

    public override void _ExitTree()
    {
        InterfaceSettings.Changed -= ApplyCursorSettings;
        base._ExitTree();
    }

    private void CreateCursorUI()
    {
        var canvasLayer = new CanvasLayer();
        canvasLayer.Name = "DesktopCursorLayer";
        canvasLayer.Layer = 101;
        AddChild(canvasLayer);

        _cursorUI = new Control();
        _cursorUI.Name = "CursorContainer";
        _cursorUI.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cursorUI.MouseFilter = Control.MouseFilterEnum.Ignore;
        canvasLayer.AddChild(_cursorUI);

        _cursorDot = new CircleCursor();
        _cursorDot.Name = "CursorDot";
        _cursorDot.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cursorUI.AddChild(_cursorDot);

        ApplyCursorSettings();
        InterfaceSettings.Changed += ApplyCursorSettings;
    }

    private void ApplyCursorSettings()
    {
        if (_cursorDot == null) return;

        float size = InterfaceSettings.ReticleSize;
        _cursorDot.Radius = size;
        _cursorDot.Thickness = InterfaceSettings.ReticleThickness;
        _cursorDot.Style = InterfaceSettings.Style;
        _cursorDot.CursorColor = InterfaceSettings.ReticleColor;
        _cursorDot.CustomMinimumSize = new Vector2(size * 2 + 4, size * 2 + 4);
        _cursorDot.QueueRedraw();
    }

    private void CreateInteractionRay()
    {
        _interactionRay = new RayCast3D();
        _interactionRay.Name = "DesktopInteractionRay";
        _interactionRay.TargetPosition = new Vector3(0, 0, -MaxRayDistance);
        _interactionRay.CollisionMask = UICollisionLayer;
        _interactionRay.CollideWithAreas = true;
        _interactionRay.CollideWithBodies = false;
        _interactionRay.Enabled = true;
        AddChild(_interactionRay);
    }

    public override void _Process(double delta)
    {
        UpdateCamera();
        UpdateCursorPosition();

        if (!DashboardToggle.IsDashboardVisible)
        {
            UpdateInteractionRay();
            UpdateHandSimulation((float)delta);
        }
    }

    private void UpdateCamera()
    {
        if (UserInputState.FocusedFreeCamActive)
            return;

        _camera = Lumora.Source.Godot.Bootstrap.XRModeManager.Instance?.CurrentCamera ?? _camera;

        if (_camera != null)
        {
            _headPosition = _camera.GlobalPosition;
            _headRotation = _camera.GlobalTransform.Basis.GetRotationQuaternion();
        }
    }

    private void UpdateCursorPosition()
    {
        if (_cursorDot == null)
            return;

        _cursorDot.Visible = InterfaceSettings.Style != InterfaceSettings.ReticleStyle.Off;
        if (!_cursorDot.Visible)
            return;

        var viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);

        if (DashboardToggle.IsDashboardVisible)
        {
            var mousePos = GetViewport()?.GetMousePosition() ?? viewportSize / 2f;
            _cursorDot.Position = mousePos - _cursorDot.CustomMinimumSize / 2f;
            _cursorDot.CursorColor = InterfaceSettings.ReticleColor;
        }
        else
        {
            var centerPos = viewportSize / 2f - _cursorDot.CustomMinimumSize / 2f;
            _cursorDot.Position = centerPos;
            _cursorDot.CursorColor = _isHoveringUI
                ? InterfaceSettings.ReticleHoverColor
                : InterfaceSettings.ReticleColor;
        }

        _cursorDot.QueueRedraw();
    }

    private void UpdateInteractionRay()
    {
        if (_camera == null || _interactionRay == null)
            return;

        _interactionRay.GlobalPosition = _camera.GlobalPosition;
        _interactionRay.GlobalRotation = _camera.GlobalRotation;

        _interactionRay.ForceRaycastUpdate();

        _isHoveringUI = false;
        if (_interactionRay.IsColliding())
        {
            var collider = _interactionRay.GetCollider();
            if (collider is Area3D)
            {
                _isHoveringUI = true;
                _interactionTargetPoint = _interactionRay.GetCollisionPoint();
            }
        }
    }

    private void UpdateHandSimulation(float delta)
    {
        if (_camera == null)
            return;

        var forward = _headRotation * Vector3.Forward;
        var right = _headRotation * Vector3.Right;
        var down = Vector3.Down;

        var hipsPos = _headPosition + down * (_playerHeight * 0.5f);

        var leftIdlePos = hipsPos - right * HandIdleOffset + forward * 0.05f;
        var rightIdlePos = hipsPos + right * HandIdleOffset + forward * 0.05f;

        var leftIdleRot = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(90), 0, Mathf.DegToRad(-90)));
        var rightIdleRot = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(90), 0, Mathf.DegToRad(90)));

        float targetLerp = _isHoveringUI ? 1f : 0f;
        _interactionLerp = Mathf.Lerp(_interactionLerp, targetLerp, delta * HandInteractionLerpSpeed);

        if (_isHoveringUI && _interactionLerp > 0.01f)
        {
            var toTarget = _interactionTargetPoint - _headPosition;
            var targetDir = toTarget.Normalized();
            var reachDistance = Mathf.Min(toTarget.Length() - 0.1f, 0.6f);

            var interactionPos = _headPosition + targetDir * reachDistance;
            var interactionRot = new Quaternion(new Vector3(0, 1, 0), Mathf.Atan2(targetDir.X, targetDir.Z));

            _rightHandPosition = rightIdlePos.Lerp(interactionPos, _interactionLerp);
            _rightHandRotation = rightIdleRot.Slerp(interactionRot, _interactionLerp);

            _leftHandPosition = leftIdlePos;
            _leftHandRotation = leftIdleRot;
        }
        else
        {
            _leftHandPosition = leftIdlePos;
            _rightHandPosition = rightIdlePos;
            _leftHandRotation = leftIdleRot;
            _rightHandRotation = rightIdleRot;
        }
    }

    public void SetCamera(Camera3D camera)
    {
        _camera = camera;
    }

    public void SetPlayerHeight(float height)
    {
        _playerHeight = height;
    }
}

// Reticle drawing for the desktop crosshair. Style selection comes from
// InterfaceSettings.Style so the dashboard "reticle" setting takes effect live.
public partial class CircleCursor : Control
{
    public Color CursorColor { get; set; } = new Color(1f, 1f, 1f, 0.5f);
    public float Radius { get; set; } = 12f;
    public float Thickness { get; set; } = 2f;
    public Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle Style { get; set; }
        = Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle.Ring;

    public override void _Draw()
    {
        var center = Size / 2f;

        switch (Style)
        {
            case Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle.Off:
                return;

            case Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle.Dot:
                DrawCircle(center, Mathf.Max(Thickness, Radius * 0.25f), CursorColor);
                break;

            case Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle.Crosshair:
                float arm = Radius;
                DrawLine(center + new Vector2(-arm, 0f), center + new Vector2(arm, 0f), CursorColor, Thickness, true);
                DrawLine(center + new Vector2(0f, -arm), center + new Vector2(0f, arm), CursorColor, Thickness, true);
                break;

            case Lumora.Source.Godot.UI.InterfaceSettings.ReticleStyle.Ring:
            default:
                DrawArc(center, Radius, 0, Mathf.Tau, 32, CursorColor, Thickness, true);
                break;
        }
    }
}
