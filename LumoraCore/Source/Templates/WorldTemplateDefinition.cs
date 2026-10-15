// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumora.Core;
using Lumora.Core.Logging;

namespace Lumora.Core.Templates;

public abstract class WorldTemplateDefinition
{
    public string TemplateName { get; }

    protected WorldTemplateDefinition(string templateName)
    {
        TemplateName = templateName ?? throw new ArgumentNullException(nameof(templateName));
    }

    // All of them by default, so a built-in space opens as whatever mode the host picks. A purpose-built
    // or published world overrides this to lock itself to e.g. Social only.
    public virtual IReadOnlyList<WorldMode> AllowedModes { get; } = new[]
    {
        WorldMode.Builder,
        WorldMode.Social,
        WorldMode.Event
    };

    public virtual WorldMode DefaultMode => WorldMode.Builder;

    public void Apply(World world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        Logger.Debug($"WorldTemplates: Initializing {TemplateName} world");
        foreach (var step in Steps(world))
            step();
        PostBuild(world);
    }

    // The same build into a world held before it runs, as many steps a frame as fit the world's staged
    // budget, pumped the way a saved world's load is; whenDone runs after the last step. A step that throws is
    // logged and the rest still come. -xlinka
    public void ApplyStaged(World world, Action? whenDone = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        Logger.Debug($"WorldTemplates: Initializing {TemplateName} world a step at a time");
        var steps = Steps(world).GetEnumerator();
        world.BeginStagedWork(() =>
        {
            long until = Stopwatch.GetTimestamp() + World.StagedBudgetTicks;
            do
            {
                if (!steps.MoveNext())
                {
                    steps.Dispose();
                    PostBuild(world);
                    whenDone?.Invoke();
                    return true;
                }
                try
                {
                    steps.Current();
                }
                catch (Exception ex)
                {
                    Logger.Error($"WorldTemplates: a step of {TemplateName} failed: {ex}");
                }
                world.CountStagedWork(1);
            }
            while (Stopwatch.GetTimestamp() < until);
            return false;
        });
    }

    // What a template builds, in steps. Apply runs them all at once; ApplyStaged runs as many as fit a frame,
    // so a big template held before its world runs never stops one frame for the whole of it. A template with
    // little to build has one step, its Build. -xlinka
    protected virtual IEnumerable<Action> Steps(World world)
    {
        yield return () => Build(world);
    }

    protected virtual void Build(World world)
    {
    }

    protected virtual void PostBuild(World world)
    {
        Logger.Debug($"WorldTemplates: {TemplateName} initialized with {world.RootSlot.ChildCount} root slots");
    }
}
