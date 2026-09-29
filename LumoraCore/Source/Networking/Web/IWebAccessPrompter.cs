// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

// Whatever puts a WebAccessQuestion in front of this machine's user. Every member is called from any
// thread and must not block: marshal onto the UI thread and return.
public interface IWebAccessPrompter
{
    // A local user and somewhere to show them a prompt exist right now.
    bool CanAsk { get; }

    // Show the question and answer it through question.Answer once the user has. False when it cannot
    // be shown at all; a surface that turns out to be gone after returning true calls CannotShow.
    bool Show(WebAccessQuestion question);

    // Take a shown question down without an answer: it timed out, or nobody waits on it any more.
    void Withdraw(WebAccessQuestion question);
}
