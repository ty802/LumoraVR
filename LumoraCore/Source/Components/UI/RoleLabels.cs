// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Localization;

namespace Lumora.Core.Components.UI;

// The words people read for each session role. The gate names its roles Host, Admin, Builder, Moderator,
// User and Spectator, and those names are identifiers: saved worlds, the host's config on the wire and
// scripts' role checks all carry them, so they stay put and only the words on screen change. Anything
// that shows a role goes through here. -xlinka
public static class RoleLabels
{
    public static readonly LocaleText Host = "Session.Role.Host".AsLocale("Host");
    public static readonly LocaleText CoHost = "Session.Role.CoHost".AsLocale("Co-host");
    public static readonly LocaleText Creator = "Session.Role.Creator".AsLocale("Creator");
    public static readonly LocaleText Moderator = "Session.Role.Moderator".AsLocale("Moderator");
    public static readonly LocaleText Member = "Session.Role.Member".AsLocale("Member");
    public static readonly LocaleText Viewer = "Session.Role.Viewer".AsLocale("Viewer");

    public static LocaleText Label(string? roleName) => roleName switch
    {
        "Host" => Host,
        "Admin" => CoHost,
        "Builder" => Creator,
        "Moderator" => Moderator,
        "User" => Member,
        "Spectator" => Viewer,
        _ => LocaleText.Plain(roleName ?? string.Empty),
    };

    public static string Of(string? roleName) => Label(roleName).Resolve();

    public static string Of(DataModelPermissionRole? role) => role == null ? string.Empty : Of(role.Name);

    // The mark on a nameplate chip. Two letters, because the names pair up on their initials (Co-host and
    // Creator, Moderator and Member) and a colour alone is too little to tell two chips apart across a
    // room. -xlinka
    public static string Badge(string? roleName) => roleName switch
    {
        "Host" => "H",
        "Admin" => "Co",
        "Builder" => "Cr",
        "Moderator" => "Mo",
        "User" => "Me",
        "Spectator" => "Vi",
        _ => string.IsNullOrEmpty(roleName) ? "?" : roleName!.Substring(0, 1).ToUpperInvariant(),
    };
}
