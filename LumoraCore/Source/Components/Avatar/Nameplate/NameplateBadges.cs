// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// One chip on a nameplate's badge row. A value type on purpose: the manager collects a fresh list every
// reconcile and compares it against what it drew, so a badge has to be cheap to build and cheap to
// compare. Glyph is drawn with the plate font, so it must be text the shipping font actually has - a
// letter, not a private-use icon. There are no image assets in this path. -xlinka
public readonly struct NameplateBadge : IEquatable<NameplateBadge>
{
    // Stable id. Doubles as the chip's slot name and as the dedup key, so two sources cannot fight over
    // the same chip.
    public readonly string Key;

    public readonly string Glyph;

    public readonly color Chip;

    public readonly color Ink;

    // Lower sorts further left. Ties break on Key so the row order is stable frame to frame.
    public readonly int Order;

    public NameplateBadge(string key, string glyph, color chip, color ink, int order)
    {
        Key = key ?? string.Empty;
        Glyph = glyph ?? string.Empty;
        Chip = chip;
        Ink = ink;
        Order = order;
    }

    public bool Equals(NameplateBadge other)
        => Key == other.Key && Glyph == other.Glyph && Chip == other.Chip && Ink == other.Ink && Order == other.Order;

    public override bool Equals(object? obj) => obj is NameplateBadge other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Glyph, Order);
}

// What a badge source gets to look at. Everything here is already replicated state or a local read of
// the permission gate - a source must not write anything.
public readonly struct NameplateBadgeContext
{
    public readonly World World;
    public readonly User User;
    public readonly UserRoot Root;

    public NameplateBadgeContext(World world, User user, UserRoot root)
    {
        World = world;
        User = user;
        Root = root;
    }
}

public interface INameplateBadgeSource
{
    void CollectBadges(in NameplateBadgeContext context, List<NameplateBadge> output);
}

// The extension seam. Registration is process-wide, not per-world: a source is a rule about what a badge
// MEANS, and that answer cannot differ between the session world and the dashboard overlay.
//
// Collect runs on the world update thread, once every reconcile per visible user, so it must not
// allocate beyond what the caller's list already holds. Registration can come from anywhere (a module
// loading late), hence the lock on the source list and the snapshot copy. -xlinka
public static class NameplateBadgeRegistry
{
    private static readonly object Gate = new();
    private static readonly List<INameplateBadgeSource> Sources = new();
    private static INameplateBadgeSource[] _snapshot = Array.Empty<INameplateBadgeSource>();

    static NameplateBadgeRegistry()
    {
        Register(new HostBadgeSource());
        Register(new RoleBadgeSource());
    }

    public static void Register(INameplateBadgeSource source)
    {
        if (source == null)
            return;
        lock (Gate)
        {
            if (Sources.Contains(source))
                return;
            Sources.Add(source);
            _snapshot = Sources.ToArray();
        }
    }

    public static bool Unregister(INameplateBadgeSource source)
    {
        if (source == null)
            return false;
        lock (Gate)
        {
            if (!Sources.Remove(source))
                return false;
            _snapshot = Sources.ToArray();
            return true;
        }
    }

    public static int SourceCount
    {
        get { lock (Gate) return Sources.Count; }
    }

