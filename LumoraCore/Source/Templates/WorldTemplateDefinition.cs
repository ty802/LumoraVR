// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Logging;

namespace Lumora.Core.Templates;

/// <summary>
/// Base class for defining a world template.
/// </summary>
public abstract class WorldTemplateDefinition
{
    /// <summary>
    /// Template name used in <see cref="WorldTemplates"/>.
    /// </summary>
    public string TemplateName { get; }

    protected WorldTemplateDefinition(string templateName)
    {
        TemplateName = templateName ?? throw new ArgumentNullException(nameof(templateName));
    }

    /// <summary>
    /// Modes this template may be hosted in. Default is all of them, so a built-in space (e.g. a grid)
    /// can be opened as Builder, Social, or Event at the host's choice. A purpose-built / published
    /// world can override this to lock itself to e.g. Social only.
    /// </summary>
    public virtual IReadOnlyList<WorldMode> AllowedModes { get; } = new[]
    {
        WorldMode.Builder,
        WorldMode.Social,
        WorldMode.Event
    };

    /// <summary>Mode used when the host doesn't pick one explicitly.</summary>
    public virtual WorldMode DefaultMode => WorldMode.Builder;

    /// <summary>
    /// Apply the template to the provided world instance.
    /// </summary>
    public void Apply(World world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        Logger.Log($"WorldTemplates: Initializing {TemplateName} world");
        Build(world);
        PostBuild(world);
    }

    protected abstract void Build(World world);

    protected virtual void PostBuild(World world)
    {
        Logger.Log($"WorldTemplates: {TemplateName} initialized with {world.RootSlot.ChildCount} root slots");
    }
}
