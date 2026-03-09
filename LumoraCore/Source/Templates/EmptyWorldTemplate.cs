// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Lumora.Core.Templates;

internal sealed class EmptyWorldTemplate : WorldTemplateDefinition
{
    public EmptyWorldTemplate() : base("Empty") { }

    protected override void Build(World world)
    {
        // Intentionally minimal.
    }

    protected override void PostBuild(World world)
    {
        // Skip the default slot-count log for the empty template.
    }
}
