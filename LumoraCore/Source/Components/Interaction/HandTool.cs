// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

[ComponentCategory("Interaction")]
[DefaultUpdateOrder(-1000)]
public sealed class HandTool : Tool
{
    public enum LaserRotationMode
    {
        AxisX,
        AxisY,
        AxisZ,
        Unconstrained
    }

    public readonly Sync<float> HoldScrollStep = new();
    public readonly Sync<float> HoldScaleStep = new();
    public readonly Sync<float> HoldRotationSensitivity = new();
    public readonly Sync<LaserRotationMode> RotationMode = new();
    public readonly SyncRef<ToolItem> ActiveToolItem = new();

    private Slot? _grabberSlot;
    private Slot? _laserSlot;
    private Slot? _toolHolderSlot;
    private Grabber? _grabber;
    private InteractionLaser? _laser;
    private bool _primaryHeld;
    private bool _prevPrimaryHeld;
    private bool _secondaryHeld;
    private bool _prevSecondaryHeld;
    private bool _gripHeld;
    private bool _prevGripHeld;
    private ToolItem? _activePrimaryToolItem;
    private ToolItem? _activeSecondaryToolItem;
    private bool _isHoldingWithLaser;
    private float _laserGrabDistance;
    private float _holderAxisOffset;
    private floatQ _holderRotationOffset = floatQ.Identity;
    private floatQ? _holderRotationReference;
    private bool _desktopInputSuppressed;
    private double _lastAlignPress = -1000.0;

    public override Grabber? Grabber => _grabber;
    public override InteractionLaser? Laser => _laser;
    public override bool PrimaryHeld => _primaryHeld;
    public override bool SecondaryHeld => _secondaryHeld;
    public override bool GripHeld => _gripHeld;
    public bool IsHoldingObjects => _grabber?.IsHoldingObjects == true;
    public bool IsHoldingObjectsWithLaser => _isHoldingWithLaser && IsHoldingObjects;

    public override void OnInit()
    {
        base.OnInit();
        HoldScrollStep.Value = 0.12f;
        HoldScaleStep.Value = 0.10f;
        HoldRotationSensitivity.Value = MathF.PI * 2f;
        RotationMode.Value = LaserRotationMode.AxisY;
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRig();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        EnsureRig();

        if (_laser == null)
        {
            return;
        }

        // While the userspace dash pointer owns the cursor (dash open, desktop or VR), this in-world
        // tool stands down completely: no second cursor floating in the world behind the dash, and
        // no presses bleeding through the panel into whatever is behind it. The userspace pointer
        // rig raises this flag while it is live (it is the thing actually pointing at the dash, on a
        // controller-tracked hand in VR or the free cursor on desktop), so we just back off. -xlinka
        var dashOwner = Engine.Current?.InputInterface;
        if (dashOwner != null && dashOwner.IsAnyUserspaceLaserActive)
        {
            _laser.SetToolState(false, false);
            _laser.SetExclusiveRoot(null);
            _laser.SetDormant(true);
            SetDesktopInputSuppression(false);
            return;
        }
        _laser.SetDormant(false);

        SampleInput(_laser);
        // In VR the laser stays visible while this hand's menu is open so the
        // user can see what they're aiming at. On desktop the menu owns a
        // mouse-driven pointer instead, so the laser goes fully inactive to
        // keep its frozen center aim from pressing menu items.
        var inputInterface = Engine.Current?.InputInterface;
        bool vrActive = inputInterface?.IsVRActive == true;
        bool menuVisible = IsContextMenuOpenByThisHand();
        bool desktopMenuOpen = menuVisible && !vrActive;
        // While the desktop menu is open the camera is frozen and the mouse
        // deflects the laser instead - the laser cursor IS the pointer. Press
        // state stays the real primary. (The dash uses the free-cursor ray the
        // platform pushes, not this deflection.)
        UpdateDesktopMenuAim(desktopMenuOpen);
        // Modal pointer targets: while our menu is open it is the only thing
        // this laser can touch; while the desktop dash is open, the dash surface
        // is - no click-through into the world behind it. Primary presses must
        // reach the target even mid-grab (otherwise the held-object actions
        // could never be clicked - holding normally suppresses canvas presses).
        Slot? exclusiveRoot = null;
        if (menuVisible)
            exclusiveRoot = _contextMenu?.VisualRoot;
        else if (!vrActive && inputInterface?.IsDashboardOpen == true)
            exclusiveRoot = UI.UserspaceDashboard.LocalInstance?.SurfaceSlot;
        _laser.SetExclusiveRoot(exclusiveRoot);
        bool uiPress = _primaryHeld && (menuVisible || !IsHoldingObjectsWithLaser);
        _laser.SetToolState(uiPress, IsHoldingObjectsWithLaser && !menuVisible);
        _laser.RefreshNow(delta);
        ProcessPrimary(_laser);
        ProcessSecondary(_laser);
        ProcessMenuKey(_laser);
        ProcessGrip(_laser);

        if (IsHoldingObjectsWithLaser && !menuVisible)
        {
            ProcessLaserHold(_laser, delta);
        }
        else
        {
            SetDesktopInputSuppression(false);
        }
    }

