// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// The field bound as its source changed, by anyone. Several changes in one frame fire it once. A script that
// writes the field it watches hears itself, a frame later each time. -xlinka
[Node("Events/On Value Changed")]
[SourcedEvent("anyfield")]
public sealed class OnValueChanged : EventNode<FieldChanged>
{
}
