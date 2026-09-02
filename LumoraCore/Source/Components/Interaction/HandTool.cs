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
    public readonly Sync<float> GrabSmoothing = new();
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
    // Handle drag started by a bare-hand primary press (no tool equipped); the dev tool tracks its own.
    private Gizmos.TransformHandle? _bareHandle;
    private ToolItem? _activePrimaryToolItem;
    private ToolItem? _activeSecondaryToolItem;
    private bool _isHoldingWithLaser;
    private float _laserGrabDistance;
    private float _holderAxisOffset;
    private floatQ _holderRotationOffset = floatQ.Identity;
    private floatQ? _holderRotationReference;
    private bool _desktopInputSuppressed;
    private bool _scrollWheelCaptured;
    private double _lastAlignPress = -1000.0;
    private long _holdPoseFrame = long.MinValue;
    private long _holdPoseWrittenFrame = long.MinValue;

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
        // Base damping rate for the laser's aim while this hand is carrying something, in 1/s (a 1/8 second
        // time constant). Below the laser's bare-pointer SmoothSpeed because a load on the end of a
        // several-metre ray magnifies every bit of sampling noise into a visible swing. Sized so a stop
        // settles inside 0.2s with no overshoot (first order, so it can't overshoot) while a 90 degree sweep
        // in a quarter second ends about 10 degrees behind and closes that in another 0.2s. -xlinka
        GrabSmoothing.Value = 8f;
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
            _laser.ArmRaySmoothing(false);
            _laser.SetExclusiveRoot(null);
            _laser.SetDormant(true);
            SetDesktopInputSuppression(false);
            SetScrollWheelCapture(false);
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
        bool uiPress = _primaryHeld && (menuVisible || !IsHoldingObjectsWithLaser)
            && EyedropperFor(_laser) == null;
        bool carryingOnLaser = IsHoldingObjectsWithLaser && !menuVisible;
        // Damp the aim only while something is actually riding the laser. A bare pointer wants to be
        // pixel-exact, and while the menu is open the mouse IS the pointer.
        _laser.ArmRaySmoothing(carryingOnLaser, GrabSmoothing.Value);
        _laser.SetToolState(uiPress, carryingOnLaser);
        // Hit classification belongs to whatever is equipped: the tool is the only thing that knows
        // which chrome it wants pulled in front of the world. A modal menu takes that away - while one
        // is up the beam may touch nothing but the menu, and a tool preferring its own targets through
        // the modal filter would be fighting it. Pushed per frame rather than on equip because the
        // beam is built lazily and an equip can land before it exists. -xlinka
        _laser.SetHitClassifier(menuVisible ? null : ActiveToolItem.Target as ILaserHitClassifier);
        _laser.RefreshNow(delta);
        ProcessPrimary(_laser);
        ProcessSecondary(_laser);
        ProcessMenuKey(_laser);
        ProcessGrip(_laser);

        // The wheel is the held object's distance control while something rides the laser. Say so, or the
        // third-person orbit zooms the camera on the same notch that pulls the object in. -xlinka
        SetScrollWheelCapture(!vrActive && IsHoldingObjectsWithLaser && !menuVisible);

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

    private ToolItem? _refusedEquip;

    private void EnsureRig()
    {
        if (Slot == null || Slot.IsRemoved)
        {
            return;
        }

        // Builds the tool rig (Grabber/Laser/Tool Holder slots + components) under the HandTool slot. This is
        // idempotent: every slot is FindChild-or-add, so a non-owner peer adopts the rig that replicated from the
        // owner instead of minting a duplicate. The owner's writes can be permission-denied for a beat during join
        // (the User<->UserRoot link lags), but we run this from OnUpdate every frame as well as OnStart, so the next
        // frame retries and lands once the link resolves - no bypass needed, just let it throw and re-drive. -xlinka
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

        // Putting a tool DOWN is never gated - a role losing ToolUse mid-session must not be left
        // holding something it cannot let go of. EnsureRig re-offers the holder's item every frame, so
        // the refusal is logged once per item or the console fills up. -xlinka
        if (item != null && !item.AllowsEquip(World?.LocalUser))
        {
            if (!ReferenceEquals(_refusedEquip, item))
            {
                _refusedEquip = item;
                Logging.Logger.Log($"{item.GetType().Name} not equipped: tools are not available to you in this world.");
            }
            return;
        }
        _refusedEquip = null;

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
            DockInHolder(item);
        }
    }

    // Physically snap the equipped item into the Tool Holder (a world tool stays put without this - the equip
    // link alone doesn't move anything). No-op for items already in the holder (the rig's default tool). -xlinka
    private void DockInHolder(ToolItem item)
    {
        var itemSlot = item?.Slot;
        if (itemSlot == null || itemSlot.IsDestroyed)
            return;
        EnsureRig();
        if (_toolHolderSlot == null || itemSlot == _toolHolderSlot || itemSlot.IsDescendantOf(_toolHolderSlot))
            return;

        // Still held (menu equip releases first; this covers stragglers) - let go before reparenting or the
        // grabber keeps a stale ref to a slot it no longer holds.
        var grabbable = itemSlot.GetComponent<Grabbable>();
        if (grabbable != null && grabbable.IsGrabbed)
            grabbable.Grabber?.Release(grabbable);

        itemSlot.SetParent(_toolHolderSlot, preserveGlobalTransform: false);
        itemSlot.LocalPosition.Value = float3.Zero;
        itemSlot.LocalRotation.Value = floatQ.Identity;
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

        // Asked again at the press and not only at the equip: a role can be changed, or the world
        // locked, while the thing is already in your hand. A press by someone who has lost the right
        // to use it does nothing at all rather than reaching the tool's own handler.
        if (!toolItem.AllowsEquip(World?.LocalUser))
        {
            return null;
        }

        return toolItem;
    }

    private void SampleInput(InteractionLaser laser)
    {
        // Tool secondary on desktop is a KEY, and a focused text field owns the keyboard outright -
        // the keyboard source is gated at that point, so typing never reaches an action and the VR
        // controller buttons carry on regardless.
        _primaryHeld = ReadPrimaryPressed(laser);
        _secondaryHeld = ReadSecondaryPressed(laser);
        _gripHeld = ReadGripPressed(laser);
    }

    private void ProcessPrimary(InteractionLaser laser)
    {
        // A press while this hand's menu is up is a menu press and nothing else. The canvas already
        // receives it (uiPress stays true with the menu open so held-object actions are clickable), so
        // letting the chain below run too meant "Destroy" on a held reference card first activated the
        // card - it opened its inspector and spent itself - and Destroy then found an empty hand. -xlinka
        if (_primaryHeld && !_prevPrimaryHeld && IsContextMenuOpenByThisHand())
        {
            _prevPrimaryHeld = _primaryHeld;
            return;
        }

        // An armed color picker owns the next world press outright, ahead of the tool, the held object
        // and the hit target - the whole point of the mode is that the click means "sample that", not
        // whatever it would otherwise have meant.
        if (_primaryHeld && !_prevPrimaryHeld && TryEyedropperPress(laser))
        {
            _prevPrimaryHeld = _primaryHeld;
            return;
        }

        if (_primaryHeld && !_prevPrimaryHeld)
        {
            var toolItem = GetUsableToolItem();
            if (toolItem != null && toolItem.OnPrimaryPress())
            {
                _activePrimaryToolItem = toolItem;
            }
            else if (IsHoldingObjectsWithLaser)
            {
                // Order matters. Holding with the laser SUPPRESSES canvas presses (uiPress below), so a
                // press aimed at a UI row can never reach the row's own button - the row has to be
                // offered the hand's contents from here instead. That has to happen BEFORE the held
                // object gets the press, or clicking a reference field while carrying a card would run
                // the CARD's action (open an inspector on its target) and spend the card without ever
                // assigning it: you aim at the field you wanted to fill, click, and lose the card. So:
                // a receiver under the pointer wins, then the held object's own action, then align.
                // -xlinka
                if (!TryDropProxyOnUI(laser) && !TryActivateHeldObject())
                    ProcessAlignPress(laser);
            }
            else if (laser.CurrentTarget is Gizmos.TransformHandle handle && handle.BeginToolDrag(laser))
            {
                // A gizmo handle is a control, not an object: primary on it drags whether or not a tool is
                // equipped. The inspector spawns gizmos with no tool in hand, and a handle that only answers
                // the dev tool's primary (or a grip) reads as dead to a mouse user. -xlinka
                _bareHandle = handle;
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
        else if (_primaryHeld && _bareHandle != null)
        {
            if (!_bareHandle.IsDragging || _bareHandle.IsDestroyed)
                _bareHandle = null;
        }
        else if (!_primaryHeld && _prevPrimaryHeld && _activePrimaryToolItem != null)
        {
            _activePrimaryToolItem.OnPrimaryRelease();
            _activePrimaryToolItem = null;
        }
        else if (!_primaryHeld && _prevPrimaryHeld && _bareHandle != null)
        {
            _bareHandle.EndToolDrag();
            _bareHandle = null;
        }

        _prevPrimaryHeld = _primaryHeld;
    }

    // Eyedropper. While a color picker is armed the next world press samples what the beam is on and
    // goes no further - not to the tool, not to the held object, and not to a canvas under the pointer
    // (see uiPress): clicking somebody else's panel to sample its color must not also press the button
    // you happened to aim at.
    //
    // The armed panel's OWN surface is the exception, on both paths. Its Cancel, its Save and the Pick
    // toggle itself all have to stay clickable, or arming the mode is a trap you cannot get out of.
    // Returns the panel that owns this press, or null when the press is nobody's business. -xlinka
    private static ColorPickerPanel? EyedropperFor(InteractionLaser laser)
    {
        var sampler = ColorPickerPanel.ActiveSampler;
        if (sampler == null || sampler.IsDestroyed || !sampler.IsSampling)
        {
            return null;
        }

        var hitSlot = laser.CurrentHitSlot;
        var panelSlot = sampler.Slot;
        if (hitSlot != null && panelSlot != null && !panelSlot.IsDestroyed
            && (ReferenceEquals(hitSlot, panelSlot) || hitSlot.IsDescendantOf(panelSlot)))
        {
            return null;
        }
        return sampler;
    }

    // A miss leaves the picker's value alone but still ends the mode: a press that did nothing visible
    // and left you armed reads as broken.
    private static bool TryEyedropperPress(InteractionLaser laser)
    {
        if (EyedropperFor(laser) is not { } sampler)
        {
            return false;
        }

        // Interaction hits only cover interaction targets; plain scenery stops the beam at its
        // collider, whose slot and point the laser now keeps, so the material walk works on a ground
        // plate too. The pixel fallback only remains for things with no collider at all. -xlinka
        var hitSlot = laser.CurrentHitSlot ?? laser.CurrentColliderHitSlot;
        float3 point = laser.CurrentHitSlot != null ? laser.CurrentHitPoint
            : laser.CurrentColliderHitSlot != null ? laser.CurrentColliderHitPoint
            : laser.HasAimPoint ? laser.AimPoint : laser.CurrentHitPoint;

        if (ColorSampling.TrySample(hitSlot, point, out var sampled))
        {
            sampler.ApplySampledColor(sampled);
        }
        sampler.DisarmSampling();
        return true;
    }

    // Cancel works wherever the beam is pointing, the picker's own panel included - the secondary is not
    // a click on anything, it is "get me out of this mode".
    private static bool TryCancelEyedropper()
    {
        var sampler = ColorPickerPanel.ActiveSampler;
        if (sampler == null || sampler.IsDestroyed || !sampler.IsSampling)
        {
            return false;
        }
        sampler.DisarmSampling();
        return true;
    }

    // Offer the primary press to any held object that wants to run its own action instead of aligning
    // (a reference card opens its target). Snapshot the hold list first: a claimer may remove itself
    // from the hand mid-iteration. Returns true when one consumed the press. -xlinka
    private bool TryActivateHeldObject()
    {
        if (_grabber == null)
            return false;

        var held = new List<IGrabbable>(_grabber.GrabbedObjects);
        foreach (var grabbable in held)
        {
            if (grabbable is not Component component || component.Slot == null || component.IsDestroyed)
                continue;
            foreach (var activatable in component.Slot.GetComponentsImplementing<IHeldActivatable>())
            {
                if (activatable is Component c && (!c.Enabled.Value || c.IsDestroyed))
                    continue;
                if (activatable.OnHeldActivate(_grabber))
                    return true;
            }
        }
        return false;
    }

    private void ProcessSecondary(InteractionLaser laser)
    {
        if (_secondaryHeld && !_prevSecondaryHeld)
        {
            // Secondary is this tool's "back out of whatever mode you are in", so it cancels an armed
            // eyedropper before anything else looks at the press. Escape is not the cancel here: it is
            // already the mouse-capture toggle AND the dashboard toggle, and stealing it would make
            // arming the picker break both. -xlinka
            if (TryCancelEyedropper())
            {
                _prevSecondaryHeld = _secondaryHeld;
                return;
            }

            // While our menu is open, the button closes it before any tool
            // gets a say - otherwise an equipped tool would eat the press and
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

    // Desktop context menu toggle. Only the right hand listens so both hands cannot double-toggle;
    // in VR the menu is summoned by the tool itself, not from here. Whichever control is bound
    // (stock: middle click, T, or the pad's top face button) toggles on its press edge, and a
    // focused text field takes the keyboard out of play before an action ever sees a keystroke.
    // -xlinka
    private void ProcessMenuKey(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || input.IsVRActive || Side.Value != Chirality.Right)
        {
            return;
        }

        if (input.Actions?.Right.ContextMenu.Pressed == true)
        {
            ToggleContextMenu(laser);
        }
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
        // Resolve, don't just read the cache: a confirm menu opened by something else (a tool's
        // touch-to-equip prompt) never goes through ToggleContextMenu, and an unfilled cache reads as
        // "no menu", so the laser keeps its world aim and the prompt can't be clicked until the
        // radial menu has been opened once by hand. -xlinka
        var menu = FindContextMenu();
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
            // Prefer a touch (physical) grab when something is within reach of the hand; fall back to the
            // laser grab when nothing is. -xlinka
            if (!TryTouchGrab())
            {
                TryGrabCurrentTarget(laser);
            }
        }
        else if (!_gripHeld && _prevGripHeld)
        {
            // Letting go over a UI row that accepts references consumes the held card before the
            // release puts anything back into the world. -xlinka
            TryDropProxyOnUI(laser);
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

        // A UI row under the pointer that offers a reference card wins over grabbing the panel:
        // gripping a member row pulls the card, gripping the frame still moves the window. -xlinka
        var grabbable = TryPullProxyFromUI(laser) ?? FindBestGrabbable(laser.CurrentTarget, laser.CurrentHitSlot);
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
        // Frozen at grab, never recomputed. The held object keeps the facing it had when you picked it up;
        // letting it re-derive from the head every frame makes it swing to face you as the view pitches and
        // yaws, which is the last thing you want while trying to place something. Resolve it to a real value
        // here so UpdateHolderRotation can never fall through to the chasing path. -xlinka
        _holderRotationReference = GetHeadFacingRotation(laser)
            ?? laser.FindHeadSlot()?.GlobalRotation
            ?? Slot.GlobalRotation;
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

    // Pull a reference card out of a hovered UI panel: resolve the exact row under the pointer
    // (the interactable hit test can't see labels or plain containers) and ask up its parent chain
    // for a proxy source. Returns the freshly spawned card, ready to grab at the hit point. -xlinka
    private IGrabbable? TryPullProxyFromUI(InteractionLaser laser)
    {
        if (_grabber == null || laser.CurrentTarget is not Helio.UI.Canvas canvas || canvas.Slot == null)
        {
            return null;
        }
        if (!canvas.TryResolveUISlot(laser.RayOrigin, laser.RayDirection, out var uiSlot, out var worldPoint) || uiSlot == null)
        {
            return null;
        }
        var source = FindUIBehavior<IProxySource>(uiSlot, canvas.Slot);
        return source?.TryCreateProxy(_grabber, worldPoint);
    }

    // Offer everything in the hand to a reference receiver under the pointer. Two callers: the grip
    // release edge (before the release puts the held items back into the world) and the primary press
    // while laser-holding. Returns true when a receiver consumed something. -xlinka
    private bool TryDropProxyOnUI(InteractionLaser laser)
    {
        if (_grabber == null || !_grabber.IsHoldingObjects)
        {
            return false;
        }
        if (laser.CurrentTarget is not Helio.UI.Canvas canvas || canvas.Slot == null)
        {
            return false;
        }
        if (!canvas.TryResolveUISlot(laser.RayOrigin, laser.RayDirection, out var uiSlot, out _) || uiSlot == null)
        {
            return false;
        }
        var receiver = FindUIBehavior<IProxyReceiver>(uiSlot, canvas.Slot);
        // Snapshot the hold list: a receiver consumes the card it took (releases it from this hand and
        // destroys it), which mutates the grabber's list mid-call. -xlinka
        if (receiver == null)
        {
            return false;
        }
        var held = new List<IGrabbable>(_grabber.GrabbedObjects);
        return held.Count > 0 && receiver.TryReceiveProxy(held, _grabber);
    }

    // First T on the slot or its parents, stopping at the canvas root (UI rows never reach outside
    // their own panel).
    private static T? FindUIBehavior<T>(Slot start, Slot canvasRoot) where T : class
    {
        for (var current = start; current != null; current = current.Parent)
        {
            foreach (var behavior in current.GetComponentsImplementing<T>())
            {
                if (behavior is Component component && component.Enabled.Value && !component.IsDestroyed)
                {
                    return behavior;
                }
            }
            if (ReferenceEquals(current, canvasRoot))
            {
                break;
            }
        }
        return null;
    }

    // Hand reach for a touch grab, in metres at unit user scale. Roughly the grab-sphere of the hand.
    private const float TouchGrabRadius = 0.1f;

    // Touch (physical) grab: in VR, if a grabbable collider is physically within reach of the hand, grab it
    // straight into the hand instead of using the laser. The held object rides the grabber slot via the
    // holder (pinned to the hand), and an IGrabAlignable object snaps to its defined in-hand pose. Gated to
    // VR: on desktop the hand isn't a tracked physical thing, so this no-ops and laser grab runs. Returns
    // false when nothing is in reach. -xlinka
    private bool TryTouchGrab()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive || _grabber == null)
        {
            return false;
        }

        var handSlot = _grabber.Slot;
        var holder = _grabber.HolderSlot;
        if (handSlot == null || holder == null)
        {
            return false;
        }

        // Pin the holder to the hand before grabbing so the object rides the controller directly - Grab
        // keeps the object's world pose, capturing its offset from the hand. -xlinka
        holder.LocalPosition.Value = float3.Zero;
        holder.LocalRotation.Value = floatQ.Identity;
        holder.LocalScale.Value = float3.One;

        var scale = handSlot.GlobalScale;
        float avgScale = (scale.x + scale.y + scale.z) / 3f;
        float radius = TouchGrabRadius * (avgScale > 0.0001f ? avgScale : 1f);

        if (!_grabber.TryGrabNearby(handSlot.GlobalPosition, radius, out var grabbed) || grabbed == null)
        {
            return false;
        }

        TryAlignGrabbed(grabbed);

        // Touch-held objects follow the hand through the hierarchy, so the laser-hold path must not drive
        // them. Leaving _isHoldingWithLaser false also keeps the laser cursor in its normal state. -xlinka
        _isHoldingWithLaser = false;
        return true;
    }

    // Snap a single touch-grabbed object to its IGrabAlignable pose (relative to the holder), if it
    // declares one. Mirrors the laser-grab path leaving the grab offset alone when there's no alignment.
    private void TryAlignGrabbed(IGrabbable grabbed)
    {
        if (_grabber == null || grabbed is not Component component)
        {
            return;
        }

        var slot = component.Slot;
        if (slot == null || slot.IsRemoved)
        {
            return;
        }

        foreach (var alignable in slot.GetComponentsImplementing<IGrabAlignable>())
        {
            if (alignable.GetGrabAlignmentPose(_grabber, out var pos, out var rot, out var scale))
            {
                slot.LocalPosition.Value = pos;
                slot.LocalRotation.Value = rot;
                slot.LocalScale.Value = scale;
                return;
            }
        }
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

        // The pose write waits for the late pass. This tool updates at -1000, but on desktop the head pitch,
        // the body yaw and the very hand slot the holder hangs off are all written at update order 0 - so
        // writing here aims the object down LAST frame's view, and then those ancestors rotate underneath it
        // before anything renders, dragging it around the feet and the hand instead of the eye. Next frame
        // recomputes from the eye and yanks it back. That push-pull is the stepping, and it flips sign
        // between looking up and looking down because the hand's aim pitch clamps on the way down.
        //
        // A world whose late pass is throttled off (a background world) never reaches OnLateUpdate, so fall
        // back to writing inline the moment the late write goes missing. -xlinka
        long frame = Engine.Current?.FrameCount ?? -1;
        _holdPoseFrame = frame;
        if (frame < 0 || _holdPoseWrittenFrame < frame - 2)
        {
            WriteHolderPose(laser, holder, delta);
        }
    }

    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);

        long frame = Engine.Current?.FrameCount ?? -1;
        if (frame < 0 || _holdPoseFrame != frame || _laser == null || _grabber == null)
        {
            return;
        }

        var holder = _grabber.HolderSlot;
        if (holder == null || holder.IsRemoved)
        {
            return;
        }

        WriteHolderPose(_laser, holder, delta);
        _holdPoseWrittenFrame = frame;
    }

    // Aim, then place. RefreshHeldAim re-resolves the laser ray off the head/root as they stand NOW and takes
    // the smoothing step, so the beam and the object it carries come off the same ray. -xlinka
    private void WriteHolderPose(InteractionLaser laser, Slot holder, float delta)
    {
        laser.RefreshHeldAim(delta);
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

        var hand = input.Actions?.Interaction(Side.Value);
        if (hand == null)
        {
            SetDesktopInputSuppression(false);
            return;
        }

        if (input.IsVRActive)
        {
            SetDesktopInputSuppression(false);
            ApplyVrHoldInputs(hand, delta, holder);
            return;
        }

        bool freezeCursor = hand.HoldFreeze.Held;
        SetDesktopInputSuppression(freezeCursor);

        float scroll = hand.HoldScroll.Value;
        if (scroll != 0f)
        {
            if (hand.HoldModifier.Held && CanScaleHeldObjects())
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

        float2 mouseDelta = hand.HoldLook.Value;
        if (mouseDelta == float2.Zero)
        {
            return;
        }

        if (hand.HoldModifier.Held)
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

    private void ApplyVrHoldInputs(Input.Actions.InteractionActions hand, float delta, Slot holder)
    {
        // The axis action applies its own PER-AXIS deadzone, so slide and twist stay independent: a
        // hard pull toward you must not smear rotation onto the object as well.
        var axis = hand.HoldAxis.Value;
        float slide = axis.y;
        float rotate = axis.x;

        if (hand.HoldModifier.Held && slide != 0f && CanScaleHeldObjects())
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

        holder.GlobalRotation = _holderRotationOffset * FacingRotation(forward, rootUp);
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

        // Point the readable front (+Z) at the head. With the corrected facing this takes the toward-head
        // direction directly; the old code negated it to compensate for LookRotation's inverse. -xlinka
        slot.GlobalRotation = FacingRotation(faceDirection, upDirection.Normalized);
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
        SetScrollWheelCapture(false);
        _laser?.ArmRaySmoothing(false);
        _holdPoseFrame = long.MinValue;
        _holdPoseWrittenFrame = long.MinValue;
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

    // floatQ.LookRotation builds its basis from matrix ROWS, so it returns the INVERSE of the intended
    // facing (see FaceLocalUser). Inverting it yields a usable facing whose local +Z points along
    // 'forward'. Held-object orientation, twist (axis offset), and align all depend on this being correct -
    // the raw LookRotation made objects face/rotate the wrong way. -xlinka
    private static floatQ FacingRotation(float3 forward, float3 up) => floatQ.LookRotation(forward, up).Inverse;

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
        return FacingRotation(forward.Normalized, float3.Up);
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

    private void SetScrollWheelCapture(bool active)
    {
        if (_scrollWheelCaptured == active)
        {
            return;
        }

        _scrollWheelCaptured = active;
        UserInputState.ForFocusedLocalUser?.SetScrollWheelCaptured(this, active);
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

        return input.Actions?.Interaction(laser.ControllerSide.Value).Primary.Held == true;
    }

    private static bool ReadGripPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            return false;
        }

        return input.Actions?.Interaction(laser.ControllerSide.Value).Grab.Held == true;
    }

    private static bool ReadSecondaryPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null)
        {
            return false;
        }

        return input.Actions?.Interaction(laser.ControllerSide.Value).Secondary.Held == true;
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
