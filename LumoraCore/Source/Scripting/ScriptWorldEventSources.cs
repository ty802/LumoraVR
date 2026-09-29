// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Components.Twitch;
using Lumora.Core.Components.Variables;
using Lumora.Core.Math;
using Lumora.Core.Twitch;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// The events a running script hears from the world beyond one object's own changes: bodies appearing,
// saves, named variables moving, and a stream chat reader's feed. ScriptEventSources hands them here as
// it subscribes, and undoes them here with the rest.
//
// Spawns are looked for once a frame by each user's root, so a body made again counts and people standing
// there when the script starts do not. Saves come from the world's own save event, where the save was
// made; the listener is taken off the world outside its event pass, since the pass walks the list it
// sits in. Variables are compared once a frame, and only in scopes whose revision moved. The chat feed
// arrives on the world thread from the reader component each machine runs for itself. -xlinka
internal sealed class ScriptWorldEventSources
{
    private readonly ScriptHostRuntime _runtime;
    private readonly List<ScriptEventEntry> _spawns = new();
    private readonly List<ScriptEventEntry> _saves = new();
    private readonly List<VariableWatch> _variables = new();
    private readonly Dictionary<Slot, List<ScriptEventEntry>> _chat = new();
    private readonly List<Action> _undo = new();
    private readonly Dictionary<User, UserRoot> _roots = new();
    private readonly List<User> _users = new();
    private readonly HashSet<User> _present = new();
    private readonly List<VariableScope> _reach = new();
    private readonly HashSet<(string Scope, string Name)> _moved = new();
    private SaveListener? _saveListener;

    private sealed class VariableWatch
    {
        public Slot Slot = null!;
        public readonly List<ScriptEventEntry> Entries = new();
        public readonly List<(VariableScope Scope, int Revision)> Scopes = new();
        public Dictionary<(VariableScope Scope, string ScopeName, string Name, Type Type), object?> Seen = new();
        public Dictionary<(VariableScope Scope, string ScopeName, string Name, Type Type), object?> Now = new();
    }

    public ScriptWorldEventSources(ScriptHostRuntime runtime)
    {
        _runtime = runtime;
    }

    // Keeps the events this class answers for; the rest are someone else's.
    public void Add(ScriptEventEntry entry, object? source)
    {
        var type = entry.PayloadType;
        if (type == typeof(UserSpawned))
        {
            _spawns.Add(entry);
        }
        else if (type == typeof(SaveCompleted))
        {
            _saves.Add(entry);
        }
        else if (type == typeof(ScopedVariableChanged))
        {
            if (source is Slot { IsDestroyed: false } slot)
                AddVariableWatch(slot, entry);
        }
        else if (type == typeof(StreamChatMessage) || type == typeof(StreamRaid) || type == typeof(StreamSubscription)
                 || type == typeof(StreamRewardRedeemed))
        {
            if (source is Slot { IsDestroyed: false } slot)
            {
                if (!_chat.TryGetValue(slot, out var entries))
                    _chat[slot] = entries = new List<ScriptEventEntry>();
                entries.Add(entry);
            }
        }
    }

    public void Start()
    {
        var world = _runtime.Host.World;
        if (world == null)
            return;
        if (_spawns.Count > 0)
            LookForSpawns(raise: false);
        if (_saves.Count > 0)
        {
            _saveListener = new SaveListener(this);
            world.RegisterEventReceiver(_saveListener);
        }
        foreach (var watch in _variables)
            Compare(watch, raise: false);
        foreach (var (slot, entries) in _chat)
            WatchChat(slot, entries);
    }

    public void Stop()
    {
        foreach (var undo in _undo)
            undo();
        _undo.Clear();
        if (_saveListener is { } listener)
        {
            listener.Stopped = true;
            _saveListener = null;
            var world = _runtime.Host.World;
            world?.RunSynchronously(() => world.UnregisterEventReceiver(listener));
        }
        _spawns.Clear();
        _saves.Clear();
        _variables.Clear();
        _chat.Clear();
        _roots.Clear();
    }