    public static void Collect(in NameplateBadgeContext context, List<NameplateBadge> output)
    {
        if (output == null)
            return;
        output.Clear();
        if (context.World == null || context.World.IsDestroyed || context.User == null || context.User.IsDestroyed)
            return;

        var sources = _snapshot;
        for (int i = 0; i < sources.Length; i++)
        {
            try
            {
                sources[i].CollectBadges(in context, output);
            }
            catch (Exception ex)
            {
                // A badge source throwing must not take the plate (or the frame) down with it. Drop that
                // source's chips for this pass and carry on with the rest.
                Logging.Logger.Warn($"NameplateBadgeRegistry: {sources[i].GetType().Name} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        DedupAndSort(output);
    }

    private static void DedupAndSort(List<NameplateBadge> badges)
    {
        for (int i = badges.Count - 1; i > 0; i--)
        {
            for (int j = 0; j < i; j++)
            {
                if (badges[i].Key == badges[j].Key)
                {
                    badges.RemoveAt(i);
                    break;
                }
            }
        }

        badges.Sort(static (a, b) =>
        {
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
        });
    }
}

// SHIPPING SOURCES
// Two, and only the two that have a real signal behind them. A badge that cannot be derived from
// replicated state or from the gate is a badge that lies on somebody's plate.

public static class NameplateBadgeColors
{
    public static readonly color Host = new color(1f, 0.78f, 0.28f, 1f);
    public static readonly color Admin = new color(1f, 0.47f, 0.36f, 1f);
    public static readonly color Moderator = new color(0.42f, 0.71f, 1f, 1f);
    public static readonly color Builder = new color(0.53f, 0.88f, 0.56f, 1f);
    public static readonly color Guest = new color(0.78f, 0.78f, 0.84f, 1f);
    public static readonly color Spectator = new color(0.6f, 0.6f, 0.66f, 1f);
    public static readonly color Ink = new color(0.06f, 0.07f, 0.09f, 1f);

    public static color ForRole(string? roleName) => roleName switch
    {
        "Host" => Host,
        "Admin" => Admin,
        "Moderator" => Moderator,
        "Builder" => Builder,
        "Guest" => Guest,
        "Spectator" => Spectator,
        _ => Guest,
    };
}

internal sealed class HostBadgeSource : INameplateBadgeSource
{
    public void CollectBadges(in NameplateBadgeContext context, List<NameplateBadge> output)
    {
        var permissions = context.World?.DataModelPermissions;
        if (permissions == null || context.User == null)
            return;
        if (!ReferenceEquals(permissions.GetRole(context.User), permissions.HostRole))
            return;

        output.Add(new NameplateBadge("host", "H", NameplateBadgeColors.Host, NameplateBadgeColors.Ink, 0));
    }
}

// Only badges a role that is NOT what this world hands a joiner. Everybody being a Builder in a builder
// world is not information, and a row of identical chips over every head is just noise. -xlinka
internal sealed class RoleBadgeSource : INameplateBadgeSource
{
    public void CollectBadges(in NameplateBadgeContext context, List<NameplateBadge> output)
    {
        var world = context.World;
        var permissions = world?.DataModelPermissions;
        if (permissions == null || context.User == null)
            return;

        var role = permissions.GetRole(context.User);
        if (role == null || ReferenceEquals(role, permissions.HostRole))
            return; // the host chip already says it

        var fallback = ResolveWorldDefault(world!, permissions);
        if (fallback != null && ReferenceEquals(role, fallback))
            return;

        output.Add(new NameplateBadge("role", Initial(role.Name),
            NameplateBadgeColors.ForRole(role.Name), NameplateBadgeColors.Ink, 1));
    }

    // The role this world lands an UNASSIGNED joiner on: the host's configured joiner default when it
    // named one, otherwise the Visitor default the world mode baked in.
    //
    // Deliberately does not ask for THIS user's access class. An assigned user is classified by the tier
    // their own role belongs to, so GetDefaultRole(GetAccessClass(user)) walks straight back to roughly
    // the role they were assigned and every badge silently disappears. The comparison has to be against
    // a user-independent answer, and an unassigned non-host user is always a Visitor. -xlinka
    //
    // Read through RootSlot.GetComponent, NOT World.PermissionConfig - that property ATTACHES the config
    // on the authority, and a nameplate has no business authoring world components as a side effect of
    // drawing itself.
    private static DataModelPermissionRole? ResolveWorldDefault(World world,
        DataModelPermissionController permissions)
    {
        var config = world.RootSlot?.GetComponent<WorldPermissionConfig>();
        var named = permissions.FindRole(config?.DefaultJoinerRole.Value);
        if (named != null)
            return named;
        return permissions.GetDefaultRole(DataModelAccessClass.Visitor);
    }

    private static string Initial(string? roleName)
        => string.IsNullOrEmpty(roleName) ? "?" : roleName!.Substring(0, 1).ToUpperInvariant();
}
