// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Media;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Localization;
using Lumora.Core.Math;
using Lumora.Core.Twitch;

namespace Lumora.Core.Components.Twitch;

// The chat column: channel name and connection state on top, a scrolling list of wrapped rows below.
// Everything under the slot is LOCAL. Each peer reads the channel itself, so a replicated row set
// would arrive twice on every machine and cost the session traffic for text it already has. The
// panel follows the newest row until the reader scrolls up, and picks the tail back up when they
// scroll down again. -xlinka
[ComponentCategory("Media/Twitch")]
public sealed class TwitchChatPanel : Component
{
    public readonly SyncRef<TwitchChat> Chat;
    public readonly Sync<int> MaxRows;
    public readonly Sync<float> TextSize;
    public readonly Sync<bool> ShowEvents;
    public readonly Sync<bool> ShowBadges;

    // Authored in pixels and scaled by whoever places the panel, the way the player's controls are.
    public const float PanelWidth = 520f;
    public const float PanelHeight = 900f;
    private const float HeaderHeight = 46f;
    private const float Padding = 12f;
    private const float RowSpacing = 6f;
    // The frame behind the panel, in the same pixel space, so it scales with the column.
    private const float FrameMargin = 30f;
    private const float FrameRadius = 30f;
    private const float FrameDepth = 6f;
    // Glyph width as a share of the font size, for the row height estimate. Generous on purpose:
    // a row that is a line too tall is padding, a row that is a line too short clips its text.
    private const float GlyphWidthRatio = 0.5f;
    private const float LineHeightRatio = 1.3f;
    private const float RowPadX = 12f;
    private const float RowPadY = 7f;

    private const string TagOwner = "#E91916";
    private const string TagMod = "#00AD03";
    private const string TagVip = "#E005B9";
    private const string TagSub = "#8B6DD9";
    private const string TagBits = "#9147FF";
    private const string TagEvent = "#F5B54A";
    private const string TagMuted = "#736F8F";

    private Slot? _root;
    private Text? _title;
    private Text? _status;
    private Slot? _rows;
    private RectTransform? _rowsRect;
    private RectTransform? _viewportRect;
    private ScrollRect? _scroll;
    private DashScrollbar? _bar;
    private FontProvider? _font;
    private FontProvider? _titleFont;
    private Slot? _spriteRoot;
    private SpriteSet? _spriteSet;
    private readonly HashSet<string> _spriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // Pictures still on their way: once one lands the rows re-shape for its true width, and an
    // animated one gets a material that plays its strip.
    private readonly Dictionary<string, (ImageProvider image, Sprite sprite)> _pendingSprites = new Dictionary<string, (ImageProvider, Sprite)>(StringComparer.OrdinalIgnoreCase);
    private float _spritePoll;
    // Enough for a busy channel's active set; past this, an emote shows as its name.
    private const int MaxSprites = 600;
    // Emotes stand taller than the text the way a web chat draws them; the line grows with them.
    private const string SpriteOpen = "<size=150%><sprite=";
    private const string SpriteClose = "></size>";
    private readonly List<Row> _rowList = new List<Row>();
    private readonly StringBuilder _builder = new StringBuilder(256);

    private TwitchChat? _bound;
    private bool _built;
    private bool _followTail = true;
    private bool _contentDirty;
    private bool _pinTail;
    private bool _applyingScroll;
    private float _lastPinned = -1f;

    private struct Row
    {
        public Slot Slot;
        public Text Text;
        public string? MessageId;
        public string? UserId;
        public float Height;
    }

    public TwitchChatPanel()
    {
        Chat = new SyncRef<TwitchChat>(this);
        MaxRows = new Sync<int>(this, 80);
        TextSize = new Sync<float>(this, 19f);
        ShowEvents = new Sync<bool>(this, true);
        ShowBadges = new Sync<bool>(this, true);
    }

    public override void OnStart()
    {
        base.OnStart();
        Build();
        Rebind();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Rebind();
    }

    public override void OnDestroy()
    {
        Unbind();
        base.OnDestroy();
    }

    // BUILD

