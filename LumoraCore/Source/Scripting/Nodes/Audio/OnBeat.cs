// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// An Audio Analyzer on the slot bound as source heard an onset: a drum hit, a note starting, a clap. With
// nothing bound, the analyser on the script's own slot. Heard where the script runs, from that machine's analyser.
[Node("Audio/Analysis/On Beat")]
[SourcedEvent]
public sealed class OnBeat : EventNode<AudioBeat>
{
}
