// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The text in the field bound as its source, else the one on the object the script sits on, was changed
// by someone typing into it; however many keys they pressed in a frame, it fires once, with the text as it
// stands. A script writing the field is not typing. -xlinka
[Node("Events/On Text Edited")]
[SourcedEvent]
public sealed class OnTextEdited : EventNode<TextEdited>
{
}