    private void Build()
    {
        if (_built || Slot == null || Slot.IsDestroyed)
            return;
        _built = true;

        var frameSlot = Slot.AddLocalSlot("Frame");
        frameSlot.LocalPosition.Value = new float3(0f, 0f, -FrameDepth);
        var frameMesh = frameSlot.AttachComponent<RoundedQuadMesh>();
        frameMesh.Size.Value = new float2(PanelWidth + FrameMargin * 2f, PanelHeight + FrameMargin * 2f);
        frameMesh.CornerRadius.Value = FrameRadius;
        frameMesh.CornerSegments.Value = 4;
        frameMesh.DualSided.Value = false;
        var frameMaterial = DashGlass.Attach(frameSlot);
        var frameRenderer = frameSlot.AttachComponent<MeshRenderer>();
        frameRenderer.Mesh.Target = frameMesh;
        frameRenderer.Material.Target = frameMaterial;
        frameRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        _root = Slot.AddLocalSlot("ChatPanel");
        var rect = _root.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(PanelWidth * -0.5f, PanelHeight * -0.5f);
        rect.OffsetMax.Value = new float2(PanelWidth * 0.5f, PanelHeight * 0.5f);
        _root.AttachComponent<Canvas>();
        SettingsUI.Panel(_root, DashTheme.GlassBackdrop, DashTheme.OutlineStrong, DashTheme.RadiusPanel);

        var fontSlot = _root.AddSlot("Font");
        _font = fontSlot.AttachComponent<FontProvider>();
        _font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
        if (_font.FallbackURLs.Count == 0)
            _font.FallbackURLs.Add(DashTheme.FontRegular);
        _titleFont = fontSlot.AttachComponent<FontProvider>();
        _titleFont.URL.Value = DashTheme.FontSemibold;
        if (_titleFont.FallbackURLs.Count == 0)
            _titleFont.FallbackURLs.Add(DashTheme.FontRegular);

        var content = _root.AddSlot("Content");
        Fill(content.AttachComponent<RectTransform>());
        // Its own chunk, drawn above the root's backdrop: inside one chunk a rounded panel's mesh
        // lands after the images and would paint over them.
        content.AttachComponent<GraphicChunkRoot>();
        var column = content.AttachComponent<VerticalLayout>();
        column.Spacing.Value = 8f;
        column.PaddingLeft.Value = Padding;
        column.PaddingRight.Value = Padding;
        column.PaddingTop.Value = Padding;
        column.PaddingBottom.Value = Padding;
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;

        var header = content.AddSlot("Header");
        header.AttachComponent<RectTransform>();
        Metrics(header, 0f, 0f, 1f, HeaderHeight, HeaderHeight, 0f);
        SettingsUI.Panel(header, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
        var headerLayout = header.AttachComponent<HorizontalLayout>();
        headerLayout.Spacing.Value = 8f;
        headerLayout.PaddingLeft.Value = 14f;
        headerLayout.PaddingRight.Value = 8f;
        headerLayout.PaddingTop.Value = 7f;
        headerLayout.PaddingBottom.Value = 7f;
        headerLayout.ForceExpandWidth.Value = false;
        headerLayout.ForceExpandHeight.Value = true;

        var ui = new UIBuilder(header);
        ui.Font(_titleFont);
        ui.TextColor(DashTheme.Text);
        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        _title = ui.Text("Chat", 21f, DashTheme.Text);
        _title.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _title.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _title.WordWrap.Value = false;
        ui.PopStyle();

        // Connection state as a chip on the right, the way the dash marks a state.
        var chip = header.AddSlot("Status");
        chip.AttachComponent<RectTransform>();
        Metrics(chip, 128f, 128f, 0f, 0f, 0f, 1f);
        SettingsUI.Panel(chip, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusChip);
        var chipUi = new UIBuilder(chip);
        chipUi.Font(_font);
        _status = chipUi.Text(string.Empty, 13f, DashTheme.TextDim);
        Fill(_status.Slot.GetComponent<RectTransform>() ?? _status.Slot.AttachComponent<RectTransform>());
        _status.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        _status.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _status.WordWrap.Value = false;

        var closeUi = new UIBuilder(header);
        closeUi.Font(_titleFont);
        closeUi.PushStyle().MinWidth(32f).PreferredWidth(32f).FlexibleWidth(0f);
        var close = closeUi.Button("×", OnClosePressed, DashTheme.Field);
        closeUi.PopStyle();
        close.Slot.Name.Value = "Close";
        foreach (var driver in close.ColorDrivers)
        {
            if (driver == null || driver.IsDestroyed)
                continue;
            driver.HighlightColor.Value = DashTheme.Negative;
            driver.PressedColor.Value = DashTheme.SurfacePressed;
            driver.Apply();
        }

        var body = content.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        Metrics(body, 0f, 0f, 1f, 0f, 0f, 1f);
        var bodyLayout = body.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 6f;
        bodyLayout.ForceExpandWidth.Value = false;
        bodyLayout.ForceExpandHeight.Value = true;

        var viewport = body.AddSlot("Viewport");
        _viewportRect = viewport.AttachComponent<RectTransform>();
        Metrics(viewport, 0f, 0f, 1f, 0f, 0f, 1f);
        viewport.AttachComponent<Mask>().ShowMaskGraphic.Value = false;
        _scroll = viewport.AttachComponent<ScrollRect>();
        _scroll.ScrollChanged += OnScrolled;

        _rows = viewport.AddSlot("Rows");
        _rowsRect = _rows.AttachComponent<RectTransform>();
        // Top-anchored; the height is pinned from the rows so the scroll range is always right.
        _rowsRect.AnchorMin.Value = new float2(0f, 1f);
        _rowsRect.AnchorMax.Value = new float2(1f, 1f);
        _rowsRect.OffsetMin.Value = new float2(0f, -PanelHeight);
        _rowsRect.OffsetMax.Value = float2.Zero;
        var rowsLayout = _rows.AttachComponent<VerticalLayout>();
        rowsLayout.Spacing.Value = RowSpacing;
        rowsLayout.PaddingRight.Value = 2f;
        rowsLayout.ForceExpandWidth.Value = true;
        rowsLayout.ForceExpandHeight.Value = false;
        _scroll.Content.Target = _rowsRect;

        _bar = DashScrollbar.InColumn(body);
        _bar.Bind(_scroll, _viewportRect, _rowsRect, OnScrolled);

        // The pictures behind the rows' sprite tags: one child slot per emote or badge, made the first
        // time a message needs it and kept for the life of the panel.
        _spriteRoot = _root.AddSlot("Sprites");
        _spriteSet = _spriteRoot.AttachComponent<SpriteSet>();
    }

    // A video player's chat goes back behind the player's Chat button. A chat standing on its own (put in the
    // world from the camera, or popped out next to you) is removed. The canvas is local, so this runs on the
    // machine of whoever pressed it; a press on something they may not change does nothing.
    public void OnClosePressed(Button button, UIInteractionContext context)
    {
        if (Slot is not { IsDestroyed: false } slot)
            return;
        try
        {
            var part = slot.GetComponent<VideoPlayerPart>();
            if (part != null && part.Tracked)
                part.Player!.ClosePart(VideoPlayerPartKind.Chat);
            else
                slot.Destroy();
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // BINDING

    private void Rebind()
    {
        var chat = Chat.Target;
        if (chat != null && chat.IsDestroyed)
            chat = null;
        if (ReferenceEquals(chat, _bound))
        {
            UpdateHeader();
            return;
        }

        Unbind();
        _bound = chat;
        ClearRows();
        if (chat == null)
        {
            UpdateHeader();
            return;
        }

        chat.MessageReceived += OnMessage;
        chat.EventReceived += OnEvent;
        chat.MessageDeleted += OnDeleted;
        chat.ChatCleared += OnCleared;
        chat.StatusChanged += OnStatus;
        chat.HistoryReset += OnHistoryReset;

        // A panel that opens late still gets the recent past.
        var history = chat.History;
        int start = System.Math.Max(0, history.Count - System.Math.Max(1, MaxRows.Value));
        for (int i = start; i < history.Count; i++)
            AppendMessage(history[i]);
        UpdateHeader();
    }

    private void Unbind()
    {
        var chat = _bound;
        _bound = null;
        if (chat == null)
            return;
        chat.MessageReceived -= OnMessage;
        chat.EventReceived -= OnEvent;
        chat.MessageDeleted -= OnDeleted;
        chat.ChatCleared -= OnCleared;
        chat.StatusChanged -= OnStatus;
        chat.HistoryReset -= OnHistoryReset;
    }

    private void OnMessage(TwitchChat chat, TwitchChatMessage message) => AppendMessage(message);

    private void OnEvent(TwitchChat chat, TwitchChatEvent chatEvent)
    {
        if (ShowEvents.Value)
            AppendEvent(chatEvent);
    }

    private void OnDeleted(TwitchChat chat, string messageId)
    {
        for (int i = _rowList.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_rowList[i].MessageId, messageId, StringComparison.Ordinal))
            {
                RemoveRow(i);
                break;
            }
        }
    }

    private void OnCleared(TwitchChat chat, TwitchChatClear clear)
    {
        if (clear.TargetUserId == null)
        {
            ClearRows();
            AddSystemRow("Chat cleared", TagMuted);
            return;
        }
        for (int i = _rowList.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_rowList[i].UserId, clear.TargetUserId, StringComparison.Ordinal))
                RemoveRow(i);
        }
    }

    private void OnStatus(TwitchChat chat)
    {
        UpdateHeader();
        string status = chat.Status;
        if (status == "Live")
            AddSystemRow("Connected to twitch.tv/" + chat.ConnectedChannel, "#4AD98A");
        else if (status == "Connecting")
            AddSystemRow("Connecting to twitch.tv/" + chat.ConnectedChannel + "...", TagMuted);
        else if (status.StartsWith("Reconnecting", StringComparison.Ordinal))
            AddSystemRow("Connection lost, reconnecting...", "#F5B54A");
        else if (status.StartsWith("Failed", StringComparison.Ordinal) || status.Length > 0 && status != "Idle")
            AddSystemRow(status, "#F05570");
    }

    private void OnHistoryReset(TwitchChat chat)
    {
        ClearRows();
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var chat = _bound;
        string channel = chat?.ConnectedChannel ?? string.Empty;
        SetText(_title, channel.Length > 0 ? "twitch.tv/" + channel : "TwitchChat.Title".AsLocale("Chat").Resolve());
        string status = chat?.Status ?? "No channel";
        SetText(_status, status);
        if (_status != null && !_status.IsDestroyed)
        {
            color tint = status == "Live" ? DashTheme.Positive
                : status.StartsWith("Reconnecting", StringComparison.Ordinal) ? DashTheme.Warning
                : status.StartsWith("Failed", StringComparison.Ordinal) ? DashTheme.Negative
                : DashTheme.TextDim;
            _status.Color.SetValueSilently(tint);
        }
    }

    // ROWS

    private void AppendMessage(TwitchChatMessage message)
    {
        var sb = _builder;
        sb.Clear();
        if (ShowBadges.Value)
            AppendBadges(sb, message);
        if (message.Bits > 0 && !HasBadge(message, "bits"))
            Tag(sb, "BITS " + message.Bits, TagBits);

        sb.Append("<color=").Append(SafeColor(message.Color)).Append('>')
          .Append(Escape(message.DisplayName)).Append("</color>");
        if (message.IsAction)
        {
            sb.Append(' ').Append("<color=").Append(SafeColor(message.Color)).Append('>');
            AppendBody(sb, message);
            sb.Append("</color>");
        }
        else
        {
            sb.Append(": ");
            AppendBody(sb, message);
        }

        AddRow(sb.ToString(), message.Id, message.UserId, DashTheme.Text);
    }

    // Badge pictures where the art is known, the coloured text tags where it is not.
    private void AppendBadges(StringBuilder sb, TwitchChatMessage message)
    {
        bool drewAny = false;
        var badges = message.Badges;
        for (int i = 0; i < badges.Count; i++)
        {
            var badge = badges[i];
            string? url = _bound?.BadgeUrl(badge.Name, badge.Version) ?? TwitchBadgeArt.BadgeUrl(badge.Name, badge.Version);
            string? sprite = url != null ? SpriteFor("b_" + badge.Name + "_" + SafeName(badge.Version), url) : null;
            if (sprite == null)
                continue;
            sb.Append(SpriteOpen).Append(sprite).Append(SpriteClose).Append(' ');
            drewAny = true;
        }
        if (drewAny)
            return;
        if (message.IsBroadcaster) Tag(sb, "OWNER", TagOwner);
        else if (message.IsModerator) Tag(sb, "MOD", TagMod);
        if (message.IsVip) Tag(sb, "VIP", TagVip);
        if (message.IsSubscriber && !message.IsBroadcaster) Tag(sb, "SUB", TagSub);
    }

    private static bool HasBadge(TwitchChatMessage message, string name)
    {
        var badges = message.Badges;
        for (int i = 0; i < badges.Count; i++)
        {
            if (string.Equals(badges[i].Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // The message text with the platform's own emotes swapped in by position and third-party ones
    // by whole word, everything else escaped.
    private void AppendBody(StringBuilder sb, TwitchChatMessage message)
    {
        string text = message.Text ?? string.Empty;
        var emotes = message.Emotes;
        if (emotes.Count == 0)
        {
            AppendWords(sb, text, 0, text.Length);
            return;
        }

        var ordered = new List<TwitchEmote>(emotes);
        ordered.Sort((a, b) => a.Start.CompareTo(b.Start));
        int position = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var emote = ordered[i];
            if (emote.Start < position || emote.End >= text.Length || emote.End < emote.Start)
                continue;
            AppendWords(sb, text, position, emote.Start);
            string? sprite = SpriteFor("t_" + emote.Id, TwitchBadgeArt.EmoteUrl(emote.Id));
            if (sprite != null)
                sb.Append(SpriteOpen).Append(sprite).Append(SpriteClose);
            else
                sb.Append(Escape(text.Substring(emote.Start, emote.End - emote.Start + 1)));
            position = emote.End + 1;
        }
        AppendWords(sb, text, position, text.Length);
    }

    private void AppendWords(StringBuilder sb, string text, int from, int to)
    {
        if (from >= to)
            return;
        var thirdParty = _bound?.ThirdPartyEmotes;
        int start = from;
        for (int i = from; i <= to; i++)
        {
            bool boundary = i == to || text[i] == ' ';
            if (!boundary)
                continue;
            if (i > start)
            {
                string word = text.Substring(start, i - start);
                string? sprite = null;
                if (thirdParty != null && thirdParty.Count > 0 && thirdParty.TryGetValue(word, out string? url))
                    sprite = SpriteFor("s_" + SafeName(word), url);
                if (sprite != null)
                    sb.Append(SpriteOpen).Append(sprite).Append(SpriteClose);
                else
                    sb.Append(Escape(word));
            }
            if (i < to)
                sb.Append(' ');
            start = i + 1;
        }
    }

    // One picture per name: a child slot of the sprite set carrying the image and the sprite that
    // reads it. Returns the sprite name to put in the tag, or null when the cache is full.
    private string? SpriteFor(string name, string url)
    {
        if (_spriteRoot == null || _spriteRoot.IsDestroyed || _spriteSet == null)
            return null;
        if (_spriteNames.Contains(name))
            return name;
        if (_spriteNames.Count >= MaxSprites)
            return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var slot = _spriteRoot.AddSlot(name);
        var image = slot.AttachComponent<ImageProvider>();
        // A frame strip must not be mipmapped, or the frames bleed into each other.
        image.GenerateMipmaps.Value = false;
        image.URL.Value = uri;
        var sprite = slot.AttachComponent<Sprite>();
        sprite.Texture.Target = image;
        _spriteNames.Add(name);
        _pendingSprites[name] = (image, sprite);
        return name;
    }

    // Sprite names ride inside a tag, so only characters that cannot close or confuse one survive.
    private static string SafeName(string word)
    {
        var sb = new StringBuilder(word.Length);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return sb.ToString();
    }

    private void AppendEvent(TwitchChatEvent chatEvent)
    {
        var sb = _builder;
        sb.Clear();
        string label = chatEvent.Kind switch
        {
            TwitchChatEventKind.Subscription => "SUB",
            TwitchChatEventKind.Resubscription => "RESUB",
            TwitchChatEventKind.SubGift => "GIFT",
            TwitchChatEventKind.AnonSubGift => "GIFT",
            TwitchChatEventKind.MysteryGift => "GIFTS",
            TwitchChatEventKind.GiftUpgrade => "UPGRADE",
            TwitchChatEventKind.Raid => "RAID",
            TwitchChatEventKind.Unraid => "RAID",
            TwitchChatEventKind.Announcement => "NOTICE",
            TwitchChatEventKind.BitsBadge => "BITS",
            TwitchChatEventKind.ViewerMilestone => "MILESTONE",
            _ => "EVENT",
        };
        Tag(sb, label, TagEvent);

        string line = chatEvent.SystemMessage;
        if (string.IsNullOrEmpty(line))
            line = chatEvent.DisplayName + " " + label.ToLowerInvariant();
        sb.Append("<color=").Append(TagEvent).Append('>').Append(Escape(line)).Append("</color>");
        if (!string.IsNullOrEmpty(chatEvent.UserMessage))
            sb.Append('\n').Append(Escape(chatEvent.UserMessage));

        AddRow(sb.ToString(), chatEvent.Id, null, DashTheme.Text, highlight: true);
    }

    private void AddSystemRow(string text, string hex)
    {
        var sb = _builder;
        sb.Clear();
        sb.Append("<color=").Append(hex).Append('>').Append(Escape(text)).Append("</color>");
        AddRow(sb.ToString(), null, null, DashTheme.Text, centered: true);
    }

    private void AddRow(string content, string? messageId, string? userId, in color tint, bool centered = false, bool highlight = false)
    {
        if (_rows == null || _rows.IsDestroyed)
            return;

        float size = TextSize.Value;
        float height = EstimateRowHeight(content) + RowPadY * 2f;
        var row = _rows.AddSlot("Row");
        row.AttachComponent<RectTransform>();
        Metrics(row, 0f, 0f, 1f, height, height, 0f);
        // Every message is its own card; system rows stay bare text so they read as the panel talking.
        if (highlight)
            SettingsUI.Panel(row, DashTheme.SurfaceHover, DashTheme.OutlineStrong, DashTheme.RadiusControl);
        else if (!centered)
            SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);

        var ui = new UIBuilder(row);
        ui.Font(_font);
        ui.FontSize(size);
        ui.TextColor(tint);
        var text = ui.Text(content, size, tint);
        var textRect = text.Slot.GetComponent<RectTransform>() ?? text.Slot.AttachComponent<RectTransform>();
        textRect.AnchorMin.Value = float2.Zero;
        textRect.AnchorMax.Value = float2.One;
        textRect.OffsetMin.Value = new float2(RowPadX, RowPadY);
        textRect.OffsetMax.Value = new float2(-RowPadX, -RowPadY);
        text.RichText.Value = true;
        text.WordWrap.Value = true;
        if (_spriteSet != null)
            text.Sprites.Target = _spriteSet;
        text.HorizontalAlignment.Value = centered ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = centered ? TextVerticalAlignment.Middle : TextVerticalAlignment.Top;
        _rowList.Add(new Row { Slot = row, Text = text, MessageId = messageId, UserId = userId, Height = height });

        int limit = System.Math.Max(1, MaxRows.Value);
        while (_rowList.Count > limit)
            RemoveRow(0);

        _contentDirty = true;
        if (_followTail)
            _pinTail = true;
    }

    private void RemoveRow(int index)
    {
        var row = _rowList[index];
        _rowList.RemoveAt(index);
        if (row.Slot != null && !row.Slot.IsDestroyed)
            row.Slot.Destroy();
        _contentDirty = true;
    }

    private void ClearRows()
    {
        for (int i = 0; i < _rowList.Count; i++)
        {
            var slot = _rowList[i].Slot;
            if (slot != null && !slot.IsDestroyed)
                slot.Destroy();
        }
        _rowList.Clear();
        _contentDirty = true;
        _followTail = true;
    }

    // PER-FRAME

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (_pendingSprites.Count > 0)
        {
            _spritePoll -= World?.Time?.Delta ?? 0f;
            if (_spritePoll <= 0f)
            {
                _spritePoll = 0.25f;
                PollSprites();
            }
        }
        if (_contentDirty)
            PinContentHeight();
        if (_pinTail && !_contentDirty)
            PinTail();
    }

    // Pictures arrive after the rows that use them were shaped, so each landing re-shapes the rows
    // once, for the true width and, for a strip, the first-frame window.
    private void PollSprites()
    {
        bool landed = false;
        List<string>? done = null;
        foreach (var pair in _pendingSprites)
        {
            var asset = pair.Value.image.Asset;
            if (asset == null || asset.Width <= 0 || asset.Height <= 0)
                continue;
            done ??= new List<string>();
            done.Add(pair.Key);
            landed = true;
        }

        if (done != null)
        {
            for (int i = 0; i < done.Count; i++)
                _pendingSprites.Remove(done[i]);
        }
        if (!landed)
            return;
        for (int i = 0; i < _rowList.Count; i++)
        {
            var text = _rowList[i].Text;
            if (text != null && !text.IsDestroyed)
                text.Refresh();
        }
    }

    // Row heights are decided when the row is made, so the content rect the scroll reads its range
    // from is exact the same frame a message lands; nothing waits on a layout pass.
    private void PinContentHeight()
    {
        if (_rowsRect == null || _rowsRect.IsDestroyed)
        {
            _contentDirty = false;
            return;
        }

        float total = 0f;
        for (int i = 0; i < _rowList.Count; i++)
        {
            total += _rowList[i].Height;
            if (i > 0) total += RowSpacing;
        }
        total += 4f;

        float viewportHeight = _viewportRect != null && !_viewportRect.IsDestroyed ? _viewportRect.LocalComputeRect.height : 0f;
        if (total < viewportHeight)
            total = viewportHeight;

        _rowsRect.OffsetMin.Value = new float2(0f, -total);
        _rowsRect.OffsetMax.Value = float2.Zero;
        _contentDirty = false;
        _bar?.Refresh();
    }

    private void PinTail()
    {
        _pinTail = false;
        if (_scroll == null || _scroll.IsDestroyed)
            return;
        _applyingScroll = true;
        try
        {
            _scroll.NormalizedPosition = new float2(_scroll.NormalizedPosition.x, 1f);
            _lastPinned = _scroll.NormalizedPosition.y;
        }
        finally
        {
            _applyingScroll = false;
        }
        _bar?.Refresh();
    }

    private void OnScrolled(ScrollRect scroll, float2 position) => OnScrolled();

    private void OnScrolled()
    {
        if (_applyingScroll || _scroll == null || _scroll.IsDestroyed)
            return;
        float y = _scroll.NormalizedPosition.y;
        // Within a hair of the end counts as the end, so a wheel notch back down re-arms the follow.
        _followTail = y >= 0.985f || (_lastPinned >= 0f && MathF.Abs(y - _lastPinned) < 0.001f);
    }

    // HELPERS

    // Lines from characters and width: tags are not glyphs, a newline always starts a line, and a word
    // longer than a line still gets one.
    private float EstimateRowHeight(string content)
    {
        int visible = 0;
        int newlines = 0;
        bool inTag = false;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inTag)
            {
                if (c == '>') inTag = false;
                continue;
            }
            if (c == '<')
            {
                inTag = true;
                if (string.CompareOrdinal(content, i, "<sprite", 0, 7) == 0) visible += 2;
                continue;
            }
            if (c == '\n') { newlines++; continue; }
            visible++;
        }

        float size = System.Math.Max(8f, TextSize.Value);
        float rowWidth = PanelWidth - Padding * 2f - 4f - DashScrollbar.Width - 6f - RowPadX * 2f;
        int perLine = System.Math.Max(8, (int)(rowWidth / (size * GlyphWidthRatio)));
        int lines = System.Math.Max(1, (visible + perLine - 1) / perLine) + newlines;
        // A line carrying a sprite is half again as tall; the estimate errs on the tall side.
        float lineHeight = size * LineHeightRatio * (content.Contains("<sprite", StringComparison.Ordinal) ? 1.5f : 1f);
        return lines * lineHeight + 4f;
    }

    private static void Tag(StringBuilder sb, string label, string hex)
        => sb.Append("<color=").Append(hex).Append(">[").Append(label).Append("]</color> ");

    private static string SafeColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#')
            return TagMuted;
        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(hex[i]))
                return TagMuted;
        }
        return hex;
    }

    // A viewer who types a tag must not restyle the panel, so the one character that opens a tag is
    // swapped for a look-alike the parser ignores.
    private static string Escape(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : text.Replace('<', '‹');

    private static void Metrics(Slot slot, float minWidth, float preferredWidth, float flexibleWidth,
        float minHeight, float preferredHeight, float flexibleHeight)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinWidth.Value = minWidth;
        element.PreferredWidth.Value = preferredWidth;
        element.FlexibleWidth.Value = flexibleWidth;
        element.MinHeight.Value = minHeight;
        element.PreferredHeight.Value = preferredHeight;
        element.FlexibleHeight.Value = flexibleHeight;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void SetText(Text? text, string content)
    {
        if (text == null || text.IsDestroyed)
            return;
        if (!string.Equals(text.Content.Value, content, StringComparison.Ordinal))
            text.Content.Value = content;
    }
}