    public override void OnDestroy()
    {
        ResetInteraction(releaseHeld: true);
        // Don't pop the item into the world mid-teardown; let it go down with the rig.
        _suppressHolderRelease = true;
        EquipToolItem(null);
        base.OnDestroy();
    }

    private bool _suppressHolderRelease;

    private void EnsureRig()
    {
        if (Slot == null || Slot.IsRemoved)
        {
            return;
        }

        _grabberSlot ??= Slot.FindChild("Grabber", recursive: false) ?? Slot.AddSlot("Grabber");
        if (_grabberSlot.GetComponent<SearchBlock>() == null)
        {
            _grabberSlot.AttachComponent<SearchBlock>();
        }
        _grabber ??= _grabberSlot.GetComponent<Grabber>() ?? _grabberSlot.AttachComponent<Grabber>();

        _laserSlot ??= Slot.FindChild("Laser", recursive: false) ?? Slot.AddSlot("Laser");
        _laser ??= _laserSlot.GetComponent<InteractionLaser>() ?? _laserSlot.AttachComponent<InteractionLaser>();
        _laser.ControllerSide.Value = Side.Value;
        _laser.SetIgnoreRoot(Slot);

        _toolHolderSlot ??= Slot.FindChild("Tool Holder", recursive: false) ?? Slot.AddSlot("Tool Holder");
        if (_toolHolderSlot.GetComponent<GrabBlock>() == null)
        {
            _toolHolderSlot.AttachComponent<GrabBlock>();
        }
        if (_toolHolderSlot.GetComponent<SearchBlock>() == null)
        {
            _toolHolderSlot.AttachComponent<SearchBlock>();
        }

        if (ActiveToolItem.Target == null)
        {
            var item = _toolHolderSlot.GetComponentInChildren<ToolItem>(includeSelf: false);
            if (item != null)
            {
                EquipToolItem(item);
            }
        }
    }

    public void EquipToolItem(ToolItem? item)
    {
        var previous = ActiveToolItem.Target;
        if (ReferenceEquals(previous, item))
        {
            return;
        }

        if (previous != null)
        {
            previous.OnDequipped();
            previous.SetActiveTool(null);
            ReleaseFromHolder(previous);
        }

        ActiveToolItem.Target = item!;
        if (item != null)
        {
            item.SetActiveTool(this);
            item.OnEquipped();
        }
    }

    // Dequip must physically remove the item from the Tool Holder, otherwise
    // EnsureRig's auto-equip finds it there next update and snaps it right back.
    // Drop it into the world just off the hand.
    private void ReleaseFromHolder(ToolItem item)
    {
        if (_suppressHolderRelease)
            return;
        var itemSlot = item?.Slot;
        if (itemSlot == null || itemSlot.IsDestroyed || _toolHolderSlot == null)
            return;
        if (itemSlot != _toolHolderSlot && !itemSlot.IsDescendantOf(_toolHolderSlot))
            return;

        var userRootSlot = Slot?.ActiveUserRoot?.Slot;
        var newParent = userRootSlot?.Parent ?? World?.RootSlot;
        if (newParent == null || newParent.IsDestroyed)
            return;

        itemSlot.SetParent(newParent, preserveGlobalTransform: true);
        // Pop it off the hand a little so it isn't left intersecting the grip.
        if (Slot != null)
        {
            itemSlot.GlobalPosition += Slot.Forward * 0.05f;
        }
    }

    public T EquipNewToolItem<T>(string slotName) where T : ToolItem, new()
    {
        EnsureRig();
        var holder = _toolHolderSlot ?? Slot;
        var itemSlot = holder.FindChild(slotName, recursive: false) ?? holder.AddSlot(slotName);
        var item = itemSlot.GetComponent<T>() ?? itemSlot.AttachComponent<T>();
        EquipToolItem(item);
        return item;
    }

