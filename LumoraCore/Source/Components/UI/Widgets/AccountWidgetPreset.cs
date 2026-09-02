// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using System.Net;
using Helio.UI;
using Helio.UI.Layout;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Components.UI;

// Who you are, as a profile tile: a picture disc on the left, your name and your storage on the right.
// The form that used to live in the card is a modal on the dashboard slot now, so the card stays a card
// and the typing happens in one place.
//
// This is still the only thing in the client that calls the account service sign-in, so read it twice
// before changing it: the typed password lives in a private field and nowhere else, it is never put in a
// sync member, never written to the settings store and never logged. What CAN be remembered is the
// session token the server hands back, and only when the box is ticked. -xlinka
[ComponentCategory("Hidden")]
public sealed class AccountWidgetPreset : HomeWidgetPreset
{
    private const string UserKey = "Account.UserId";
    private const string TokenKey = "Account.Token";

    // CARD. The picture disc is anchored to the left edge and everything else starts to the right of it,
    // so the two halves never fight for the same pixels no matter how the cell stretches.
    private const float AvatarSize = 86f;
    private const float AvatarGap = 14f;
    private const float TextLeft = Pad + AvatarSize + AvatarGap;
    private const float HeadSize = 34f;
    private const float ShoulderWidth = 58f;
    private const float ShoulderHeight = 30f;
    private const float LoginWidth = 168f;
    private const float SignOutWidth = 88f;
    private const float SignOutHeight = 26f;
    private const float VerifiedWidth = 68f;
    private const float BarHeight = 8f;
    // Right-hand strip the storage figure sits in, kept clear of the bar.
    private const float StorageTextWidth = 128f;
    // Right-hand strip the sign-out pill sits in, kept clear of the status line.
    private const float StatusRightInset = Pad + 96f;

    // DIALOG.
    private const float DialogWidth = 420f;
    private const float DialogPad = 22f;
    private const float FieldHeight = 34f;
    private const float RememberHeight = 20f;
    private const float StatusHeight = 18f;
    private const float TitleHeight = 28f;
    private const float SubmitWidth = 132f;
    private const float CancelWidth = 108f;

    // Row tops for the two shapes the dialog takes, and the panel height that goes with each. The
    // two-factor field only exists after the service asks for a code, and reserving its space up front
    // left the form floating above its own buttons. Order: title, username, password, code, remember,
    // buttons, status.
    private static readonly float[] TopsPlain = { 24f, 70f, 112f, 0f, 158f, 194f, 236f };
    private static readonly float[] TopsWithCode = { 24f, 70f, 112f, 154f, 200f, 236f, 278f };
    private const float DialogHeightPlain = 278f;
    private const float DialogHeightWithCode = 320f;

    // Same dim the create-world modal uses, so two dialogs raised from the same screen read as one thing.
    private static readonly color ScrimFill = new color(0.02f, 0.02f, 0.05f, 0.62f);

    private enum Focus { None, Username, Password, Code }

    private sealed class Pill
    {
        public Slot Slot = null!;
        public RoundedPanel Panel = null!;
        public Text Text = null!;
        public Button Button = null!;
        public ColorDriver? Driver;

        public void SetRamp(in color normal, in color hover, in color pressed, in color textColor)
        {
            SetCardRamp(Driver, normal, hover, pressed);
            ListingStyle.SetTextColor(Text, textColor);
        }
    }

    private sealed class Field
    {
        public Slot Slot = null!;
        public RoundedPanel Panel = null!;
        public Text Text = null!;
        public Button Button = null!;
    }

    // Only one widget may try the remembered token per run, however many copies of this card exist.
    private static bool _resumeTried;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _code = string.Empty;
    private bool _remember;
    private bool _needsCode;
    private bool _busy;
    private bool _dialogOpen;
    private Focus _focus = Focus.None;

    private Slot? _signedOutRoot;
    private Slot? _signedInRoot;
    private Pill? _loginPill;
    private Pill? _signOutPill;
    private Text? _status;
    private Text? _name;
    private Text? _email;
    private Slot? _verified;
    private Text? _storage;
    private RectTransform? _barFill;

    private Slot? _dialogBackdrop;
    private Slot? _dialogPanel;
    private RectTransform? _dialogRect;
    private RectTransform? _titleRect;
    private Field? _userField;
    private Field? _passField;
    private Field? _codeField;
    private RoundedPanel? _rememberBox;
    private RectTransform? _rememberRect;
    private Button? _rememberButton;
    private RectTransform? _buttonRowRect;
    private Pill? _submit;
    private Pill? _cancel;
    private Text? _dialogStatus;
    private bool _hooked;

