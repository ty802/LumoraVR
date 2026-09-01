// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// The composition root for one user's nameplate: background panel, name, badge row, status dot.
//
// LOCAL, TOP TO BOTTOM. This component and every slot, mesh, material and renderer under it is minted in
// the local RefID byte, so a nameplate adds no sync member, no delta, and nothing to a save. Every peer
// builds its own plate for every user out of state that was already on the wire - the name, the
// permission config, the head stream, User.IsPresent - which is also why the plate can face YOU while
// facing everyone else at the same time. There is no such thing as "the" nameplate; there is one per
// viewer per user, and none of them talk.
//
// DATA-DRIVEN, NOT POLLED. The name, the badge set and the presence state each have an owner that says
// when they changed: field change events for the name, the permission config's change events for roles,
// a 4Hz motion sample for presence. The per-frame work is a handful of float compares. The one thing on
// a timer is the role reconcile, and only because a host can move somebody's role through a path that
// writes the gate's session override without touching the replicated config. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class NameplateManager : UserRootComponent
{
    public const string RootSlotName = "Nameplate";

    private const string PlateSlotName = "Plate";
    private const string PanelSlotName = "Panel";
    private const string RimSlotName = "Rim";
    private const string LabelSlotName = "Label";
    private const string BadgeRowSlotName = "Badges";
    private const string StatusSlotName = "Status";
    private const string GroupCardSlotName = "GroupCard";
    private const string BadgeSlotPrefix = "badge_";

    // Reconcile cadence for the role read. Not a frame cost: a couple of dictionary lookups twice a
    // second, and it only touches geometry when the answer actually moved.
    private const float ReconcileInterval = 0.5f;

    private const float PanelPadX = 0.05f;
    private const float PanelPadY = 0.035f;
    private const float BadgeSize = 0.05f;
    private const float BadgeGap = 0.008f;
    private const float RowGap = 0.018f;
    private const float StatusSize = 0.032f;
    private const float StatusGap = 0.016f;
    private const float PanelDepth = -0.004f;
    private const float ChipDepth = -0.002f;

    // Metres of real corner geometry, not a texture radius. Deliberately small: a plate is read at a
    // glance from across a room and heavy rounding turns a name panel into a pill. The rim sits at the
    // SAME depth as the panel and grows outward from its outline, so the two share an edge and there
    // is no second alpha layer stacked behind the panel to darken it.
    private const float PanelCornerRadius = 0.02f;
    private const float ChipCornerRadius = 0.012f;
    private const float RimWidth = 0.008f;

    // THE GROUP CARD
    //
    // A popout that sits directly on the plate's top edge rather than floating in the badge row. Top
    // corners take the panel's radius and the bottom two the chip's, which is what makes it read as part
    // of the plate: a card rounded the same all the way round looks like a separate thing that happens to
    // be nearby. The gap is one hairline of daylight, not a margin. -xlinka
    private const float GroupCardGap = 0.004f;
    private const float GroupCardHeight = BadgeSize * 1.3f;
    private const float GroupIconSize = BadgeSize;
    private const float GroupDotSize = BadgeSize * 0.3f;
    private const float GroupContentGap = 0.01f;
    private const float GroupTagScale = 0.8f;
    // How much of the group colour lands over the plate's own panel colour.
    private const float GroupFillStrength = 0.85f;

    public readonly Sync<float> HeightOffset = new();
    public readonly Sync<float> TextSize = new();

    // Metres past which the plate stops drawing, with a fade band in front of the cut so it dissolves
    // rather than pops. Zero draws at any distance.
    public readonly Sync<float> MaxViewDistance = new();

    // Seconds of no head, hand or root motion before the dot goes amber.
    public readonly Sync<float> IdleSeconds = new();

    public readonly Sync<bool> ShowBadges = new();
    public readonly Sync<bool> ShowStatus = new();

    // The group popout. Same gate style as ShowBadges: a world that does not want group identity over
    // people's heads turns it off and the plate lays out exactly as it did before the card existed.
    public readonly Sync<bool> ShowGroupCard = new();

    // Your own plate hangs in front of your own face in first person. Third-person and free-cam still
    // show it, because there the point of view is not behind your eyes.
    public readonly Sync<bool> HideInFirstPerson = new();

    // Colour is NOT a knob here. The name, its outline and the panel behind it come from the equip
    // manager's replicated Badge* fields, so a world or an avatar authors one answer and every viewer
    // draws the same plate. The constants below are only what a plate falls back to when there is no
    // equip manager to ask, which in practice means a template that spawned a user root by hand. -xlinka
    private static readonly color FallbackPanel = new color(0.04f, 0.05f, 0.07f, 0.62f);
    private static readonly color FallbackRim = new color(0.14f, 0.15f, 0.19f, 0.85f);
    private static readonly color FallbackText = new color(0.95f, 0.97f, 1f, 1f);
    private static readonly color FallbackOutline = new color(0f, 0f, 0f, 0.9f);

    private Slot? _plate;
    private Slot? _badgeRow;
    private TextRenderer? _label;
    private Meshes.RoundedQuadMesh? _panelMesh;
    private Meshes.RoundedQuadRingMesh? _rimMesh;
    private Meshes.RoundedQuadMesh? _statusMesh;

    private Slot? _groupCard;
    private Meshes.RoundedQuadMesh? _groupCardMesh;
    private Slot? _groupIconSlot;
    private Meshes.RoundedQuadMesh? _groupIconMesh;
    private Lumora.Core.Assets.UnlitMaterial? _groupIconMaterial;
    private Lumora.Core.Assets.ImageProvider? _groupIconProvider;
    private TextRenderer? _groupTag;
    private Slot? _groupDotSlot;
    private Meshes.RoundedQuadMesh? _groupDotMesh;

    private readonly NameplateActivityWatcher _activity = new();
    private readonly List<NameplateBadge> _wanted = new();
    private readonly List<NameplateBadge> _drawn = new();

    private User? _user;
    private AvatarEquipManager? _equip;
    private WorldPermissionConfig? _watchedConfig;
    private Action<IChangeable>? _userChanged;
    private Action<IChangeable>? _configChanged;
    private Action<IChangeable>? _equipChanged;
    private Action<IChangeable>? _groupChanged;

    private bool _groupDirty = true;
    private bool _cardActive;
    private bool _cardIconShown;
    private bool _cardTagShown;
    private bool _cardDotShown;
    private string _cardIconUrl = string.Empty;
    private string _hostGroupId = string.Empty;
    private float2 _cardLaidOutFor = new float2(-1f, -1f);

    private float _reconcileCountdown;
    private bool _diagnosed;
    private float _diagnoseCountdown = 4f;
    private bool _hasCustomBadgeDriver;
    private bool _avatarShowing;
    private bool _badgesDirty = true;
    private bool _labelDirty = true;
    private float2 _laidOutFor = new float2(-1f, -1f);
    private bool _plateVisible = true;

    public NameplatePresence Presence => _activity.State;

    public IReadOnlyList<NameplateBadge> Badges => _drawn;

    public Slot? PlateSlot => _plate;

    public TextRenderer? Label => _label;

    public override void OnInit()
    {
        base.OnInit();
        HeightOffset.Value = 0.35f;
        TextSize.Value = 0.07f;
        MaxViewDistance.Value = 40f;
        IdleSeconds.Value = 60f;
        ShowBadges.Value = true;
        ShowGroupCard.Value = true;
        // Off by default: the presence dot is real data but it reads as clutter floating beside the
        // panel, and the owner vetoed it. The machinery stays for worlds that want it. -xlinka
        ShowStatus.Value = false;
        // Off at the owner's request so your own plate is visible for look testing. Flip back to true
        // once the plate visuals are signed off - seeing your own name glued to your face is not the
        // shipping experience. -xlinka
        HideInFirstPerson.Value = false;
    }

    public override void OnStart()
    {
        base.OnStart();

        HeightOffset.OnChanged += _ => MarkLayoutDirty();
        TextSize.OnChanged += _ => _labelDirty = true;
        MaxViewDistance.OnChanged += _ => ApplyViewDistance();
        IdleSeconds.OnChanged += _ => _activity.IdleThreshold = MathF.Max(1f, IdleSeconds.Value);
        ShowBadges.OnChanged += _ => _badgesDirty = true;
        ShowStatus.OnChanged += _ => MarkLayoutDirty();
        ShowGroupCard.OnChanged += _ => _groupDirty = true;

        _activity.IdleThreshold = MathF.Max(1f, IdleSeconds.Value);
        Bind();
        Compose();
    }

    // BINDING
    // Subscribe to the things that actually say when the plate is stale. Nothing here reads a value; the
    // handlers only raise a flag and the next update does the work, so a burst of changes in one frame
    // costs one rebuild rather than one each.

    private void Bind()
    {
        var root = Slot?.ActiveUserRoot;
        _user = root?.ActiveUser;
        _equip = root?.GetRegisteredComponent<AvatarEquipManager>()
                 ?? Slot?.GetComponentInParent<AvatarEquipManager>();

        if (_user != null && !_user.IsDestroyed)
        {
            _userChanged = _ => { _labelDirty = true; _badgesDirty = true; };
            _user.UserName.Changed += _userChanged;
            _user.IsPresent.Changed += _userChanged;

            // The card is rebuilt off a change event, never polled: six string compares per frame per
            // visible user is the sort of thing that only shows up when a room fills. -xlinka
            _groupChanged = _ => _groupDirty = true;
            _user.GroupId.Changed += _groupChanged;
            _user.GroupTag.Changed += _groupChanged;
            _user.GroupName.Changed += _groupChanged;
            _user.GroupIconHash.Changed += _groupChanged;
            _user.GroupRole.Changed += _groupChanged;
            _user.GroupColor.Changed += _groupChanged;
        }

        if (_equip != null && !_equip.IsDestroyed)
        {
            _equipChanged = _ => { _labelDirty = true; ApplyPanelColor(); ApplyRimColor(); };
            _equip.BadgeText.Changed += _equipChanged;
            _equip.BadgeColor.Changed += _equipChanged;
            _equip.BadgeOutline.Changed += _equipChanged;
            _equip.BadgeBackground.Changed += _equipChanged;
            _equip.BadgeRim.Changed += _equipChanged;
        }

        WatchPermissionConfig();

        // A late bind (the user root resolved after this component started) has to repaint what it just
        // learned the authored colours are.
        _labelDirty = true;
        _badgesDirty = true;
        _groupDirty = true;
        ApplyPanelColor();
        ApplyRimColor();
    }

    // Read the config through RootSlot rather than World.PermissionConfig: that property ATTACHES the
    // component on the authority, and a nameplate must not author world state to draw itself.
    private void WatchPermissionConfig()
    {
        var config = World?.RootSlot?.GetComponent<WorldPermissionConfig>();
        if (config == null || config.IsDestroyed || ReferenceEquals(config, _watchedConfig))
            return;

        UnwatchPermissionConfig();
        _watchedConfig = config;
        _configChanged = _ => _badgesDirty = true;
        for (int i = 0; i < config.SyncMemberCount; i++)
        {
            if (config.GetSyncMember(i) is IChangeable changeable)
                changeable.Changed += _configChanged;
        }
    }

    private void UnwatchPermissionConfig()
    {
        if (_watchedConfig == null || _configChanged == null)
        {
            _watchedConfig = null;
            return;
        }

        if (!_watchedConfig.IsDestroyed)
        {
            for (int i = 0; i < _watchedConfig.SyncMemberCount; i++)
            {
                if (_watchedConfig.GetSyncMember(i) is IChangeable changeable)
                    changeable.Changed -= _configChanged;
            }
        }
        _watchedConfig = null;
        _configChanged = null;
    }

    // COMPOSITION

    private void Compose()
    {
        if (Slot == null || World == null || _plate != null)
            return;

        var theme = NameplateTheme.For(World);
        if (theme == null)
            return;

        var existing = Slot.FindChild(PlateSlotName, recursive: false);
        existing?.Destroy();

        var plate = Slot.AddLocalSlot(PlateSlotName);
        plate.Persistent.Value = false;
        plate.AttachComponent<PositionAtUser>().VerticalOffset.Value = HeightOffset.Value;
        plate.AttachComponent<FaceLocalUser>();
        // None of this is anybody's to edit, and this component rewrites all of it every time the name
        // reshapes - which is exactly why the marker is a TOOLING one. It takes the plate out of the
        // inspector and off the tools without touching a single write below it. Marked on the plate
        // root and on the manager root, so neither half shows up as a stray branch.
        Slot.GetOrAttachComponent<ImmutableComponent>();
        plate.GetOrAttachComponent<ImmutableComponent>();
        _plate = plate;

        var rim = plate.AddSlot(RimSlotName);
        rim.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        _rimMesh = rim.AttachComponent<Meshes.RoundedQuadRingMesh>();
        _rimMesh.Size.Value = new float2(0.4f, 0.12f);
        _rimMesh.CornerRadius.Value = PanelCornerRadius;
        _rimMesh.RimWidth.Value = RimWidth;
        AttachRenderer(rim, _rimMesh, theme.PlateMaterial);
        ApplyRimColor();

        var panel = plate.AddSlot(PanelSlotName);
        panel.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        _panelMesh = panel.AttachComponent<Meshes.RoundedQuadMesh>();
        _panelMesh.Size.Value = new float2(0.4f, 0.12f);
        _panelMesh.CornerRadius.Value = PanelCornerRadius;
        AttachRenderer(panel, _panelMesh, theme.PlateMaterial);
        ApplyPanelColor();

        var label = plate.AddSlot(LabelSlotName);
        _label = label.AttachComponent<TextRenderer>();
        if (theme.Font != null)
            _label.Font.Target = theme.Font;
        _label.Size.Value = TextSize.Value;
        _label.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _label.VerticalAlign.Value = TextVerticalAlignment.Middle;
        // A readable ring in atlas texels. Without it a light name over a light wall is unreadable the
        // moment the panel fades out at range. Keep it well under the MSDF field's representable range:
        // the shader caps the shift at ~1.4 texels of a pixel-range-4 atlas, and past that the ring
        // swallows the glyph quad whole. -xlinka
        _label.OutlineThickness.Value = 1.2f;

        _badgeRow = plate.AddSlot(BadgeRowSlotName);
        ComposeGroupCard(plate, theme);

        var status = plate.AddSlot(StatusSlotName);
        _statusMesh = status.AttachComponent<Meshes.RoundedQuadMesh>();
        _statusMesh.Size.Value = new float2(StatusSize, StatusSize);
        // Radius at half the side IS a disc, so the dot is geometry now instead of a fully-rounded
        // texture stretched over a quad.
        _statusMesh.CornerRadius.Value = StatusSize * 0.5f;
        AttachRenderer(status, _statusMesh, theme.PlateMaterial);
        ApplyStatusColor();

        _labelDirty = true;
        _groupDirty = true;
        _laidOutFor = new float2(-1f, -1f);
        _cardLaidOutFor = new float2(-1f, -1f);
        ApplyViewDistance();
        Reconcile();
    }

    // The card's pieces are built ONCE and shown or hidden per user. Building them on demand would mean
    // minting slots and a material inside a live plate every time somebody changed the group they wear,
    // and the icon's asset provider is exactly the thing that must survive a hash change rather than be
    // replaced by it. -xlinka
    private void ComposeGroupCard(Slot plate, NameplateTheme? theme)
    {
        var card = plate.AddSlot(GroupCardSlotName);
        card.LocalPosition.Value = new float3(0f, 0f, PanelDepth);
        _groupCardMesh = card.AttachComponent<Meshes.RoundedQuadMesh>();
        _groupCardMesh.Size.Value = new float2(GroupCardHeight, GroupCardHeight);
        _groupCardMesh.CornerRadius.Value = PanelCornerRadius;
        _groupCardMesh.BottomCornerRadius.Value = ChipCornerRadius;
        AttachRenderer(card, _groupCardMesh, theme?.PlateMaterial);
        card.ActiveSelf.Value = false;
        _groupCard = card;

        var icon = card.AddSlot("Icon");
        icon.LocalPosition.Value = new float3(0f, 0f, ChipDepth);
        _groupIconMesh = icon.AttachComponent<Meshes.RoundedQuadMesh>();
        _groupIconMesh.Size.Value = new float2(GroupIconSize, GroupIconSize);
        _groupIconMesh.CornerRadius.Value = ChipCornerRadius;
        _groupIconMesh.UseVertexColors.Value = false;
        // Its own material, not the shared plate one: the plate material carries no texture on purpose and
        // every plate in the world shares it, so pointing that at one user's icon would put that icon on
        // everybody. Culling and blend match the plate so the card reads the same from behind.
        var material = icon.AttachComponent<Lumora.Core.Assets.UnlitMaterial>();
        material.TintColor.Value = colorHDR.White;
        material.UseVertexColor.Value = false;
        material.BlendMode.Value = Lumora.Core.Assets.BlendMode.Alpha;
        material.Culling.Value = Lumora.Core.Assets.Culling.None;
        _groupIconMaterial = material;
        AttachRenderer(icon, _groupIconMesh, material);
        icon.ActiveSelf.Value = false;
        _groupIconSlot = icon;

        var tagSlot = card.AddSlot("Tag");
        _groupTag = tagSlot.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            _groupTag.Font.Target = theme.Font;
        _groupTag.Size.Value = TextSize.Value * GroupTagScale;
        _groupTag.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        _groupTag.VerticalAlign.Value = TextVerticalAlignment.Middle;
        tagSlot.ActiveSelf.Value = false;

        var dot = card.AddSlot("Dot");
        dot.LocalPosition.Value = new float3(0f, 0f, ChipDepth);
        _groupDotMesh = dot.AttachComponent<Meshes.RoundedQuadMesh>();
        _groupDotMesh.Size.Value = new float2(GroupDotSize, GroupDotSize);
        _groupDotMesh.CornerRadius.Value = GroupDotSize * 0.5f;
        AttachRenderer(dot, _groupDotMesh, theme?.PlateMaterial);
        dot.ActiveSelf.Value = false;
        _groupDotSlot = dot;
    }

    private static MeshRenderer AttachRenderer(Slot slot, Meshes.ProceduralMesh mesh, Lumora.Core.Assets.UnlitMaterial? material)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        if (material != null)
            renderer.Material.Target = material;
        // A floating label has no business darkening the floor under it, and shadow passes are what
        // nameplates cost when a room fills up.
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return renderer;
    }

    // UPDATE

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (Slot == null || World == null)
            return;

        if (_plate == null || _plate.IsDestroyed)
        {
            _plate = null;
            Compose();
            if (_plate == null)
                return;
        }

        _reconcileCountdown -= delta;
        if (_reconcileCountdown <= 0f)
        {
            _reconcileCountdown = ReconcileInterval;

            if (_user == null || _user.IsDestroyed)
            {
                Unbind();
                Bind();
            }
            else if (_watchedConfig == null || _watchedConfig.IsDestroyed)
            {
                // The config arrives long after a guest joined - it is created on demand on the authority
                // and reaches a guest by state sync. Pick it up when it lands rather than sitting on a
                // dead watch for the rest of the session.
                WatchPermissionConfig();
            }

            Reconcile();
        }

        if (!_diagnosed && _label != null && (_diagnoseCountdown -= delta) <= 0f)
        {
            _diagnosed = true;
            if (string.IsNullOrEmpty(_label.Text.Value))
                ApplyLabel();
            var fontSet = _label.Font.Target?.Asset;
            int renderers = 0, materials = 0;
            string materialKinds = "";
            if (_label.Slot != null)
            {
                foreach (var mr in _label.Slot.GetComponentsInChildren<MeshRenderer>())
                {
                    renderers++;
                    materials += mr.Materials.Count;
                    for (int i = 0; i < mr.Materials.Count; i++)
                        materialKinds += (mr.Materials[i]?.GetType().Name ?? "null") + ",";
                }
            }
            Logging.Logger.Log($"NameplateManager diag: text='{_label.Text.Value}' fontTarget={_label.Font.Target != null} " +
                $"fontAsset={fontSet != null} fontValid={fontSet?.IsValid ?? false} rendered={_label.RenderedSize} " +
                $"labelActive={_label.Slot?.IsActive} plateVisible={_plateVisible} avatarShowing={_avatarShowing} " +
                $"equip={_equip != null} color={_label.Color.Value} renderers={renderers} materials={materials} kinds={materialKinds}");
        }

        UpdateVisibility();
        if (!_plateVisible)
            return;

        if (_activity.Tick(Slot.ActiveUserRoot, _user, delta))
            ApplyStatusColor();

        if (_labelDirty)
        {
            _labelDirty = false;
            ApplyLabel();
        }

        if (_badgesDirty)
        {
            _badgesDirty = false;
            RebuildBadges();
        }

        if (_groupDirty)
        {
            _groupDirty = false;
            RebuildGroupCard();
        }

        Layout();
    }

    // The twice-a-second pass. Everything here costs a tree walk or a registry scan, which is exactly why
    // none of it runs per frame: with twenty people in a room a per-frame component search per plate is
    // the whole nameplate system showing up in a profile for answers that change on the scale of somebody
    // swapping an avatar. -xlinka
    private void Reconcile()
    {
        _hasCustomBadgeDriver = HasCustomBadgeDriver();

        var root = Slot?.ActiveUserRoot;
        var head = root == null || root.IsDestroyed ? null : root.HeadSlot;
        // A hidden avatar hides its plate. The plate hangs under the avatar container, so slot activation
        // already covers the container itself being switched off; this catches the head node going away or
        // being deactivated on its own.
        _avatarShowing = root != null && !root.IsDestroyed
            && head != null && !head.IsDestroyed && head.IsActive;

        // A host can move a role through a path that writes only the gate's session override, which fires
        // no config change on any peer. Re-reading the resolved role here is what makes the plate follow
        // the Session screen as well as the Permissions screen.
        _badgesDirty = true;

        // The world's own group id has no change event this component watches (WorldSettings arrives by
        // state sync like the permission config does), so it is read here and the card only rebuilt when
        // the answer actually moved. Read through RootSlot: World.Configuration ATTACHES the settings
        // component on the authority and a nameplate must not author world state to draw itself. -xlinka
        string hostGroup = World?.RootSlot?.GetComponent<WorldSettings>()?.HostGroupId.Value ?? string.Empty;
        if (!string.Equals(hostGroup, _hostGroupId, StringComparison.Ordinal))
        {
            _hostGroupId = hostGroup;
            _groupDirty = true;
        }
    }

    // The plate is not drawn when: the badge is switched off, the equipped avatar carries its own name
    // badge driver, the avatar itself is hidden, or this is your own plate seen through your own eyes.
    private void UpdateVisibility()
    {
        bool show = _avatarShowing && !_hasCustomBadgeDriver;

        if (show && _equip != null && !_equip.IsDestroyed && !_equip.AutoAddNameBadge.Value)
            show = false;

        var root = Slot?.ActiveUserRoot;
        if (show && HideInFirstPerson.Value && root != null && root.IsLocalUserRoot
            && !UserInputState.FocusedExternalCameraActive)
            show = false;

        if (show == _plateVisible)
            return;
        _plateVisible = show;
        if (_plate != null && !_plate.IsDestroyed)
            _plate.ActiveSelf.Value = show;
    }

    private bool HasCustomBadgeDriver()
    {
        var host = _equip?.Slot;
        if (host == null || host.IsDestroyed)
            return false;
        foreach (var driver in host.GetComponentsInChildren<NameBadgeDriver>())
        {
            if (driver != null && !driver.IsDestroyed)
                return true;
        }
        return false;
    }

    private void ApplyLabel()
    {
        if (_label == null || _label.IsDestroyed)
            return;

        string name = _equip?.BadgeText.Value ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            name = _user?.UserName.Value ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            name = "User";

        _label.Text.Value = name;
        _label.Size.Value = TextSize.Value;

        bool authored = _equip != null && !_equip.IsDestroyed;
        _label.Color.Value = authored ? _equip!.BadgeColor.Value : FallbackText;

        var outline = authored ? _equip!.BadgeOutline.Value : FallbackOutline;
        _label.OutlineColor.Value = new colorHDR(outline.r, outline.g, outline.b, outline.a);

        MarkLayoutDirty();
    }

    private void ApplyPanelColor()
    {
        if (_panelMesh == null || _panelMesh.IsDestroyed)
            return;
        _panelMesh.Color = _equip != null && !_equip.IsDestroyed ? _equip.BadgeBackground.Value : FallbackPanel;
    }

    // The rim is the supporter-reward surface: one authored colour living on the EQUIP manager, which
    // replicates, painted into a mesh that does not. The plate itself stays local top to bottom.
    private void ApplyRimColor()
    {
        if (_rimMesh == null || _rimMesh.IsDestroyed)
            return;
        _rimMesh.Color = _equip != null && !_equip.IsDestroyed ? _equip.BadgeRim.Value : FallbackRim;
    }

    private void ApplyStatusColor()
    {
        if (_statusMesh == null || _statusMesh.IsDestroyed)
            return;
        _statusMesh.Color = NameplatePresenceColors.For(_activity.State);
    }

    private void ApplyViewDistance()
    {
        if (_label != null && !_label.IsDestroyed)
            _label.MaxViewDistance.Value = MathF.Max(0f, MaxViewDistance.Value);

        if (_plate == null || _plate.IsDestroyed)
            return;

        float distance = MathF.Max(0f, MaxViewDistance.Value);
        float margin = distance > 0f ? TextRenderer.ViewDistanceFadeMargin : 0f;
        foreach (var renderer in _plate.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer == null || renderer.IsDestroyed)
                continue;
            if (renderer.MaxViewDistance == distance && renderer.ViewDistanceFadeMargin == margin)
                continue;
            renderer.MaxViewDistance = distance;
            renderer.ViewDistanceFadeMargin = margin;
            renderer.MarkChangeDirty();
        }
    }

    // BADGES

    private void RebuildBadges()
    {
        if (_badgeRow == null || _badgeRow.IsDestroyed)
            return;

        _wanted.Clear();
        if (ShowBadges.Value && _user != null && !_user.IsDestroyed)
        {
            var context = new NameplateBadgeContext(World!, _user, Slot?.ActiveUserRoot!);
            NameplateBadgeRegistry.Collect(in context, _wanted);
        }

        if (SameBadges(_wanted, _drawn))
            return;

        _badgeRow.DestroyChildren();

        var theme = NameplateTheme.For(World);
        for (int i = 0; i < _wanted.Count; i++)
            BuildBadge(_badgeRow, _wanted[i], theme);

        _drawn.Clear();
        _drawn.AddRange(_wanted);
        SpreadBadgeRow(_panelMesh != null && !_panelMesh.IsDestroyed ? _panelMesh.Size.Value.y : 0.12f);
        MarkLayoutDirty();
        ApplyViewDistance();
    }

    private void BuildBadge(Slot row, in NameplateBadge badge, NameplateTheme? theme)
    {
        var slot = row.AddSlot(BadgeSlotPrefix + badge.Key);

        var chip = slot.AddSlot("Chip");
        chip.LocalPosition.Value = new float3(0f, 0f, ChipDepth);
        var chipMesh = chip.AttachComponent<Meshes.RoundedQuadMesh>();
        chipMesh.Size.Value = new float2(BadgeSize, BadgeSize);
        chipMesh.CornerRadius.Value = ChipCornerRadius;
        chipMesh.Color = badge.Chip;
        AttachRenderer(chip, chipMesh, theme?.PlateMaterial);

        if (string.IsNullOrEmpty(badge.Glyph))
            return;

        var glyphSlot = slot.AddSlot("Glyph");
        var glyph = glyphSlot.AttachComponent<TextRenderer>();
        if (theme?.Font != null)
            glyph.Font.Target = theme.Font;
        glyph.Text.Value = badge.Glyph;
        glyph.Size.Value = BadgeSize * 0.72f;
        glyph.Color.Value = badge.Ink;
        glyph.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        glyph.VerticalAlign.Value = TextVerticalAlignment.Middle;
        glyph.MaxViewDistance.Value = MathF.Max(0f, MaxViewDistance.Value);
    }

    // THE GROUP CARD

    private void RebuildGroupCard()
    {
        var card = _groupCard;
        if (card == null || card.IsDestroyed)
            return;

        var user = _user;
        string tag = user == null || user.IsDestroyed ? string.Empty : user.GroupTag.Value ?? string.Empty;
        string iconHash = user == null || user.IsDestroyed ? string.Empty : user.GroupIconHash.Value ?? string.Empty;
        string groupId = user == null || user.IsDestroyed ? string.Empty : user.GroupId.Value ?? string.Empty;

        // Nothing to say, no card. A group id with neither a tag nor an icon would draw an empty coloured
        // stub over somebody's head, which is worse than no card at all.
        bool active = ShowGroupCard.Value
            && groupId.Length > 0
            && (tag.Length > 0 || iconHash.Length > 0);

        if (active != _cardActive)
        {
            _cardActive = active;
            card.ActiveSelf.Value = active;
            // The badge row and the status dot sit above whatever the card takes, so their placement is
            // stale the moment the card appears or goes.
            MarkLayoutDirty();
        }
        if (!active)
            return;

        var groupColor = user!.GroupColor.Value;
        var fill = CompositeGroupFill(groupColor);
        if (_groupCardMesh != null && !_groupCardMesh.IsDestroyed)
            _groupCardMesh.Color = fill;

        bool showIcon = iconHash.Length > 0;
        if (showIcon)
            PointIconAt(iconHash);
        if (_groupIconSlot != null && !_groupIconSlot.IsDestroyed && _groupIconSlot.ActiveSelf.Value != showIcon)
            _groupIconSlot.ActiveSelf.Value = showIcon;

        bool showTag = tag.Length > 0;
        if (_groupTag != null && !_groupTag.IsDestroyed)
        {
            if (showTag)
            {
                _groupTag.Text.Value = tag;
                _groupTag.Size.Value = TextSize.Value * GroupTagScale;
                _groupTag.Color.Value = InkFor(fill);
                _groupTag.MaxViewDistance.Value = MathF.Max(0f, MaxViewDistance.Value);
            }
            if (_groupTag.Slot != null && _groupTag.Slot.ActiveSelf.Value != showTag)
                _groupTag.Slot.ActiveSelf.Value = showTag;
        }

        // The quiet marker for a group's own staff, and only inside that group's own world. No word and no
        // glyph on purpose: the owner's line was "not blatant, not to be confused with official staff", and
        // a dot in the group's own colour is as far as that goes. -xlinka
        bool showDot = groupId.Length > 0
            && string.Equals(groupId, _hostGroupId, StringComparison.Ordinal)
            && IsGroupStaff(user.GroupRole.Value);
        if (showDot && _groupDotMesh != null && !_groupDotMesh.IsDestroyed)
            _groupDotMesh.Color = color.Lerp(groupColor.a > 0f ? groupColor : fill, color.White, 0.5f);
        if (_groupDotSlot != null && !_groupDotSlot.IsDestroyed && _groupDotSlot.ActiveSelf.Value != showDot)
            _groupDotSlot.ActiveSelf.Value = showDot;

        if (showIcon != _cardIconShown || showTag != _cardTagShown || showDot != _cardDotShown)
        {
            _cardIconShown = showIcon;
            _cardTagShown = showTag;
            _cardDotShown = showDot;
            _cardLaidOutFor = new float2(-1f, -1f);
        }
        ApplyViewDistance();
    }

    private static bool IsGroupStaff(string? role)
        => role is "Moderator" or "Admin" or "Owner";

    // The provider is built once and RETARGETED. Replacing it per hash change would leave a dead asset
    // component on the plate for every group the wearer has ever worn this session.
    private void PointIconAt(string hash)
    {
        var host = _groupCard;
        if (host == null || host.IsDestroyed)
            return;

        string url = Nexus.Cloud.Cdn.ServiceConfig.Current.GetContentUrl(hash);
        if (_groupIconProvider != null && !_groupIconProvider.IsDestroyed
            && string.Equals(url, _cardIconUrl, StringComparison.Ordinal))
        {
            return;
        }

        if (_groupIconProvider == null || _groupIconProvider.IsDestroyed)
        {
            // Its own hidden child so the provider is never something the card's layout has to step over.
            var slot = host.AddSlot("IconAsset");
            slot.ActiveSelf.Value = false;
            _groupIconProvider = slot.AttachComponent<Lumora.Core.Assets.ImageProvider>();
            _groupIconProvider.GenerateMipmaps.Value = false;
            if (_groupIconMaterial != null && !_groupIconMaterial.IsDestroyed)
                _groupIconMaterial.Texture.Target = _groupIconProvider;
        }

        _cardIconUrl = url;
        try
        {
            _groupIconProvider.URL.Value = new Uri(url);
        }
        catch (UriFormatException)
        {
            // A hash the service handed us that does not make a URL is a hash we draw nothing for.
            _cardIconUrl = string.Empty;
        }
    }

    // The group colour laid over the plate's own panel colour at GroupFillStrength, composited here rather
    // than stacked as a second translucent quad: two alpha layers over the same pixels come out darker
    // than one and the card would not match the plate it is sitting on. A clear group colour (nothing
    // parsed) falls straight back to the plate's own tint. -xlinka
    private color CompositeGroupFill(in color group)
    {
        var plate = _equip != null && !_equip.IsDestroyed ? _equip.BadgeBackground.Value : FallbackPanel;
        if (group.a <= 0f)
            return plate;

        float source = GroupFillStrength * group.a;
        float outAlpha = source + plate.a * (1f - source);
        if (outAlpha <= 0f)
            return plate;
        float backing = plate.a * (1f - source);
        return new color(
            (group.r * source + plate.r * backing) / outAlpha,
            (group.g * source + plate.g * backing) / outAlpha,
            (group.b * source + plate.b * backing) / outAlpha,
            outAlpha);
    }

    // White or the plate's own ink, whichever pulls further away from the fill. A group that picked a pale
    // colour gets dark text and one that picked a deep colour gets white, without anybody authoring it.
    private color InkFor(in color fill)
    {
        var plateInk = _equip != null && !_equip.IsDestroyed ? _equip.BadgeColor.Value : FallbackText;
        float fillLuma = Luminance(fill);
        return MathF.Abs(Luminance(color.White) - fillLuma) >= MathF.Abs(Luminance(plateInk) - fillLuma)
            ? color.White
            : plateInk;
    }

    private static float Luminance(in color value)
        => 0.2126f * value.r + 0.7152f * value.g + 0.0722f * value.b;

    // How much room the card takes out of the plate's top edge, badge row and status included.
    private float GroupCardOffset => _cardActive ? GroupCardHeight + RowGap : 0f;

    // Content runs left to right at a fixed height, so the only thing that has to wait on shaping is the
    // tag's own width. Same deal as the name: TextRenderer fills RenderedSize during meshing, so this runs
    // again for free once that number lands and never again after.
    private void LayoutGroupCard(float panelHeight)
    {
        var card = _groupCard;
        if (!_cardActive || card == null || card.IsDestroyed || _groupCardMesh == null || _groupCardMesh.IsDestroyed)
            return;

        float tagWidth = 0f;
        if (_cardTagShown && _groupTag != null && !_groupTag.IsDestroyed)
        {
            var shaped = _groupTag.RenderedSize;
            if (shaped.x <= 0f)
                return;   // still shaping; the next update lays it out
            tagWidth = shaped.x;
        }

        var signature = new float2(tagWidth, panelHeight);
        if ((signature - _cardLaidOutFor).LengthSquared < 1e-8f)
            return;
        _cardLaidOutFor = signature;

        float content = 0f;
        if (_cardIconShown)
            content += GroupIconSize;
        if (_cardTagShown)
            content += (content > 0f ? GroupContentGap : 0f) + tagWidth;
        if (_cardDotShown)
            content += (content > 0f ? GroupContentGap : 0f) + GroupDotSize;

        float width = content + PanelPadX;
        _groupCardMesh.Size.Value = new float2(width, GroupCardHeight);
        card.LocalPosition.Value = new float3(0f, panelHeight * 0.5f + GroupCardGap + GroupCardHeight * 0.5f, 0f);

        float cursor = -content * 0.5f;
        if (_cardIconShown && _groupIconSlot != null && !_groupIconSlot.IsDestroyed)
        {
            _groupIconSlot.LocalPosition.Value = new float3(cursor + GroupIconSize * 0.5f, 0f, ChipDepth);
            cursor += GroupIconSize + GroupContentGap;
        }
        if (_cardTagShown && _groupTag?.Slot != null && !_groupTag.Slot.IsDestroyed)
        {
            _groupTag.Slot.LocalPosition.Value = new float3(cursor + tagWidth * 0.5f, 0f, 0f);
            cursor += tagWidth + GroupContentGap;
        }
        if (_cardDotShown && _groupDotSlot != null && !_groupDotSlot.IsDestroyed)
            _groupDotSlot.LocalPosition.Value = new float3(cursor + GroupDotSize * 0.5f, 0f, ChipDepth);
    }

    private static bool SameBadges(List<NameplateBadge> a, List<NameplateBadge> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
                return false;
        }
        return true;
    }

    // LAYOUT
    // The panel and the badge row are sized off the SHAPED text, not off a guess: TextRenderer fills
    // RenderedSize during meshing, which lands a frame or two after the text is set and again whenever
    // the font atlas finishes loading. Re-laying out only when that number moves is what keeps this off
    // the per-frame bill.

    private void MarkLayoutDirty() => _laidOutFor = new float2(-1f, -1f);

    private void Layout()
    {
        if (_label == null || _label.IsDestroyed || _plate == null || _plate.IsDestroyed)
            return;

        var size = _label.RenderedSize;
        if (size.x <= 0f || size.y <= 0f)
            return;

        // The card runs on its own guard: its width waits on the TAG shaping, which lands on a different
        // frame from the name's, and the name not having moved is no reason to leave the card unplaced.
        LayoutGroupCard(size.y + PanelPadY);

        if ((size - _laidOutFor).LengthSquared < 1e-8f)
            return;
        _laidOutFor = size;

        var panelSize = new float2(size.x + PanelPadX, size.y + PanelPadY);
        if (_panelMesh != null && !_panelMesh.IsDestroyed)
            _panelMesh.Size.Value = panelSize;
        // Same size and same radius as the panel: the band INNER outline is the panel outline, so it
        // hugs the edge at every width the name grows to.
        if (_rimMesh != null && !_rimMesh.IsDestroyed)
            _rimMesh.Size.Value = panelSize;

        SpreadBadgeRow(size.y + PanelPadY);

        var status = _plate.FindChild(StatusSlotName, recursive: false);
        if (status != null && !status.IsDestroyed)
        {
            status.ActiveSelf.Value = ShowStatus.Value;
            float statusX = -((size.x + PanelPadX) * 0.5f + StatusGap + StatusSize * 0.5f);
            status.LocalPosition.Value = new float3(statusX, GroupCardOffset, 0f);
        }

        var positioner = _plate.GetComponent<PositionAtUser>();
        if (positioner != null && !positioner.IsDestroyed)
            positioner.VerticalOffset.Value = HeightOffset.Value;
    }

    // Chips are centred as a row, so the row has to be re-spread whenever its member count changes as
    // well as whenever the name resizes. Called from both, because a chip added before the font atlas has
    // finished loading would otherwise sit at the origin under every other chip until the name shapes.
    private void SpreadBadgeRow(float panelHeight)
    {
        if (_badgeRow == null || _badgeRow.IsDestroyed)
            return;

        // Badges ride above whatever the group card took, so a plate with a card gets its chips clear of it
        // and a plate without one lays out exactly as it did before the card existed.
        _badgeRow.LocalPosition.Value = new float3(0f,
            panelHeight * 0.5f + RowGap + BadgeSize * 0.5f + GroupCardOffset, 0f);

        var children = _badgeRow.Children;
        float span = children.Count > 0 ? children.Count * BadgeSize + (children.Count - 1) * BadgeGap : 0f;
        float x = -span * 0.5f + BadgeSize * 0.5f;
        for (int i = 0; i < children.Count; i++)
            children[i].LocalPosition.Value = new float3(x + i * (BadgeSize + BadgeGap), 0f, 0f);
    }

    // TEARDOWN

    private void Unbind()
    {
        if (_user != null && !_user.IsDestroyed)
        {
            if (_userChanged != null)
            {
                _user.UserName.Changed -= _userChanged;
                _user.IsPresent.Changed -= _userChanged;
            }
            if (_groupChanged != null)
            {
                _user.GroupId.Changed -= _groupChanged;
                _user.GroupTag.Changed -= _groupChanged;
                _user.GroupName.Changed -= _groupChanged;
                _user.GroupIconHash.Changed -= _groupChanged;
                _user.GroupRole.Changed -= _groupChanged;
                _user.GroupColor.Changed -= _groupChanged;
            }
        }
        _userChanged = null;
        _groupChanged = null;
        _user = null;

        if (_equip != null && !_equip.IsDestroyed && _equipChanged != null)
        {
            _equip.BadgeText.Changed -= _equipChanged;
            _equip.BadgeColor.Changed -= _equipChanged;
            _equip.BadgeOutline.Changed -= _equipChanged;
            _equip.BadgeBackground.Changed -= _equipChanged;
            _equip.BadgeRim.Changed -= _equipChanged;
        }
        _equipChanged = null;
        _equip = null;

        UnwatchPermissionConfig();
        _activity.Reset();
    }

    public override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }
}
