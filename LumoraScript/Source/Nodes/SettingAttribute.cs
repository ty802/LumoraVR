// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A value edited on the node, never supplied by a data wire. It keeps its parameter name and
// position so existing saved values and overload signatures still resolve.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class SettingAttribute : Attribute
{
}
