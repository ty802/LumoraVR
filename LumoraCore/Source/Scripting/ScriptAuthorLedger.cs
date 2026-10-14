// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// What each person's scripts hold between them, across every script in the world: slots made that
// still stand, slots made in the last minute, rays cast this frame, and working memory. Nothing here
// stops a script: past a Warn mark the script that went over is warned, so fifty scripted objects from
// one person show up as one person's doing.
//
// People are told apart by ScriptPrincipal.AuthorKey. The world's own scripts are not counted here: the
// world is not a person, and what may be built into a world is a question for the roles. What a guest's
// scripts made in their byte goes when they do: the host takes it away at the next recheck, before the
// byte can pass to anyone else. -xlinka
public sealed class ScriptAuthorLedger
{
    public const int WarnMadePerMinute = 600;
    public const int WarnRaysPerFrame = 2048;
    public const long WarnMemoryBytes = 64L * 1024 * 1024;

    private sealed class Author
    {
        public readonly List<(Slot Slot, int Count)> Standing = new();
        public readonly Queue<(double Time, int Count)> Recent = new();
        public int RecentTotal;
        public ulong CastFrame = ulong.MaxValue;
        public int Casts;
        public long Memory;
        public int SweepStandingAt = 64;
    }

    private readonly World _world;
    private readonly Dictionary<string, Author> _authors = new(StringComparer.Ordinal);

    internal ScriptAuthorLedger(World world)
    {
        _world = world;
    }

    // Working memory the author's running scripts hold.
    public long MemoryOf(ScriptPrincipal principal)
        => _authors.TryGetValue(principal.AuthorKey, out var author) ? author.Memory : 0;

    // Every author this machine has counted, as they stand now. The world's own scripts are never counted.
    public void GetAccounts(List<ScriptAuthorAccount> into)
    {
        into.Clear();
        ulong frame = _world.Time.UpdateIndex;
        double now = _world.Time.TotalTime;
        foreach (var (key, author) in _authors)
        {
            int standing = 0;
            foreach (var (slot, count) in author.Standing)
            {
                if (!slot.IsDestroyed)
                    standing += count;
            }
            int recent = 0;
            foreach (var (time, count) in author.Recent)
            {
                if (now - time < 60.0)
                    recent += count;
            }
            into.Add(new ScriptAuthorAccount(key, author.Memory, standing, recent, author.CastFrame == frame ? author.Casts : 0));
        }
    }

    // True as the author's scripts pass WarnRaysPerFrame in this frame.
    internal bool CountCast(ScriptPrincipal principal)
    {
        if (principal.IsWorldContent)
            return false;
        var author = Get(principal);
        ulong frame = _world.Time.UpdateIndex;
        if (frame != author.CastFrame)
        {
            author.CastFrame = frame;
            author.Casts = 0;
        }
        return ++author.Casts == WarnRaysPerFrame + 1;
    }

    // Told before count more slots are made; true as the author's scripts pass WarnMadePerMinute.
    internal bool ReserveMade(ScriptPrincipal principal, int count)
    {
        if (principal.IsWorldContent)
            return false;
        var author = Get(principal);
        double now = _world.Time.TotalTime;
        while (author.Recent.Count > 0 && now - author.Recent.Peek().Time >= 60.0)
            author.RecentTotal -= author.Recent.Dequeue().Count;
        return author.RecentTotal <= WarnMadePerMinute && author.RecentTotal + count > WarnMadePerMinute;
    }

    internal void Made(ScriptPrincipal principal, Slot slot, int count)
    {
        if (principal.IsWorldContent)
            return;
        var author = Get(principal);
        author.Recent.Enqueue((_world.Time.TotalTime, count));
        author.RecentTotal += count;
        if (author.Standing.Count >= author.SweepStandingAt)
        {
            author.Standing.RemoveAll(static made => made.Slot.IsDestroyed);
            author.SweepStandingAt = System.Math.Max(64, author.Standing.Count * 2);
        }
        author.Standing.Add((slot, count));
    }

    // On the host: whatever a script made in a user's byte whose maker is no longer the one behind it.
    internal void DropMadeByAbsent()
    {
        if (!_world.IsAuthority)
            return;
        List<Slot>? gone = null;
        foreach (var (key, author) in _authors)
        {
            author.Standing.RemoveAll(static made => made.Slot.IsDestroyed);
            foreach (var (slot, _) in author.Standing)
            {
                byte owner = slot.ReferenceID.GetUserByte();
                if (slot.IsLocalElement || !RefIDConstants.IsValidUserByte(owner) || HeldBy(owner, key))
                    continue;
                (gone ??= new List<Slot>()).Add(slot);
            }
        }
        if (gone == null)
            return;
        using (_world.DataModelPermissions?.EnterSystemBypass())
        {
            foreach (var slot in gone)
            {
                if (!slot.IsDestroyed)
                    slot.Destroy();
            }
        }
    }

    private bool HeldBy(byte owner, string key)
    {
        foreach (var user in _world.GetAllUsers())
        {
            if (!user.IsDestroyed && user.HoldsByte(owner))
                return ScriptPrincipal.AuthorKeyOf(user) == key;
        }
        return false;
    }

    // Held for as long as a script runs. Freed is what a script of the same author that this one takes
    // over from is about to give back. True when the author's scripts then hold more than WarnMemoryBytes.
    internal bool Hold(ScriptPrincipal principal, long bytes, long freed)
    {
        if (principal.IsWorldContent)
            return false;
        var author = Get(principal);
        author.Memory += bytes;
        return author.Memory - freed > WarnMemoryBytes;
    }

    internal void Release(ScriptPrincipal principal, long bytes)
    {
        if (principal.IsWorldContent || !_authors.TryGetValue(principal.AuthorKey, out var author))
            return;
        author.Memory = System.Math.Max(0, author.Memory - bytes);
    }

    private Author Get(ScriptPrincipal principal)
    {
        if (!_authors.TryGetValue(principal.AuthorKey, out var author))
        {
            author = new Author();
            _authors.Add(principal.AuthorKey, author);
        }
        return author;
    }
}