    private static LumoraClient? Client => Engine.Current?.CDNClient;

    // The Home screen this card is docked on, if any. It owns which of the two dashboard modals is up.
    private HomeScreen? HomeHost
    {
        get
        {
            var screen = Slot.GetComponentInParents<HomeScreen>();
            return screen != null && !screen.IsDestroyed ? screen : null;
        }
    }

    public AccountWidgetPreset()
    {
        MinSize.Value = new float2(300f, 110f);
        PreferredSize.Value = new float2(380f, 130f);
        MaxSize.Value = new float2(720f, 260f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        _remember = Lumora.Core.Settings.HasValue(TokenKey);

        if (OnDashboard)
            BuildDialog();

        BuildAvatar(root);
        BuildSignedOut(root);
        BuildSignedIn(root);
        BuildStatus(root);

        Hook();
        ApplyState();

        if (Client?.IsAuthenticated == true)
            LoadProfile();
        else
            TryResume();
    }

    public override void OnDestroy()
    {
        Unhook();
        // The dialog hangs off the dashboard slot, not off this widget, so nothing tears it down when the
        // card is dragged off the grid and destroyed. Take it with us. -xlinka
        if (_dialogBackdrop != null && !_dialogBackdrop.IsDestroyed)
            _dialogBackdrop.Destroy();
        if (_dialogPanel != null && !_dialogPanel.IsDestroyed)
            _dialogPanel.Destroy();
        base.OnDestroy();
    }

    protected override void Poll()
    {
        // Only the pills on the card are gated: the dialog lives above the grid, never under a drag.
        GateForEdit(_loginPill?.Button);
        GateForEdit(_signOutPill?.Button);
    }

    // CARD

    // The disc, and the silhouette that stands in for a picture. Nothing in the profile carries a picture
    // URL, so this is what everyone gets rather than a broken image slot. The head and the shoulder bar sit
    // under a circular stencil mask, which is what lets the shoulders run off the bottom of the disc
    // instead of squaring off inside it. -xlinka
    private void BuildAvatar(Slot root)
    {
        float half = AvatarSize * 0.5f;
        var disc = SettingsUI.Child(root, "Avatar", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(Pad, -half), new float2(Pad + AvatarSize, half));
        SettingsUI.Panel(disc, DashTheme.Field, DashTheme.Outline, half);

        var clip = SettingsUI.Fill(disc, "Clip");
        SettingsUI.Panel(clip, color.White, color.Transparent, half);
        var mask = clip.AttachComponent<Mask>();
        mask.StencilMasking.Value = true;
        mask.ShowMaskGraphic.Value = false;

        float headHalf = HeadSize * 0.5f;
        var head = SettingsUI.Child(clip, "Head", new float2(0.5f, 1f), new float2(0.5f, 1f),
            new float2(-headHalf, -(18f + HeadSize)), new float2(headHalf, -18f));
        SettingsUI.Panel(head, DashTheme.TextMuted, color.Transparent, headHalf);

        float shoulderHalf = ShoulderWidth * 0.5f;
        var shoulders = SettingsUI.Child(clip, "Shoulders", new float2(0.5f, 1f), new float2(0.5f, 1f),
            new float2(-shoulderHalf, -(60f + ShoulderHeight)), new float2(shoulderHalf, -60f));
        SettingsUI.Panel(shoulders, DashTheme.TextMuted, color.Transparent, ShoulderHeight * 0.5f);
    }

    private void BuildSignedOut(Slot root)
    {
        _signedOutRoot = SettingsUI.Fill(root, "SignedOut");

        Label(_signedOutRoot, "Name", "Anonymous", DashTheme.FontTitle, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(TextLeft, -50f), new float2(-Pad, -22f), BoldFont);

        if (_dialogPanel != null)
        {
            var slot = SettingsUI.Child(_signedOutRoot, "Login", new float2(0f, 1f), new float2(0f, 1f),
                new float2(TextLeft, -(56f + DashTheme.ControlHeight)), new float2(TextLeft + LoginWidth, -56f));
            _loginPill = AttachPill(slot, "Login / Register", DashTheme.FontBody, OnLoginPressed);
        }
        else
        {
            // Typed keys are routed to the CURRENT DASH SCREEN and the modal needs a dashboard slot to
            // hang off, so a card floating in the world has neither. Saying so beats a button that opens
            // nothing. -xlinka
            Label(_signedOutRoot, "Note", "Sign in from the Home screen", DashTheme.FontBody, DashTheme.TextDim,
                TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
                new float2(TextLeft, -82f), new float2(-Pad, -58f));
        }
    }

    private void BuildSignedIn(Slot root)
    {
        _signedInRoot = SettingsUI.Fill(root, "SignedIn");

        // The name and the chip share one content-sized row so the chip lands right after the name rather
        // than parked against the far edge of a card whose width is not ours to pick.
        var nameRow = Row(_signedInRoot, "NameRow", 14f, 26f, TextLeft, Pad);
        var layout = nameRow.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = DashTheme.Gap;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = false;
        layout.MainAlignment.Value = MainAxisAlignment.Start;
        layout.CrossAlignment.Value = LayoutAlignment.Center;

        _name = Label(nameRow, "Name", string.Empty, DashTheme.FontTitle, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, float2.One, float2.Zero, float2.Zero, BoldFont);

        _verified = nameRow.AddSlot("Verified");
        _verified.AttachComponent<RectTransform>();
        // The chip is measured by its LayoutElement, not by its label: a LayoutElement outranks the Text on
        // the same slot, so the text goes on a child and the box keeps its own size.
        var chipSize = _verified.AttachComponent<LayoutElement>();
        chipSize.MinWidth.Value = VerifiedWidth;
        chipSize.PreferredWidth.Value = VerifiedWidth;
        chipSize.FlexibleWidth.Value = 0f;
        chipSize.MinHeight.Value = DashTheme.ChipHeight;
        chipSize.PreferredHeight.Value = DashTheme.ChipHeight;
        chipSize.FlexibleHeight.Value = 0f;
        SettingsUI.Panel(_verified, DashTheme.AccentSoft, color.Transparent, DashTheme.RadiusChip);
        SettingsUI.FillLabel(_verified, "Text", SemiboldFont, DashTheme.FontLabel, DashTheme.Accent)
            .Content.Value = "Verified";
        // Off until the profile says otherwise, so a card that comes up already signed in does not flash a
        // badge it has not earned yet.
        SetActive(_verified, false);

        _email = Label(_signedInRoot, "Email", string.Empty, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(TextLeft, -58f), new float2(-Pad, -42f));

        var track = Row(_signedInRoot, "Bar", 70f, BarHeight, TextLeft, Pad + StorageTextWidth);
        SettingsUI.Panel(track, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusControl);
        var fill = SettingsUI.Child(track, "Fill", float2.Zero, new float2(0f, 1f), float2.Zero, float2.Zero);
        SettingsUI.Panel(fill, DashTheme.Accent, color.Transparent, DashTheme.RadiusControl);
        _barFill = SettingsUI.Rect(fill);

        // Right aligned over the full width, so the figure sits against the card edge and the bar keeps
        // whatever is left instead of both being pinned to a guessed split.
        _storage = Label(_signedInRoot, "Storage", "Storage unknown", DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(0f, 1f), new float2(1f, 1f),
            new float2(TextLeft, -86f), new float2(-Pad, -62f));

        var signOut = SettingsUI.Child(_signedInRoot, "SignOut", new float2(1f, 0f), new float2(1f, 0f),
            new float2(-(Pad + SignOutWidth), 12f), new float2(-Pad, 12f + SignOutHeight));
        _signOutPill = AttachPill(signOut, "Sign out", DashTheme.FontSmall, SignOut);
    }

    // One status line for both shapes of the card, hidden while it has nothing to say. It shares its band
    // with the sign-out pill, so it stops short of the pill's strip.
    private void BuildStatus(Slot root)
    {
        _status = Label(root, "Status", string.Empty, DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(TextLeft, -112f), new float2(-StatusRightInset, -96f));
        SetActive(_status.Slot, false);
    }

    // DIALOG
    // Hosted on the CANVAS ROOT (the dashboard slot), the same place the create-world modal goes: the
    // backdrop has to cover the whole dash and the panel has to draw above the nav chrome, and neither is
    // possible from inside a widget on the grid. Owned here all the same, because the fields, the focus,
    // the private password and the whole sign-in flow live in this component. -xlinka

    private void BuildDialog()
    {
        var host = Dash?.Slot;
        if (host == null)
            return;

        _dialogBackdrop = host.AddSlot("SignInBackdrop");
        var backRect = _dialogBackdrop.AttachComponent<RectTransform>();
        backRect.AnchorMin.Value = float2.Zero;
        backRect.AnchorMax.Value = float2.One;
        backRect.OffsetMin.Value = float2.Zero;
        backRect.OffsetMax.Value = float2.Zero;
        _dialogBackdrop.OrderOffset.Value = 9000L;
        // OverlayLevel 1 reserves a render band above all normal UI so the dim covers the nav bar and the
        // header rather than fighting them for order.
        _dialogBackdrop.AttachComponent<GraphicChunkRoot>().OverlayLevel = 1;
        _dialogBackdrop.AttachComponent<Image>().Tint.Value = ScrimFill;
        _dialogBackdrop.AttachComponent<Button>().Clicked += (_, ctx) =>
        {
            // Dismiss only when the click lands OUTSIDE the panel, so a click on a field never closes the
            // dialog even though the full-screen backdrop caught the hit.
            var panelRect = _dialogPanel?.GetComponent<RectTransform>()?.LocalComputeRect;
            if (panelRect.HasValue && panelRect.Value.Contains(ctx.PointIn(_dialogPanel)))
                return;
            CloseSignInDialog();
        };
        _dialogBackdrop.ActiveSelf.Value = false;

        _dialogPanel = host.AddSlot("SignInDialog");
        _dialogRect = _dialogPanel.AttachComponent<RectTransform>();
        _dialogRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        _dialogRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        _dialogPanel.OrderOffset.Value = 10000L;
        _dialogPanel.AttachComponent<GraphicChunkRoot>().OverlayLevel = 2;
        SettingsUI.Panel(_dialogPanel, DashTheme.Panel, DashTheme.OutlineStrong, DashTheme.RadiusPanel);
        // Absorb clicks on the panel background so they never fall through to the backdrop's dismiss.
        _dialogPanel.AttachComponent<Button>();

        var title = Label(_dialogPanel, "Title", "Sign in", DashTheme.FontTitle, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(DialogPad, -52f), new float2(-DialogPad, -24f), BoldFont);
        _titleRect = SettingsUI.Rect(title.Slot);

        _userField = BuildField(_dialogPanel, "Username", TopsPlain[1]);
        _passField = BuildField(_dialogPanel, "Password", TopsPlain[2]);
        _codeField = BuildField(_dialogPanel, "Code", TopsWithCode[3]);

        var rememberRow = Row(_dialogPanel, "Remember", TopsPlain[4], RememberHeight, DialogPad, DialogPad);
        _rememberRect = SettingsUI.Rect(rememberRow);
        var box = SettingsUI.Child(rememberRow, "Box", new float2(0f, 0.5f), new float2(0f, 0.5f),
            new float2(0f, -8f), new float2(16f, 8f));
        _rememberBox = SettingsUI.Panel(box, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusChip);
        Label(rememberRow, "Text", "Remember me", DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 1f), new float2(26f, 0f), float2.Zero);
        _rememberButton = rememberRow.AttachComponent<Button>();
        _rememberButton.Clicked += (_, _) =>
        {
            _remember = !_remember;
            if (!_remember)
                ForgetToken();
            UpdateRemember();
        };

        var buttonRow = Row(_dialogPanel, "Actions", TopsPlain[5], DashTheme.ControlHeight, DialogPad, DialogPad);
        _buttonRowRect = SettingsUI.Rect(buttonRow);
        _submit = BuildPill(buttonRow, "Submit", "Sign in", 0f, SubmitWidth, SignIn);
        _cancel = BuildPill(buttonRow, "Cancel", "Cancel", SubmitWidth + DashTheme.Gap + 4f, CancelWidth,
            CloseSignInDialog);

        _dialogStatus = Label(_dialogPanel, "Status", string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, new float2(0f, 1f), new float2(1f, 1f),
            new float2(DialogPad, -(TopsPlain[6] + StatusHeight)), new float2(-DialogPad, -TopsPlain[6]));

        _userField.Button.Clicked += (_, _) => SetFocus(Focus.Username);
        _passField.Button.Clicked += (_, _) => SetFocus(Focus.Password);
        _codeField.Button.Clicked += (_, _) => SetFocus(Focus.Code);

        _cancel.SetRamp(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed, DashTheme.Text);
        _dialogPanel.ActiveSelf.Value = false;
    }