    // Once a frame, from the script phase.
    public void Poll()
    {
        if (_spawns.Count > 0)
            LookForSpawns(raise: true);
        foreach (var watch in _variables)
            Compare(watch, raise: true);
    }

    private void Saved() => Raise(_saves, new SaveCompleted());

    private void LookForSpawns(bool raise)
    {
        var world = _runtime.Host.World;
        if (world == null)
            return;
        world.GetAllUsers(_users);
        _present.Clear();
        foreach (var user in _users)
        {
            if (user.IsDestroyed)
                continue;
            _present.Add(user);
            var root = ScriptReads.RootOf(user);
            if (root == null || root.Slot == null || root.HeadSlot == null)
                continue;
            if (_roots.TryGetValue(user, out var seen) && ReferenceEquals(seen, root))
                continue;
            _roots[user] = root;
            if (raise)
                Raise(_spawns, new UserSpawned(user));
        }
        if (_roots.Count > _present.Count)
        {
            foreach (var gone in new List<User>(_roots.Keys))
            {
                if (!_present.Contains(gone))
                    _roots.Remove(gone);
            }
        }
    }

    private void AddVariableWatch(Slot slot, ScriptEventEntry entry)
    {
        var watch = _variables.Find(w => ReferenceEquals(w.Slot, slot));
        if (watch == null)
        {
            watch = new VariableWatch { Slot = slot };
            _variables.Add(watch);
        }
        watch.Entries.Add(entry);
    }

    // Every variable the watched slot reaches, against what was there at the last look, once the scopes it
    // reaches or their revisions moved.
    private void Compare(VariableWatch watch, bool raise)
    {
        if (watch.Slot.IsDestroyed)
            return;
        ScriptScopedVariables.Reachable(watch.Slot, _reach);
        bool same = watch.Scopes.Count == _reach.Count;
        for (int i = 0; same && i < _reach.Count; i++)
            same = ReferenceEquals(watch.Scopes[i].Scope, _reach[i]) && watch.Scopes[i].Revision == _reach[i].Revision;
        if (same)
            return;

        watch.Scopes.Clear();
        watch.Now.Clear();
        foreach (var scope in _reach)
        {
            watch.Scopes.Add((scope, scope.Revision));
            string scopeName = scope.ActiveName ?? string.Empty;
            foreach (var manager in scope.LiveVariables)
            {
                if (manager.ReadableCount > 0)
                    watch.Now[(scope, scopeName, manager.Name, manager.ValueType)] = manager.BoxedValue;
            }
        }

        if (raise)
        {
            _moved.Clear();
            foreach (var (key, value) in watch.Now)
            {
                if (!watch.Seen.TryGetValue(key, out var before) || !Equals(before, value))
                    _moved.Add((key.ScopeName, key.Name));
            }
            foreach (var key in watch.Seen.Keys)
            {
                if (!watch.Now.ContainsKey(key))
                    _moved.Add((key.ScopeName, key.Name));
            }
            foreach (var (scopeName, name) in _moved)
            {
                bool found = false;
                foreach (var key in watch.Now.Keys)
                    found |= key.ScopeName == scopeName && key.Name == name;
                Raise(watch.Entries, new ScopedVariableChanged(scopeName, name, found));
            }
        }
        (watch.Seen, watch.Now) = (watch.Now, watch.Seen);
    }

