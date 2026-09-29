// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Localization;
using Lumora.Core.Networking.Web;

namespace Lumora.Core.Components.UI;

// A web access question as a modal on the local dashboard: the host, what the script is doing there,
// who wrote the script and in which world, then Deny and Allow. Dismissing it answers Ignored, which is
// not remembered.
//
// Allow stays dead for its first three seconds on screen. The clock starts again whenever the dialog is
// rebuilt (a modal stacked over it closed) or the dash comes back from closed, so a click that was
// already on its way when the dialog appeared cannot land on Allow. -xlinka
[ComponentCategory("Hidden")]
public sealed class WebAccessPrompt : Component
{
    public const float AllowDelay = 3f;

    private const int DenyIndex = 0;
    private const int AllowIndex = 1;
    // A long user or world name is clipped rather than allowed to push the host out of the box.
    private const int NameLength = 48;

    private static readonly LocaleText TitleText = "WebAccess.Prompt.Title".AsLocale("Allow web access?");
    private static readonly LocaleText DenyText = "WebAccess.Prompt.Deny".AsLocale("Deny");
    private static readonly LocaleText AllowText = "WebAccess.Prompt.Allow".AsLocale("Allow");
    private static readonly LocaleText UnknownAuthor = "WebAccess.Prompt.UnknownAuthor".AsLocale("an unknown author");
    private static readonly LocaleText UnnamedWorld = "WebAccess.Prompt.UnnamedWorld".AsLocale("an unnamed world");

    private WebAccessQuestion? _question;
    private ModalRequest? _request;
    private ModalHost? _host;
    private Button? _allowButton;
    private bool _allowReady;
    private double _allowAt;
    private ulong _lastUpdate;
    // Set once the dash was opened for a question. It closes again when no question follows.
    private bool _closeDash;

    public WebAccessQuestion? Question => _question;

    public bool AllowReady => _question != null && _allowReady;

    public static WebAccessPrompt? For(Dashboard? dashboard)
    {
        if (dashboard == null || dashboard.IsDestroyed || dashboard.Slot == null || dashboard.Slot.IsDestroyed)
            return null;
        return dashboard.Slot.GetComponent<WebAccessPrompt>() ?? dashboard.Slot.AttachComponent<WebAccessPrompt>();
    }

    public void Present(WebAccessQuestion question, bool openedDash)
    {
        if (ReferenceEquals(_question, question))
            return;
        if (_question != null)
            TakeDown();

        var host = ModalHost.For(Slot);
        if (host == null)
        {
            question.CannotShow();
            return;
        }
        Hook(host);

        var request = new ModalRequest
        {
            Title = TitleText,
            Message = "WebAccess.Prompt.Message".AsLocale("{0}\n{1}\nScript by {2}, in {3}",
                question.Host, question.Reason,
                Clip(question.AskerName, UnknownAuthor), Clip(question.WorldName, UnnamedWorld)),
            Invoker = Slot,
            CancelIndex = -1,
            DefaultIndex = -1,
            DismissOnScrim = true,
        };
        request.AddOption(DenyText);
        request.AddOption(AllowText, ModalOptionStyle.Accent);
        request.Completed = result => OnCompleted(request, result);

        _question = question;
        _request = request;
        _allowReady = false;
        _closeDash |= openedDash;
        if (!host.Open(request))
        {
            _question = null;
            _request = null;
            question.CannotShow();
            ScheduleDashClose();
            return;
        }
        Gate(restart: true);
    }

    // Nobody waits on the answer any more: it timed out, or the script stopped.
    public void Withdraw(WebAccessQuestion question)
    {
        if (!ReferenceEquals(_question, question))
            return;
        TakeDown();
        ScheduleDashClose();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        Gate(restart: false);
    }

    public override void OnDestroy()
    {
        if (_host != null)
            _host.Closed -= OnHostClosed;
        _host = null;
        var question = _question;
        _question = null;
        _request = null;
        _allowButton = null;
        question?.Answer(WebAccessAnswer.Ignored);
        base.OnDestroy();
    }

    // Allow is live only once it has sat on screen for AllowDelay. A new button instance means the dialog
    // was rebuilt, and a gap in updates means the dash was closed and nothing could be seen.
    private void Gate(bool restart)
    {
        var host = _host;
        if (_request == null || host == null || host.IsDestroyed || !ReferenceEquals(host.Current, _request))
            return;
        var buttons = host.OptionButtons;
        if (buttons.Count <= AllowIndex)
            return;
        var allow = buttons[AllowIndex];
        if (allow == null || allow.IsDestroyed)
            return;

        var time = World.Time;
        if (restart || !ReferenceEquals(allow, _allowButton) || time.UpdateIndex > _lastUpdate + 1)
        {
            _allowButton = allow;
            _allowAt = time.TotalTime + AllowDelay;
        }
        _lastUpdate = time.UpdateIndex;
        _allowReady = time.TotalTime >= _allowAt;
        if (allow.Interactable.Value != _allowReady)
            allow.Interactable.Value = _allowReady;
    }

    private void OnCompleted(ModalRequest request, ModalResult result)
    {
        if (!ReferenceEquals(_request, request))
            return;
        var question = _question;
        var answer = result.Index switch
        {
            AllowIndex => WebAccessAnswer.Allowed,
            DenyIndex => WebAccessAnswer.Denied,
            _ => WebAccessAnswer.Ignored,
        };
        // Only a live Allow button counts as Allow; anything that completes the dialog on that index
        // early is treated as a dismissal.
        if (answer == WebAccessAnswer.Allowed && !_allowReady)
            answer = WebAccessAnswer.Ignored;

        _question = null;
        _request = null;
        _allowButton = null;
        _allowReady = false;
        question?.Answer(answer);
        ScheduleDashClose();
    }

    // Takes the dialog down without an answer. The host can only close its top dialog, which this one is
    // in practice: its scrim eats every press that could open another over it.
    private void TakeDown()
    {
        var request = _request;
        var host = _host;
        _question = null;
        _request = null;
        _allowButton = null;
        _allowReady = false;
        if (request != null && host != null && !host.IsDestroyed && ReferenceEquals(host.Current, request))
            host.Complete(-1);
    }

    private void Hook(ModalHost host)
    {
        if (ReferenceEquals(_host, host))
            return;
        if (_host != null)
            _host.Closed -= OnHostClosed;
        _host = host;
        host.Closed += OnHostClosed;
    }

    // A dialog stacked over this one closed and the host rebuilt ours with fresh buttons: gate them now,
    // not on the next update.
    private void OnHostClosed(ModalHost host, Slot? invoker)
    {
        if (_request != null && ReferenceEquals(host.Current, _request))
            Gate(restart: true);
    }

    // The next question comes in through a queued post, so the dash waits a couple of updates before
    // closing rather than flickering shut and open again between two questions.
    private void ScheduleDashClose()
    {
        if (!_closeDash)
            return;
        World?.RunInUpdates(2, () =>
        {
            if (IsDestroyed || _question != null || !_closeDash)
                return;
            _closeDash = false;
            var dash = UserspaceDashboard.LocalInstance;
            if (dash != null && !dash.IsDestroyed && ReferenceEquals(dash.World, World))
                dash.Close();
        });
    }

    private static string Clip(string text, in LocaleText fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback.Resolve();
        return text.Length <= NameLength ? text : text.Substring(0, NameLength - 3) + "...";
    }
}