    private ToolItem? GetUsableToolItem()
    {
        var toolItem = ActiveToolItem.Target;
        if (toolItem == null || !toolItem.Enabled.Value || toolItem.IsDestroyed)
        {
            return null;
        }

        if (IsHoldingObjects && !toolItem.CanUseWhenHolding)
        {
            return null;
        }

        return toolItem;
    }

    private void SampleInput(InteractionLaser laser)
    {
        _primaryHeld = ReadPrimaryPressed(laser);
        _secondaryHeld = ReadSecondaryPressed(laser);
        _gripHeld = ReadGripPressed(laser);
    }

    private void ProcessPrimary(InteractionLaser laser)
    {
        if (_primaryHeld && !_prevPrimaryHeld)
        {
            var toolItem = GetUsableToolItem();
            if (toolItem != null && toolItem.OnPrimaryPress())
            {
                _activePrimaryToolItem = toolItem;
            }
            else if (IsHoldingObjectsWithLaser)
            {
                ProcessAlignPress(laser);
            }
            else if (laser.CurrentTarget != null && laser.CurrentPointerTarget == null)
            {
                laser.CurrentRayTarget?.NotifyActivated(laser.CurrentHitPoint);
                laser.NotifyActivatedByTool(laser.CurrentTarget, laser.CurrentHitPoint);
            }
        }
        else if (_primaryHeld && _activePrimaryToolItem != null)
        {
            _activePrimaryToolItem.OnPrimaryHold();
        }
        else if (!_primaryHeld && _prevPrimaryHeld && _activePrimaryToolItem != null)
        {
            _activePrimaryToolItem.OnPrimaryRelease();
            _activePrimaryToolItem = null;
        }

        _prevPrimaryHeld = _primaryHeld;
    }

    private void ProcessSecondary(InteractionLaser laser)
    {
        if (_secondaryHeld && !_prevSecondaryHeld)
        {
            // While our menu is open, the button closes it before any tool
            // gets a say — otherwise an equipped tool would eat the press and
            // the menu could never be dismissed.
            if (IsContextMenuOpenByThisHand())
            {
                FindContextMenu()?.Close();
            }
            else
            {
                var toolItem = GetUsableToolItem();
                if (toolItem != null && toolItem.UsesSecondary && toolItem.OnSecondaryPress())
                {
                    _activeSecondaryToolItem = toolItem;
                }
                else if (!IsHoldingObjects && Engine.Current?.InputInterface?.IsVRActive == true)
                {
                    // VR fallback: free secondary opens the menu. Desktop uses
                    // the dedicated T binding instead.
                    ToggleContextMenu(laser);
                }
            }
        }
        else if (_secondaryHeld && _activeSecondaryToolItem != null)
        {
            _activeSecondaryToolItem.OnSecondaryHold();
        }
        else if (!_secondaryHeld && _prevSecondaryHeld && _activeSecondaryToolItem != null)
        {
            _activeSecondaryToolItem.OnSecondaryRelease();
            _activeSecondaryToolItem = null;
        }

        _prevSecondaryHeld = _secondaryHeld;
    }

    // Secondary press with no tool/held-object claim toggles the user's
    // radial context menu at the laser, carrying what it was pointing at so
    // sources can add contextual actions (equip avatar, etc.).
    private void ToggleContextMenu(InteractionLaser laser)
    {
        var menu = FindContextMenu();
        if (menu == null)
            return;

        menu.Toggle(new UI.ContextMenuContext
        {
            Pointer = _laserSlot ?? Slot,
            Target = laser?.CurrentHitSlot,
            Side = Side.Value,
        });
    }

    // Accumulated mouse deflection (yaw, pitch radians) steering the laser while
    // the desktop context menu has the camera frozen. Same sign convention as
    // the camera: mouse right = look right, mouse up = look up.
    private float2 _menuAim;
    private const float MenuAimRadiansPerScreen = 1.5f;
    private const float MenuAimMaxRadians = 0.85f;

    private void UpdateDesktopMenuAim(bool active)
    {
        if (_laser == null)
            return;

        if (!active)
        {
            if (_menuAim != float2.Zero)
            {
                _menuAim = float2.Zero;
                _laser.SetDesktopAimOffset(float2.Zero);
            }
            return;
        }

        var mouse = Engine.Current?.InputInterface?.Mouse;
        if (mouse == null)
            return;

        var d = mouse.DirectDelta.Value;
        _menuAim = new float2(
            System.Math.Clamp(_menuAim.x - d.x * MenuAimRadiansPerScreen, -MenuAimMaxRadians, MenuAimMaxRadians),
            System.Math.Clamp(_menuAim.y - d.y * MenuAimRadiansPerScreen, -MenuAimMaxRadians, MenuAimMaxRadians));
        _laser.SetDesktopAimOffset(_menuAim);
    }

