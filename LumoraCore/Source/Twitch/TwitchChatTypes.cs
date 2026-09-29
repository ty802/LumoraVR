// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Twitch;

public enum TwitchChatEventKind
{
    Subscription,
    Resubscription,
    SubGift,
    AnonSubGift,
    MysteryGift,
    GiftUpgrade,
    Raid,
    Unraid,
    Announcement,
    BitsBadge,
    ViewerMilestone,
    Unknown
}

public readonly struct TwitchBadge
{
    public readonly string Name;
    public readonly string Version;

    public TwitchBadge(string name, string version)
    {
        Name = name;
        Version = version;
    }

    public override string ToString() => Name + "/" + Version;
}

public readonly struct TwitchEmote
{
    public readonly string Id;
    public readonly int Start;
    public readonly int End;

    public TwitchEmote(string id, int start, int end)
    {
        Id = id;
        Start = start;
        End = end;
    }

    public int Length => End - Start + 1;
}

public readonly struct TwitchRoomState
{
    public readonly string ChannelLogin;
    public readonly string RoomId;
    public readonly bool EmoteOnly;
    public readonly int FollowersOnlyMinutes;
    public readonly int SlowSeconds;
    public readonly bool SubscribersOnly;
    public readonly bool UniqueChat;

    public TwitchRoomState(string channelLogin, string roomId, bool emoteOnly, int followersOnlyMinutes, int slowSeconds, bool subscribersOnly, bool uniqueChat)
    {
        ChannelLogin = channelLogin;
        RoomId = roomId;
        EmoteOnly = emoteOnly;
        FollowersOnlyMinutes = followersOnlyMinutes;
        SlowSeconds = slowSeconds;
        SubscribersOnly = subscribersOnly;
        UniqueChat = uniqueChat;
    }

    // default(TwitchRoomState) would read as "followers-only, 0 minutes"; Default is the off state. -xlinka
    public static TwitchRoomState Default => new TwitchRoomState(string.Empty, string.Empty, false, -1, 0, false, false);

    public bool FollowersOnly => FollowersOnlyMinutes >= 0;
}

public sealed class TwitchChatMessage
{
    public string Id { get; init; } = string.Empty;
    public string ChannelLogin { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Login { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool IsAction { get; init; }
    public IReadOnlyList<TwitchBadge> Badges { get; init; } = Array.Empty<TwitchBadge>();
    public bool IsBroadcaster { get; init; }
    public bool IsModerator { get; init; }
    public bool IsVip { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsFirstMessage { get; init; }
    public bool IsReturningChatter { get; init; }
    public int Bits { get; init; }
    public IReadOnlyList<TwitchEmote> Emotes { get; init; } = Array.Empty<TwitchEmote>();
    public string? ReplyParentMessageId { get; init; }
    public string? ReplyParentDisplayName { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    // The channel reward this message redeemed, for a reward that asks the viewer for text; empty otherwise.
    public string RewardId { get; init; } = string.Empty;

    // Sent with the channel's highlight-my-message reward.
    public bool IsHighlighted { get; init; }

    public string ToDisplayText() => Text;

    public override string ToString() => DisplayName + ": " + Text;
}

public sealed class TwitchChatEvent
{
    public TwitchChatEventKind Kind { get; init; } = TwitchChatEventKind.Unknown;
    public string RawKind { get; init; } = string.Empty;
    public string SystemMessage { get; init; } = string.Empty;
    public string? UserMessage { get; init; }
    public string Login { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string? SubPlan { get; init; }
    public int CumulativeMonths { get; init; }
    public int StreakMonths { get; init; }
    public int GiftCount { get; init; }
    public string? RecipientDisplayName { get; init; }
    public int RaidViewerCount { get; init; }
    public string? AnnouncementColor { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? RecipientLogin { get; init; }

    public string ToDisplayText() => SystemMessage.Length > 0 ? SystemMessage : RawKind;

    public override string ToString() => ToDisplayText();
}

public sealed class TwitchChatClear
{
    public string? TargetUserId { get; init; }
    public string? TargetLogin { get; init; }
    public int? BanDurationSeconds { get; init; }
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsWholeRoom => TargetLogin == null && TargetUserId == null;
    public bool IsPermanent => !IsWholeRoom && BanDurationSeconds == null;
}

public static class TwitchChatColors
{
    private static readonly string[] Palette =
    {
        "#FF0000", "#0000FF", "#00FF00", "#B22222", "#FF7F50",
        "#9ACD32", "#FF4500", "#2E8B57", "#DAA520", "#D2691E",
        "#5F9EA0", "#1E90FF", "#FF69B4", "#8A2BE2", "#00FF7F"
    };

    public static IReadOnlyList<string> DefaultPalette => Palette;

    // string.GetHashCode is salted per process, so a user's colour would change every launch. -xlinka
    public static string ForLogin(string? login)
    {
        if (string.IsNullOrEmpty(login)) return Palette[0];
        uint hash = 2166136261u;
        for (int i = 0; i < login.Length; i++)
        {
            char c = login[i];
            if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            hash ^= c;
            hash *= 16777619u;
        }
        return Palette[(int)(hash % (uint)Palette.Length)];
    }
}