    // The reader on the slot, followed as readers come and go there.
    private void WatchChat(Slot slot, List<ScriptEventEntry> entries)
    {
        TwitchChat? hooked = null;
        Action<TwitchChat, TwitchChatMessage> message = (_, said) => Heard(entries, said);
        Action<TwitchChat, TwitchChatEvent> happened = (_, notice) => Heard(entries, notice);
        void Hook()
        {
            var reader = slot.IsDestroyed ? null : slot.GetComponent<TwitchChat>();
            if (reader is { IsDestroyed: true })
                reader = null;
            if (ReferenceEquals(reader, hooked))
                return;
            if (hooked != null)
            {
                hooked.MessageReceived -= message;
                hooked.EventReceived -= happened;
            }
            hooked = reader;
            if (hooked != null)
            {
                hooked.MessageReceived += message;
                hooked.EventReceived += happened;
            }
        }
        Action<Slot, Component> changed = (_, component) =>
        {
            if (component is TwitchChat)
                Hook();
        };
        Hook();
        slot.OnComponentAdded += changed;
        slot.OnComponentRemoved += changed;
        _undo.Add(() =>
        {
            slot.OnComponentAdded -= changed;
            slot.OnComponentRemoved -= changed;
            if (hooked != null)
            {
                hooked.MessageReceived -= message;
                hooked.EventReceived -= happened;
            }
            hooked = null;
        });
    }

    private void Heard(List<ScriptEventEntry> entries, TwitchChatMessage said)
    {
        Raise(entries, new StreamChatMessage(said.Text, said.DisplayName, said.Login, said.UserId, ColorOf(said.Color), said.IsModerator,
            said.IsSubscriber, said.IsBroadcaster, said.IsVip, said.IsFirstMessage, said.IsAction, said.IsHighlighted, said.Bits,
            said.RewardId, said.Id));
        if (said.RewardId.Length > 0)
            Raise(entries, new StreamRewardRedeemed(said.RewardId, said.DisplayName, said.Login, said.UserId, said.Text));
    }

    private void Heard(List<ScriptEventEntry> entries, TwitchChatEvent notice)
    {
        switch (notice.Kind)
        {
            case TwitchChatEventKind.Raid:
                Raise(entries, new StreamRaid(notice.DisplayName, notice.Login, notice.UserId, ColorOf(notice.Color), notice.RaidViewerCount));
                break;
            case TwitchChatEventKind.Subscription:
            case TwitchChatEventKind.Resubscription:
                Raise(entries, new StreamSubscription(notice.DisplayName, notice.Login, notice.UserMessage ?? string.Empty,
                    notice.CumulativeMonths, notice.SubPlan ?? string.Empty, notice.Kind == TwitchChatEventKind.Resubscription,
                    Gifted: false, GiftedBy: string.Empty, Anonymous: false));
                break;
            case TwitchChatEventKind.SubGift:
            case TwitchChatEventKind.AnonSubGift:
                bool anonymous = notice.Kind == TwitchChatEventKind.AnonSubGift;
                Raise(entries, new StreamSubscription(notice.RecipientDisplayName ?? string.Empty, notice.RecipientLogin ?? string.Empty,
                    string.Empty, notice.CumulativeMonths, notice.SubPlan ?? string.Empty, Resub: false, Gifted: true,
                    anonymous ? string.Empty : notice.DisplayName, anonymous));
                break;
        }
    }

    // The colour a name is shown in, as the chat writes it; black when it gives none that reads.
    private static color ColorOf(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return color.Black;
        try
        {
            return color.FromHex(hex.Trim());
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return color.Black;
        }
    }

    private void Raise<T>(List<ScriptEventEntry> entries, T payload) where T : struct
    {
        foreach (var entry in entries)
        {
            if (entry.PayloadType != typeof(T))
                continue;
            var node = entry.NodeId;
            _runtime.Enqueue(runner => runner.Raise(node, payload));
        }
    }

    private sealed class SaveListener : IWorldEventReceiver
    {
        private readonly ScriptWorldEventSources _owner;
        public bool Stopped;

        public SaveListener(ScriptWorldEventSources owner)
        {
            _owner = owner;
        }

        public bool HasEventHandler(World.WorldEvent eventType) => eventType == World.WorldEvent.OnWorldSaved;

        public void OnWorldSaved(string path)
        {
            if (!Stopped)
                _owner.Saved();
        }

        public void OnUserJoined(User user)
        {
        }

        public void OnUserLeft(User user)
        {
        }

        public void OnFocusChanged(World.WorldFocus focus)
        {
        }

        public void OnWorldDestroy()
        {
        }

        public void OnUserSpawn(User user)
        {
        }
    }
}