    private bool _menuKeyWasDown;
    private bool _menuTKeyWasDown;

    // Desktop context menu binding: middle mouse click OR the T key toggles open/close
    // (tool secondary owns R, primary owns left click). Only the right hand
    // listens so both hands don't double-toggle. T is tracked on its own edge so it
    // and the mouse each toggle independently. -xlinka
    private void ProcessMenuKey(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || input.IsVRActive || Side.Value != Chirality.Right)
        {
            _menuKeyWasDown = false;
            _menuTKeyWasDown = false;
            return;
        }

        bool mouseDown = input.Mouse?.MiddleButton.Held == true;
        bool tDown = input.Keyboard?.IsKeyPressed(Key.T) == true;

        if ((mouseDown && !_menuKeyWasDown) || (tDown && !_menuTKeyWasDown))
        {
            ToggleContextMenu(laser);
        }

        _menuKeyWasDown = mouseDown;
        _menuTKeyWasDown = tDown;
    }

    private UI.ContextMenuSystem? _contextMenu;

    private UI.ContextMenuSystem? FindContextMenu()
    {
        if (_contextMenu == null || _contextMenu.IsDestroyed)
            _contextMenu = Slot?.ActiveUserRoot?.Slot?.GetComponentInChildren<UI.ContextMenuSystem>();
        return _contextMenu;
    }

    private bool IsContextMenuOpenByThisHand()
    {
        var menu = _contextMenu;
        if (menu == null || menu.IsDestroyed || !menu.IsOpen.Value)
            return false;
        return menu.CurrentContext?.Side == Side.Value;
    }

    private void ProcessGrip(InteractionLaser laser)
    {
        // Grab state freezes while this hand's menu is open: letting go of grip
        // to work the menu must not drop the held object, or Destroy/Duplicate
        // would always act on an empty hand. The release edge is processed
        // after the menu closes.
        if (IsContextMenuOpenByThisHand())
            return;

        if (_gripHeld && !_prevGripHeld)
        {
            TryGrabCurrentTarget(laser);
        }
        else if (!_gripHeld && _prevGripHeld)
        {
            _grabber?.ReleaseAll();
            ResetInteraction(releaseHeld: false);
        }

        _prevGripHeld = _gripHeld;
    }

    private void TryGrabCurrentTarget(InteractionLaser laser)
    {
        if (_grabber == null)
        {
            return;
        }

        var grabbable = FindBestGrabbable(laser.CurrentTarget, laser.CurrentHitSlot);
        if (grabbable == null)
        {
            return;
        }

        var holder = _grabber.HolderSlot;
        if (holder == null)
        {
            return;
        }

        _laserGrabDistance = MathF.Max(0.05f, laser.CurrentHitDistance);
        _holderAxisOffset = 0f;
        _holderRotationOffset = floatQ.Identity;
        _holderRotationReference = GetHeadFacingRotation(laser);
        RotationMode.Value = LaserRotationMode.AxisY;

        holder.GlobalPosition = laser.CurrentHitPoint;
        holder.GlobalScale = float3.One;
        UpdateHolderRotation(laser, holder);

        if (!_grabber.TryGrab(grabbable))
        {
            return;
        }

        _isHoldingWithLaser = true;
    }

    private void ProcessLaserHold(InteractionLaser laser, float delta)
    {
        if (_grabber == null || !_grabber.IsHoldingObjects)
        {
            ResetInteraction(releaseHeld: false);
            return;
        }

        var holder = _grabber.HolderSlot;
        if (holder == null || holder.IsRemoved)
        {
            ResetInteraction(releaseHeld: false);
            return;
        }

        ApplyHoldInputs(laser, holder, delta);
        _laserGrabDistance = Clamp(_laserGrabDistance, 0.05f, MathF.Max(laser.MaxDistance.Value, 0.05f));
        UpdateHolderPosition(laser, holder);
        UpdateHolderRotation(laser, holder);
    }

    private void ApplyHoldInputs(InteractionLaser laser, Slot holder, float delta)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            SetDesktopInputSuppression(false);
            return;
        }

        if (input.IsVRActive)
        {
            SetDesktopInputSuppression(false);
            ApplyVrHoldInputs(laser, input, delta, holder);
            return;
        }

        bool freezeCursor = IsKeyHeld(input, Key.E);
        SetDesktopInputSuppression(freezeCursor);

        float scroll = input.Mouse?.ScrollWheelDelta.Value ?? 0f;
        if (scroll != 0f)
        {
            if (IsShiftHeld(input) && CanScaleHeldObjects())
            {
                ScaleHolder(holder, scroll * HoldScaleStep.Value);
            }
            else
            {
                float step = MathF.Max(0.05f, _laserGrabDistance * HoldScrollStep.Value);
                _laserGrabDistance += scroll * step;
            }
        }

        if (!freezeCursor)
        {
            return;
        }

        float2 mouseDelta = input.Mouse?.DirectDelta.Value ?? float2.Zero;
        if (mouseDelta == float2.Zero)
        {
            return;
        }

        if (IsShiftHeld(input))
        {
            _holderAxisOffset += mouseDelta.x * HoldRotationSensitivity.Value;
        }
        else
        {
            ApplyFreeformRotation(laser, new float3(
                -mouseDelta.y * HoldRotationSensitivity.Value,
                mouseDelta.x * HoldRotationSensitivity.Value,
                0f));
        }
    }

    private void ApplyVrHoldInputs(InteractionLaser laser, InputInterface input, float delta, Slot holder)
    {
        var controller = Side.Value == Chirality.Left ? input.LeftController : input.RightController;
        if (controller == null)
        {
            return;
        }

        float slide = ApplyDeadzone(controller.ThumbstickPosition.Y, 0.15f);
        float rotate = ApplyDeadzone(controller.ThumbstickPosition.X, 0.15f);

        if (controller.SecondaryButtonPressed && slide != 0f && CanScaleHeldObjects())
        {
            ScaleHolder(holder, slide * delta);
        }
        else if (slide != 0f)
        {
            _laserGrabDistance += slide * MathF.Max(1f, _laserGrabDistance) * 4f * delta;
        }

        if (rotate != 0f)
        {
            _holderAxisOffset += rotate * MathF.PI * 2f * delta;
        }
    }

    private void UpdateHolderPosition(InteractionLaser laser, Slot holder)
    {
        float3 origin = laser.RayOrigin;
        float3 direction = laser.RayDirection;
        if (direction.Length <= 0.0001f)
        {
            ResolveFallbackRay(laser, out origin, out direction);
        }

        holder.GlobalPosition = origin + direction.Normalized * _laserGrabDistance;
    }

    private void UpdateHolderRotation(InteractionLaser laser, Slot holder)
    {
        if (RotationMode.Value == LaserRotationMode.Unconstrained)
        {
            holder.GlobalRotation = Slot.GlobalRotation;
            return;
        }

        var root = FindUserRootSlot();
        var head = laser.FindHeadSlot();
        float3 rootUp = root?.Up ?? float3.Up;
        if (rootUp.Length <= 0.0001f)
        {
            rootUp = float3.Up;
        }
        rootUp = rootUp.Normalized;

        floatQ reference = _holderRotationReference ?? GetHeadFacingRotation(laser) ?? (head?.GlobalRotation ?? Slot.GlobalRotation);
        float3 rightAxis = reference * float3.Right;
        if (rightAxis.Length <= 0.0001f)
        {
            rightAxis = Slot.Right;
        }
        rightAxis = rightAxis.Normalized;

        float3 forward = ComputeHolderForward(laser, holder, reference, root, head);
        if (forward.Length <= 0.0001f)
        {
            forward = reference * float3.Backward;
        }
        if (forward.Length <= 0.0001f)
        {
            forward = float3.Backward;
        }
        forward = forward.Normalized;

        float angle = _holderAxisOffset;
        switch (RotationMode.Value)
        {
            case LaserRotationMode.AxisX:
                forward = (floatQ.AxisAngle(rightAxis, angle) * forward).Normalized;
                break;
            case LaserRotationMode.AxisY:
                forward = (floatQ.AxisAngle(rootUp, angle) * forward).Normalized;
                break;
            case LaserRotationMode.AxisZ:
                rootUp = (floatQ.AxisAngle(forward, angle) * rootUp).Normalized;
                break;
        }

        holder.GlobalRotation = _holderRotationOffset * floatQ.LookRotation(forward, rootUp);
    }

    private float3 ComputeHolderForward(InteractionLaser laser, Slot holder, floatQ reference, Slot? root, Slot? head)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive)
        {
            return reference * float3.Backward;
        }

        if (root == null || head == null)
        {
            return laser.RayDirection;
        }

        float3 fromHead = holder.GlobalPosition - head.GlobalPosition;
        float3 radial = ProjectOnPlane(fromHead, root.Up);
        float3 ray = ProjectOnPlane(laser.RayDirection, root.Up);

        if (radial.Length > 0.0001f && ray.Length > 0.0001f)
        {
            return (radial.Normalized + ray.Normalized).Normalized;
        }
        if (radial.Length > 0.0001f)
        {
            return radial.Normalized;
        }
        return laser.RayDirection;
    }

    private void ApplyFreeformRotation(InteractionLaser laser, float3 rotation)
    {
        if (_grabber == null || rotation.Length <= 0.0001f)
        {
            return;
        }

        floatQ delta = floatQ.Euler(rotation);
        var holder = _grabber.HolderSlot;
        if (holder == null)
        {
            _holderRotationOffset = delta * _holderRotationOffset;
            return;
        }

        var slots = GetGrabbedSlots();
        if (slots.Count == 0)
        {
            _holderRotationOffset = delta * _holderRotationOffset;
            return;
        }

        floatQ viewRotation = GetHeadFacingRotation(laser) ?? (laser.FindHeadSlot()?.GlobalRotation ?? Slot.GlobalRotation);
        floatQ inverseView = viewRotation.Inverse;
        foreach (var slot in slots)
        {
            floatQ globalRotation = viewRotation * (delta * (inverseView * slot.GlobalRotation));
            slot.GlobalRotation = globalRotation;
        }
    }

    private void ProcessAlignPress(InteractionLaser laser)
    {
        double now = World?.TotalTime ?? 0.0;
        if (now - _lastAlignPress < 0.5)
        {
            RotationMode.Value = RotationMode.Value == LaserRotationMode.AxisY
                ? LaserRotationMode.Unconstrained
                : LaserRotationMode.AxisY;
            PreserveHeldGlobalTransforms(() =>
            {
                if (_grabber?.HolderSlot != null)
                {
                    UpdateHolderRotation(laser, _grabber.HolderSlot);
                }
            });
            AlignHeldObjects(laser, GetLaserRotationAxis());
            _lastAlignPress = -1000.0;
            return;
        }

        AlignHeldObjects(laser, float3.Up);
        _lastAlignPress = now;
    }

    private void AlignHeldObjects(InteractionLaser laser, float3 referenceAxis)
    {
        foreach (var slot in GetGrabbedSlots())
        {
            if (TryAlignUiSlot(laser, slot))
            {
                continue;
            }
            AlignSlotToReferenceAxis(slot, referenceAxis);
        }
    }

    private static bool TryAlignUiSlot(InteractionLaser laser, Slot slot)
    {
        if (slot.GetComponent<Helio.UI.Canvas>() == null && slot.GetComponent<Helio.UI.RectTransform>() == null)
        {
            return false;
        }

        var head = laser.FindHeadSlot();
        if (head == null)
        {
            return false;
        }

        float3 faceDirection = head.GlobalPosition - slot.GlobalPosition;
        if (faceDirection.Length <= 0.0001f)
        {
            faceDirection = head.Forward;
        }
        if (faceDirection.Length <= 0.0001f)
        {
            return false;
        }
        faceDirection = faceDirection.Normalized;

        float3 upDirection = ProjectOnPlane(head.Up, faceDirection);
        if (upDirection.Length <= 0.0001f)
        {
            upDirection = ProjectOnPlane(float3.Up, faceDirection);
        }
        if (upDirection.Length <= 0.0001f)
        {
            upDirection = float3.Right;
        }

        slot.GlobalRotation = floatQ.LookRotation(-faceDirection, upDirection.Normalized);
        return true;
    }

    private void AlignSlotToReferenceAxis(Slot slot, float3 referenceAxis)
    {
        var root = FindUserRootSlot();
        float3 referenceGlobal = root != null ? root.LocalDirectionToGlobal(referenceAxis) : referenceAxis;
        if (referenceGlobal.Length <= 0.0001f)
        {
            return;
        }
        referenceGlobal = referenceGlobal.Normalized;

        float3 referenceInTarget = slot.GlobalDirectionToLocal(referenceGlobal);
        if (referenceInTarget.Length <= 0.0001f)
        {
            return;
        }

        float3 localAxis = GetClosestLocalAxis(referenceInTarget.Normalized);
        float3 selectedGlobal = slot.LocalDirectionToGlobal(localAxis);
        if (selectedGlobal.Length <= 0.0001f)
        {
            return;
        }

        var parent = slot.Parent;
        float3 selectedParent = parent != null ? parent.GlobalDirectionToLocal(selectedGlobal.Normalized) : selectedGlobal.Normalized;
        float3 referenceParent = parent != null ? parent.GlobalDirectionToLocal(referenceGlobal) : referenceGlobal;
        floatQ delta = RotationFromTo(selectedParent, referenceParent);
        slot.GlobalRotation = delta * slot.GlobalRotation;
    }

    private void PreserveHeldGlobalTransforms(Action action)
    {
        var slots = GetGrabbedSlots();
        var positions = new List<float3>(slots.Count);
        var rotations = new List<floatQ>(slots.Count);
        foreach (var slot in slots)
        {
            positions.Add(slot.GlobalPosition);
            rotations.Add(slot.GlobalRotation);
        }

        action();

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsRemoved)
            {
                continue;
            }

            slots[i].GlobalPosition = positions[i];
            slots[i].GlobalRotation = rotations[i];
        }
    }

    public void ResetInteraction(bool releaseHeld)
    {
        if (releaseHeld)
        {
            _grabber?.ReleaseAll();
        }

        SetDesktopInputSuppression(false);
        _isHoldingWithLaser = false;
        _laserGrabDistance = 0f;
        _holderAxisOffset = 0f;
        _holderRotationOffset = floatQ.Identity;
        _holderRotationReference = null;
        RotationMode.Value = LaserRotationMode.AxisY;
        _primaryHeld = false;
        _prevPrimaryHeld = false;
        _secondaryHeld = false;
        _prevSecondaryHeld = false;
        _activePrimaryToolItem = null;
        _activeSecondaryToolItem = null;
        _gripHeld = false;
        _prevGripHeld = false;
    }

    private List<Slot> GetGrabbedSlots()
    {
        var slots = new List<Slot>();
        if (_grabber == null)
        {
            return slots;
        }

        foreach (var grabbable in _grabber.GrabbedObjects)
        {
            if (grabbable is Component component && component.Slot != null && !component.Slot.IsRemoved)
            {
                slots.Add(component.Slot);
            }
        }
        return slots;
    }

    private static IGrabbable? FindBestGrabbable(IInteractionTarget? target, Slot? hitSlot)
    {
        IGrabbable? best = target as IGrabbable;
        int bestPriority = best?.GrabPriority ?? int.MinValue;

        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var grabbable in current.GetComponentsImplementing<IGrabbable>())
            {
                if (grabbable.AllowOnlyPhysicalGrab)
                {
                    continue;
                }

                if (grabbable.GrabPriority > bestPriority)
                {
                    best = grabbable;
                    bestPriority = grabbable.GrabPriority;
                }
            }

            current = current.Parent;
        }

        return best;
    }

    private float3 GetLaserRotationAxis()
    {
        return RotationMode.Value switch
        {
            LaserRotationMode.AxisX => float3.Right,
            LaserRotationMode.AxisY => float3.Up,
            LaserRotationMode.AxisZ => float3.Forward,
            _ => float3.Zero
        };
    }

    private Slot? FindUserRootSlot() => Slot?.ActiveUserRoot?.Slot;

    private static floatQ? GetHeadFacingRotation(InteractionLaser laser)
    {
        var head = laser.FindHeadSlot();
        if (head == null)
        {
            return null;
        }

        float3 forward = head.GlobalRotation * float3.Backward;
        forward = ProjectOnPlane(forward, float3.Up);
        if (forward.Length <= 0.0001f)
        {
            forward = float3.Backward;
        }
        return floatQ.LookRotation(forward.Normalized, float3.Up);
    }

    private void ResolveFallbackRay(InteractionLaser laser, out float3 origin, out float3 direction)
    {
        var input = Engine.Current?.InputInterface;
        if (input != null && !input.IsVRActive)
        {
            var head = laser.FindHeadSlot();
            origin = head?.GlobalPosition ?? Slot.GlobalPosition;
            direction = (head?.GlobalRotation ?? Slot.GlobalRotation) * float3.Backward;
        }
        else
        {
            origin = laser.Slot.GlobalPosition;
            direction = -laser.Slot.Forward;
        }

        if (direction.Length <= 0.0001f)
        {
            direction = float3.Backward;
        }
        direction = direction.Normalized;
    }

    private bool CanScaleHeldObjects()
    {
        if (_grabber == null || _grabber.GrabbedObjects.Count == 0)
        {
            return false;
        }

        foreach (var grabbable in _grabber.GrabbedObjects)
        {
            if (grabbable == null || !grabbable.Scalable)
            {
                return false;
            }

            if (grabbable is Component component && component.IsDestroyed)
            {
                return false;
            }
        }
        return true;
    }

    private void ScaleHolder(Slot holder, float delta)
    {
        float factor = MathF.Max(0.05f, 1f + delta);
        holder.GlobalScale = holder.GlobalScale * factor;
    }

    private void SetDesktopInputSuppression(bool active)
    {
        if (_desktopInputSuppressed == active)
        {
            return;
        }

        _desktopInputSuppressed = active;
        UserInputState.ForFocusedLocalUser?.SetDesktopInputSuppressed(this, active);
    }

    private static float3 ProjectOnPlane(float3 vector, float3 normal)
    {
        if (normal.Length <= 0.0001f)
        {
            return vector;
        }
        normal = normal.Normalized;
        return vector - normal * float3.Dot(vector, normal);
    }

    private static float3 GetClosestLocalAxis(float3 direction)
    {
        float3 best = float3.Up;
        float bestDot = float3.Dot(best, direction);
        TestLocalAxis(float3.Down, direction, ref best, ref bestDot);
        TestLocalAxis(float3.Right, direction, ref best, ref bestDot);
        TestLocalAxis(float3.Left, direction, ref best, ref bestDot);
        TestLocalAxis(float3.Forward, direction, ref best, ref bestDot);
        TestLocalAxis(float3.Backward, direction, ref best, ref bestDot);
        return best;
    }

    private static void TestLocalAxis(float3 axis, float3 direction, ref float3 best, ref float bestDot)
    {
        float dot = float3.Dot(axis, direction);
        if (dot > bestDot)
        {
            bestDot = dot;
            best = axis;
        }
    }

    private static floatQ RotationFromTo(float3 from, float3 to)
    {
        if (from.Length <= 0.0001f || to.Length <= 0.0001f)
        {
            return floatQ.Identity;
        }

        from = from.Normalized;
        to = to.Normalized;
        float dot = System.Math.Clamp(float3.Dot(from, to), -1f, 1f);
        if (dot > 0.9999f)
        {
            return floatQ.Identity;
        }

        if (dot < -0.9999f)
        {
            float3 fallback = MathF.Abs(float3.Dot(from, float3.Up)) > 0.9f ? float3.Right : float3.Up;
            float3 axis = float3.Cross(from, fallback);
            return axis.Length <= 0.0001f ? floatQ.Identity : floatQ.AxisAngle(axis.Normalized, MathF.PI);
        }

        float3 rotationAxis = float3.Cross(from, to);
        return rotationAxis.Length <= 0.0001f
            ? floatQ.Identity
            : floatQ.AxisAngle(rotationAxis.Normalized, MathF.Acos(dot));
    }

    private static bool ReadPrimaryPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            return false;
        }

        bool vrTrigger = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController.TriggerPressed
            : input.RightController.TriggerPressed;
        if (vrTrigger)
        {
            return true;
        }

        return !input.IsVRActive &&
               laser.ControllerSide.Value == Chirality.Right &&
               input.Mouse?.LeftButton.Held == true;
    }

    private static bool ReadGripPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            return false;
        }

        bool vrGrip = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController.GripPressed || input.LeftController.GripValue > 0.5f
            : input.RightController.GripPressed || input.RightController.GripValue > 0.5f;
        if (vrGrip)
        {
            return true;
        }

        return !input.IsVRActive &&
               laser.ControllerSide.Value == Chirality.Right &&
               input.Mouse?.RightButton.Held == true;
    }

    private static bool ReadSecondaryPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            return false;
        }

        bool vrSecondary = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController.SecondaryButtonPressed
            : input.RightController.SecondaryButtonPressed;
        if (vrSecondary)
        {
            return true;
        }

        // Desktop tool secondary is R (middle mouse is free; the context menu
        // gets its own T binding so nothing conflicts).
        return !input.IsVRActive &&
               laser.ControllerSide.Value == Chirality.Right &&
               input.Keyboard?.IsKeyPressed(Key.R) == true;
    }

    private static bool IsShiftHeld(InputInterface input)
    {
        return IsKeyHeld(input, Key.LeftShift) || IsKeyHeld(input, Key.RightShift);
    }

    private static bool IsKeyHeld(InputInterface input, Key key)
    {
        return input.Keyboard?.IsKeyPressed(key) == true;
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        return MathF.Abs(value) < deadzone ? 0f : value;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}
