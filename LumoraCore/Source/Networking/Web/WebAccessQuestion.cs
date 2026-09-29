// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

// One prompt: may scripts reach Host? AskerName is the script's author and Reason is one line the
// client wrote about what the script is doing, never text a script supplied.
public sealed class WebAccessQuestion
{
    private readonly WebAccess _owner;

    public string Host { get; }
    public string AskerName { get; }
    public string WorldName { get; }
    public string Reason { get; }

    internal WebAccessQuestion(WebAccess owner, string host, string askerName, string worldName, string reason)
    {
        _owner = owner;
        Host = host;
        AskerName = askerName;
        WorldName = worldName;
        Reason = reason;
    }

    // True while this is the question on screen and nothing has answered it yet. A prompt that finds
    // it false has been timed out or withdrawn and shows nothing.
    public bool IsOpen => _owner.IsShowing(this);

    // Allowed and Denied are remembered; Ignored is not. A stale answer is dropped.
    public void Answer(WebAccessAnswer answer) => _owner.Answer(this, answer, remember: true);

    // The surface it was handed to is gone. Counts as a no and is not remembered.
    public void CannotShow() => _owner.Answer(this, WebAccessAnswer.Denied, remember: false);
}