    private Pill BuildPill(Slot parent, string name, string label, float left, float width, Action onClick)
    {
        var slot = SettingsUI.Child(parent, name, new float2(0f, 0f), new float2(0f, 1f),
            new float2(left, 0f), new float2(left + width, 0f));
        return AttachPill(slot, label, DashTheme.FontBody, onClick);
    }

    private Pill AttachPill(Slot slot, string label, float fontSize, Action onClick)
    {
        var pill = new Pill
        {
            Slot = slot,
            Panel = SettingsUI.Panel(slot, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl),
            Button = slot.AttachComponent<Button>(),
        };
        pill.Text = SettingsUI.FillLabel(slot, "Text", SemiboldFont, fontSize, DashTheme.Text);
        pill.Text.Content.Value = label;
        pill.Driver = pill.Button.AddColorDriver(pill.Panel.Color, DashTheme.Surface, InteractionColorMode.Direct);
        pill.Button.Clicked += (_, _) => onClick();
        return pill;
    }

    private Field BuildField(Slot parent, string name, float top)
    {
        var slot = Row(parent, name, top, FieldHeight, DialogPad, DialogPad);
        var field = new Field
        {
            Slot = slot,
            Panel = SettingsUI.Panel(slot, DashTheme.Field, DashTheme.Outline, DashTheme.RadiusControl),
            Button = slot.AttachComponent<Button>(),
        };
        field.Text = Label(slot, "Text", string.Empty, DashTheme.FontBody, DashTheme.TextMuted,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 1f),
            new float2(10f, 0f), new float2(-10f, 0f));
        return field;
    }

    public void OpenSignInDialog()
    {
        if (_dialogPanel == null || _dialogPanel.IsDestroyed || SignedIn)
            return;
        _dialogOpen = true;
        SetActive(_dialogBackdrop, true);
        SetActive(_dialogPanel, true);
        SetFocus(Focus.Username);
        UpdateSubmitButton();
        Repaint();
    }

    public void CloseSignInDialog()
    {
        if (!_dialogOpen)
            return;
        _dialogOpen = false;
        SetActive(_dialogBackdrop, false);
        SetActive(_dialogPanel, false);
        // Nothing typed outlives the dialog. The username is not a secret and coming back to a filled-in
        // name is worth keeping; the password and the code are neither.
        _password = string.Empty;
        _code = string.Empty;
        _focus = Focus.None;
        UpdateFields();
        Repaint();
    }

    private void OnLoginPressed()
    {
        // The Home screen decides which modal is up, because the create-world dialog is the other one and
        // two of them open at once is two backdrops deep.
        var home = HomeHost;
        if (home != null)
            home.OpenSignIn();
        else
            OpenSignInDialog();
    }

    // STATE

    private bool SignedIn => Client?.IsAuthenticated == true;

    private void ApplyState()
    {
        bool signedIn = SignedIn;
        SetActive(_signedOutRoot, !signedIn);
        SetActive(_signedInRoot, signedIn);
        SetActive(_codeField?.Slot, !signedIn && _needsCode);
        if (signedIn)
            CloseSignInDialog();

        _loginPill?.SetRamp(DashTheme.Accent, DashTheme.AccentHover, DashTheme.AccentPressed,
            WidgetScreen.OnFill(DashTheme.Accent));
        _signOutPill?.SetRamp(DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed, DashTheme.Text);
        ApplyDialogLayout();
        UpdateFields();
        UpdateRemember();
        UpdateSubmitButton();
        Repaint();
    }

    private void ApplyDialogLayout()
    {
        if (_dialogPanel == null || _dialogPanel.IsDestroyed || _dialogRect == null)
            return;
        var tops = _needsCode ? TopsWithCode : TopsPlain;
        float height = _needsCode ? DialogHeightWithCode : DialogHeightPlain;
        SettingsUI.SetOffsets(_dialogRect,
            new float2(-DialogWidth * 0.5f, -height * 0.5f),
            new float2(DialogWidth * 0.5f, height * 0.5f));

        Place(_titleRect, tops[0], TitleHeight);
        Place(_userField?.Slot, tops[1], FieldHeight);
        Place(_passField?.Slot, tops[2], FieldHeight);
        if (_needsCode)
            Place(_codeField?.Slot, tops[3], FieldHeight);
        Place(_rememberRect, tops[4], RememberHeight);
        Place(_buttonRowRect, tops[5], DashTheme.ControlHeight);
        if (_dialogStatus != null && !_dialogStatus.IsDestroyed)
            Place(SettingsUI.Rect(_dialogStatus.Slot), tops[6], StatusHeight);
    }

    private static void Place(Slot? slot, float top, float height)
    {
        if (slot != null && !slot.IsDestroyed)
            Place(SettingsUI.Rect(slot), top, height);
    }

    private static void Place(RectTransform? rect, float top, float height)
    {
        if (rect == null || rect.IsDestroyed)
            return;
        SettingsUI.SetOffsets(rect, new float2(DialogPad, -(top + height)), new float2(-DialogPad, -top));
    }

    private void SetFocus(Focus focus)
    {
        _focus = focus;
        UpdateFields();
        Repaint();
    }

    private void UpdateFields()
    {
        Paint(_userField, Focus.Username, _username, "Username or email");
        Paint(_passField, Focus.Password, new string('•', _password.Length), "Password");
        Paint(_codeField, Focus.Code, _code, "Two-factor code");
    }

    private void Paint(Field? field, Focus which, string value, string placeholder)
    {
        if (field == null)
            return;
        bool focused = _focus == which;
        SettingsUI.SetPaint(field.Panel, DashTheme.Field, focused ? DashTheme.Accent : DashTheme.Outline);
        if (value.Length == 0)
        {
            ListingStyle.SetText(field.Text, focused ? "|" : placeholder);
            ListingStyle.SetTextColor(field.Text, DashTheme.TextMuted);
        }
        else
        {
            ListingStyle.SetText(field.Text, focused ? value + "|" : value);
            ListingStyle.SetTextColor(field.Text, DashTheme.Text);
        }
    }

    private void UpdateRemember()
    {
        if (_rememberBox == null || _rememberBox.IsDestroyed)
            return;
        SettingsUI.SetPaint(_rememberBox,
            _remember ? DashTheme.Accent : DashTheme.Field,
            _remember ? DashTheme.Accent : DashTheme.Outline);
        Repaint();
    }

    private void UpdateSubmitButton()
    {
        if (_submit == null)
            return;
        bool live = !_busy && Connectivity.IsOnline;
        if (live)
            _submit.SetRamp(DashTheme.Accent, DashTheme.AccentHover, DashTheme.AccentPressed,
                WidgetScreen.OnFill(DashTheme.Accent));
        else
            _submit.SetRamp(DashTheme.Field, DashTheme.Field, DashTheme.Field, DashTheme.TextMuted);

        if (!Connectivity.IsOnline && !SignedIn)
            SetStatus("Offline", DashTheme.TextMuted);
    }

    // One message, written to the card and to the dialog at once: the dialog shows it while you are in it,
    // the card keeps it after the dialog goes away.
    private void SetStatus(string text, in color tint)
    {
        WriteStatus(_status, true, text, tint);
        WriteStatus(_dialogStatus, false, text, tint);
        Repaint();
    }

    private static void WriteStatus(Text? label, bool hideWhenEmpty, string text, in color tint)
    {
        if (label == null || label.IsDestroyed)
            return;
        ListingStyle.SetText(label, text);
        ListingStyle.SetTextColor(label, tint);
        if (hideWhenEmpty)
            SetActive(label.Slot, text.Length > 0);
    }

    // KEY INPUT (routed here by HomeScreen while this widget is docked on it)
    // The dialog is modal, so while it is up it takes the keyboard whole and everything else answers false.

    public bool ConsumeChar(char c)
    {
        if (!_dialogOpen)
            return false;
        if (_focus == Focus.None || char.IsControl(c))
            return true;
        switch (_focus)
        {
            case Focus.Username when _username.Length < 64:
                _username += c;
                break;
            case Focus.Password when _password.Length < 128:
                _password += c;
                break;
            case Focus.Code when _code.Length < 12:
                _code += c;
                break;
        }
        UpdateFields();
        Repaint();
        return true;
    }

    public bool ConsumeBackspace()
    {
        if (!_dialogOpen)
            return false;
        switch (_focus)
        {
            case Focus.Username:
                _username = Chop(_username);
                break;
            case Focus.Password:
                _password = Chop(_password);
                break;
            case Focus.Code:
                _code = Chop(_code);
                break;
        }
        UpdateFields();
        Repaint();
        return true;
    }

    public bool ConsumeEnter()
    {
        if (!_dialogOpen)
            return false;
        SetFocus(Focus.None);
        SignIn();
        return true;
    }

    public bool ConsumeEscape()
    {
        if (!_dialogOpen)
            return false;
        CloseSignInDialog();
        return true;
    }

    private static string Chop(string value) => value.Length > 0 ? value.Substring(0, value.Length - 1) : value;

    // ACCOUNT SERVICE

    private async void SignIn()
    {
        if (_busy || SignedIn)
            return;

        var client = Client;
        if (client == null)
        {
            SetStatus("No account service.", DashTheme.Negative);
            return;
        }
        if (!Connectivity.IsOnline)
        {
            SetStatus("Offline", DashTheme.TextMuted);
            return;
        }
        if (_username.Length == 0 || _password.Length == 0)
        {
            SetStatus("Username and password are needed.", DashTheme.Warning);
            return;
        }

        _busy = true;
        UpdateSubmitButton();
        SetStatus("Signing in...", DashTheme.TextDim);

        var world = World;
        string user = _username;
        string secret = _password;
        string? code = _code.Length > 0 ? _code : null;
        bool remember = _remember;

        try
        {
            var result = await client.SignIn(user, secret, remember, code);
            OnUi(world, () =>
            {
                _busy = false;
                if (result.Success)
                {
                    // The Authenticated event has already flipped the view and closed the dialog; this only
                    // makes sure nothing typed outlives the form.
                    _password = string.Empty;
                    _code = string.Empty;
                    UpdateFields();
                    return;
                }
                if (result.Status == HttpStatusCode.Forbidden)
                {
                    // 403 is the answer when the account has two-factor on and no code came with the
                    // request. Same credentials, resubmitted with the code.
                    _needsCode = true;
                    SetActive(_codeField?.Slot, true);
                    ApplyDialogLayout();
                    SetFocus(Focus.Code);
                    SetStatus(result.Message ?? "Enter your two-factor code.", DashTheme.Warning);
                }
                else
                {
                    SetStatus(result.Message ?? "Sign in failed.", DashTheme.Negative);
                }
                UpdateSubmitButton();
            });
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            OnUi(world, () =>
            {
                _busy = false;
                UpdateSubmitButton();
                SetStatus(message, DashTheme.Negative);
            });
        }
    }

    private async void SignOut()
    {
        var client = Client;
        if (client == null || _busy)
            return;
        _busy = true;
        SetStatus("Signing out...", DashTheme.TextDim);
        ForgetToken();

        var world = World;
        try
        {
            var result = await client.SignOut();
            OnUi(world, () =>
            {
                _busy = false;
                if (result.Failed)
                    SetStatus(result.Message ?? "Signed out locally.", DashTheme.TextMuted);
            });
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            OnUi(world, () =>
            {
                _busy = false;
                SetStatus(message, DashTheme.Negative);
            });
        }
    }

    private void TryResume()
    {
        var client = Client;
        if (client == null || client.IsAuthenticated || _resumeTried || !Connectivity.IsOnline)
            return;
        string id = Lumora.Core.Settings.ReadValue<string>(UserKey, string.Empty) ?? string.Empty;
        string token = ReadSealedToken();
        if (id.Length == 0 || token.Length == 0)
            return;
        _resumeTried = true;
        Resume(id, token);
    }

    private async void Resume(string id, string token)
    {
        var client = Client;
        if (client == null)
            return;
        _busy = true;
        UpdateSubmitButton();
        SetStatus("Resuming...", DashTheme.TextDim);

        var world = World;
        try
        {
            var result = await client.SignInWithToken(id, token);
            OnUi(world, () =>
            {
                _busy = false;
                UpdateSubmitButton();
                if (result.Success)
                    return;
                ForgetToken();
                SetStatus("Saved sign-in expired.", DashTheme.TextMuted);
            });
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            OnUi(world, () =>
            {
                _busy = false;
                UpdateSubmitButton();
                SetStatus(message, DashTheme.Negative);
            });
        }
    }

    private async void LoadProfile()
    {
        var client = Client;
        if (client == null || !client.IsAuthenticated)
            return;

        var world = World;
        try
        {
            var profile = await client.GetCurrentUser();
            var quota = await client.GetQuota();
            OnUi(world, () => ApplyProfile(profile, quota));
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            OnUi(world, () => SetStatus(message, DashTheme.Negative));
        }
    }

    private void ApplyProfile(ApiResponse<UserProfile> profile, ApiResponse<UserQuotaResponse> quota)
    {
        if (IsDestroyed)
            return;

        if (profile.Success && profile.Data != null)
        {
            var data = profile.Data;
            if (_name != null && !_name.IsDestroyed)
            {
                ListingStyle.SetText(_name, data.Username);
                // The display color comes back as hex, written in sRGB like every other token, so it has
                // to be decoded before it lands on a vertex.
                ListingStyle.SetTextColor(_name,
                    ColorSwatchStore.TryParseHex(data.NameColor, out var tint) ? tint.ToLinear() : DashTheme.Text);
            }
            if (_email != null && !_email.IsDestroyed)
                ListingStyle.SetText(_email, data.Email ?? string.Empty);
            SetActive(_verified, data.IsVerified);
            SetStatus(string.Empty, DashTheme.TextMuted);
            RememberToken(data.Id);
        }
        else
        {
            SetStatus(profile.Message ?? "Could not load your profile.", DashTheme.Negative);
        }

        if (quota.Success && quota.Data != null && _storage != null && !_storage.IsDestroyed)
        {
            var data = quota.Data;
            ListingStyle.SetText(_storage, $"{FormatSize(data.UsedMB)} / {FormatSize(data.QuotaMB)}");
            float used = data.QuotaMB > 0 ? (float)data.UsedMB / data.QuotaMB : 0f;
            if (_barFill != null && !_barFill.IsDestroyed)
                SettingsUI.SetAnchors(_barFill, float2.Zero, new float2(System.Math.Clamp(used, 0f, 1f), 1f));
        }
        else if (_storage != null && !_storage.IsDestroyed)
        {
            ListingStyle.SetText(_storage, "Storage unknown");
        }

        Repaint();
    }

    // Megabytes are what the service speaks, gigabytes are what a person reads once there are thousands of
    // them. One decimal, and no trailing .0 so a round quota stays "5 GB".
    private static string FormatSize(long megabytes)
    {
        if (megabytes > 1024L)
            return (megabytes / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
        return megabytes.ToString(CultureInfo.InvariantCulture) + " MB";
    }

    // REMEMBERED SESSION
    // The token, not the password: the password is never persisted anywhere. The store is the engine keyed
    // config (a binary data tree in the user roaming folder), the same one the color swatches use, but a
    // bearer token in a plain data tree is a bearer token for anyone who can read the file, so it goes
    // through LocalEncryption (AES-GCM under the platform-sealed vault key, the same wrap saved worlds
    // get) and only the base64 of that blob is written. A blob that fails to open (tampered, or the vault
    // key was re-sealed on another machine) counts as no token. Signing out, or clearing the box, deletes
    // it. -xlinka

    private void RememberToken(string userId)
    {
        var token = Client?.CurrentSession?.Token;
        if (!_remember || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
        {
            if (!_remember)
                ForgetToken();
            return;
        }
        string sealedToken;
        try
        {
            sealedToken = Convert.ToBase64String(
                Persistence.LocalEncryption.Encrypt(System.Text.Encoding.UTF8.GetBytes(token)));
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Account: could not seal the session token, not remembering it ({ex.Message})");
            ForgetToken();
            return;
        }
        Lumora.Core.Settings.WriteValue(UserKey, userId);
        Lumora.Core.Settings.WriteValue(TokenKey, sealedToken);
    }

    private static string ReadSealedToken()
    {
        string stored = Lumora.Core.Settings.ReadValue<string>(TokenKey, string.Empty) ?? string.Empty;
        if (stored.Length == 0)
            return string.Empty;
        try
        {
            var blob = Convert.FromBase64String(stored);
            if (!Persistence.LocalEncryption.IsEncrypted(blob))
                return string.Empty;
            return System.Text.Encoding.UTF8.GetString(Persistence.LocalEncryption.Decrypt(blob));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ForgetToken()
    {
        Lumora.Core.Settings.DeleteValue(UserKey);
        Lumora.Core.Settings.DeleteValue(TokenKey);
    }

    // EVENTS

    private void Hook()
    {
        var client = Client;
        if (client == null || _hooked)
            return;
        _hooked = true;
        client.Authenticated += OnAuthenticated;
        client.SignedOut += OnSignedOut;
    }

    private void Unhook()
    {
        var client = Client;
        if (client == null || !_hooked)
            return;
        _hooked = false;
        client.Authenticated -= OnAuthenticated;
        client.SignedOut -= OnSignedOut;
    }

    private void OnAuthenticated(Lumora.Nexus.Cloud.Cdn.Session session)
    {
        OnUi(World, () =>
        {
            _password = string.Empty;
            _code = string.Empty;
            _needsCode = false;
            _focus = Focus.None;
            ApplyState();
            LoadProfile();
        });
    }

    private void OnSignedOut()
    {
        OnUi(World, () =>
        {
            _username = string.Empty;
            _password = string.Empty;
            _code = string.Empty;
            _needsCode = false;
            _focus = Focus.None;
            _resumeTried = true;
            ApplyState();
        });
    }

    // The account service answers on a background thread; nothing here may touch the UI tree until it is
    // back on the world update thread. The widget can also be dragged off the grid (and destroyed) while a
    // request is in flight, so the continuation checks it is still alive before it paints anything.
    private void OnUi(World? world, Action action)
    {
        if (world == null || world.IsDestroyed)
            return;
        world.RunInUpdates(0, () =>
        {
            if (!IsDestroyed)
                action();
        });
    }
}
