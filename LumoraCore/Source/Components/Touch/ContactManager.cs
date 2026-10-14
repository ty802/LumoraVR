// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumora.Core.Math;
using Logger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Touch;

// One per world: every contact in it is worked out here in one pass a frame, run by the first receiver to update.
//
// The pass freezes every live sender into a flat array of shapes (each user's body included), drops them into a
// hash grid of half metre cells, and asks each receiver only about the senders in the cells its own bounds cover.
// A room of avatars covered in receivers and senders is then a few dozen sphere tests a receiver, not every receiver
// against every sender in the room. Tags are checked by mask before anything is measured. -xlinka
public sealed class ContactManager
{
    private static readonly ConditionalWeakTable<World, ContactManager> Managers = new();

    private const float CellSize = 0.5f;
    private const int BucketCount = 1024;
    // A shape covering more cells than this goes on the list every receiver checks.
    private const int MaxCells = 27;

    private readonly World _world;
    private readonly List<ContactSender> _senders = new();
    private readonly List<ContactReceiver> _receivers = new();
    private readonly List<UserContactSenders> _bodies = new();

    private ContactShape[] _shapes = new ContactShape[64];
    private int _shapeCount;

    private readonly int[] _heads = new int[BucketCount];
    private int[] _next = new int[256];
    private int[] _entryShape = new int[256];
    private int _entryCount;
    private readonly List<int> _wide = new();
    private int[] _stamp = new int[64];
    private int _stampValue;

    private ulong _frame = ulong.MaxValue;
    private bool _running;

    private ContactManager(World world)
    {
        _world = world;
    }

    public static ContactManager? For(World? world)
        => world == null ? null : Managers.GetValue(world, static w => new ContactManager(w));

    public World World => _world;

    public int SenderCount => _senders.Count;
    public int ReceiverCount => _receivers.Count;

    // Last frame: shapes placed, sender-receiver pairs measured, and the wall time of the pass.
    public int LastShapes { get; private set; }
    public int LastPairs { get; private set; }
    public double LastMilliseconds { get; private set; }

    internal void Register(ContactSender sender)
    {
        if (sender != null && !_senders.Contains(sender))
            _senders.Add(sender);
    }

    internal void Unregister(ContactSender sender) => _senders.Remove(sender);

    internal void Register(ContactReceiver receiver)
    {
        if (receiver != null && !_receivers.Contains(receiver))
            _receivers.Add(receiver);
    }

    internal void Unregister(ContactReceiver receiver) => _receivers.Remove(receiver);

    internal void Register(UserContactSenders body)
    {
        if (body != null && !_bodies.Contains(body))
            _bodies.Add(body);
    }

    internal void Unregister(UserContactSenders body) => _bodies.Remove(body);

    internal void AddShape(in ContactShape shape)
    {
        if (_shapeCount >= _shapes.Length)
            Array.Resize(ref _shapes, _shapes.Length * 2);
        _shapes[_shapeCount++] = shape;
    }

    internal void Tick()
    {
        if (_running || _world == null || _world.IsDisposed || _world.IsDestroyed)
            return;
        if (_world.Focus == World.WorldFocus.Background)
            return;
        ulong frame = _world.Time?.UpdateIndex ?? 0;
        if (frame == _frame)
            return;
        _frame = frame;
        _running = true;
        long start = Stopwatch.GetTimestamp();
        try
        {
            using var bypass = _world.DataModelPermissions?.EnterSystemBypass();
            Run(frame, _world.Time?.Delta ?? (1f / 60f));
        }
        catch (Exception ex)
        {
            Logger.Error($"ContactManager: contact pass threw - {ex}");
        }
        finally
        {
            _running = false;
            LastMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
    }

    private void Run(ulong frame, float delta)
    {
        _shapeCount = 0;
        for (int i = _senders.Count - 1; i >= 0; i--)
        {
            var sender = _senders[i];
            if (sender == null || sender.IsDestroyed)
            {
                _senders.RemoveAt(i);
                continue;
            }
            sender.Gather(this, frame, delta);
        }
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            var body = _bodies[i];
            if (body == null || body.IsDestroyed)
            {
                _bodies.RemoveAt(i);
                continue;
            }
            body.Contribute(this, frame, delta);
        }
        LastShapes = _shapeCount;
        BuildGrid();

        int pairs = 0;
        for (int i = _receivers.Count - 1; i >= 0; i--)
        {
            var receiver = _receivers[i];
            if (receiver == null || receiver.IsDestroyed)
            {
                _receivers.RemoveAt(i);
                continue;
            }
            pairs += Evaluate(receiver, frame, delta);
        }
        LastPairs = pairs;

        foreach (var receiver in _receivers)
        {
            var target = receiver.ReportTo.Target;
            if (target == null || target.IsDestroyed || ReferenceEquals(target, receiver))
                continue;
            target.Raw = MathF.Max(target.Raw, receiver.Raw);
            target.RawTouching |= receiver.RawTouching;
        }
        foreach (var receiver in _receivers)
            receiver.Publish(receiver.Raw, receiver.RawTouching);
    }

    private int Evaluate(ContactReceiver receiver, ulong frame, float delta)
    {
        receiver.Raw = 0f;
        receiver.RawTouching = false;
        var owner = receiver.Owner(frame, out _);
        if (!receiver.Live(_world, owner) || !receiver.Shape(out var a, out var b, out float radius))
        {
            receiver.WasTouching = false;
            return 0;
        }
        var receiverVelocity = receiver.Velocity((a + b) * 0.5f, frame, delta);
        var tags = receiver.TagIds;
        ulong mask = receiver.TagMask;
        var mode = receiver.Mode.Value;
        bool allowSelf = receiver.AllowSelf.Value;
        bool allowOthers = receiver.AllowOthers.Value;
        bool fromAvatars = receiver.FromAvatars.Value;
        bool fromWorld = receiver.FromWorld.Value;

        float depth = 0f;
        float speed = 0f;
        bool touching = false;
        int pairs = 0;

        var min = new float3(MathF.Min(a.x, b.x) - radius, MathF.Min(a.y, b.y) - radius, MathF.Min(a.z, b.z) - radius);
        var max = new float3(MathF.Max(a.x, b.x) + radius, MathF.Max(a.y, b.y) + radius, MathF.Max(a.z, b.z) + radius);

        if (mask != 0)
        {
            NextStamp();
            if (CellSpan(min, max, out var lo, out var hi))
            {
                for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                        for (int z = lo.z; z <= hi.z; z++)
                        {
                            for (int e = _heads[Bucket(x, y, z)]; e >= 0; e = _next[e])
                                Test(_entryShape[e]);
                        }
            }
            else
            {
                for (int s = 0; s < _shapeCount; s++)
                    Test(s);
            }
            foreach (int s in _wide)
                Test(s);
        }

        void Test(int index)
        {
            if (_stamp[index] == _stampValue)
                return;
            _stamp[index] = _stampValue;
            ref var shape = ref _shapes[index];
            if (!ContactTags.Share(tags, mask, shape.Tags, shape.TagMask))
                return;
            bool self = owner != null && ReferenceEquals(owner, shape.Owner);
            if (self ? !allowSelf : !allowOthers)
                return;
            if (shape.OnAvatar ? !fromAvatars : !fromWorld)
                return;
            if (shape.BoundsMax.x < min.x || shape.BoundsMin.x > max.x || shape.BoundsMax.y < min.y || shape.BoundsMin.y > max.y
                || shape.BoundsMax.z < min.z || shape.BoundsMin.z > max.z)
                return;
            pairs++;
            ContactShape.Closest(a, b, shape.A, shape.B, out var onReceiver, out var onSender);
            float distance = (onReceiver - onSender).Length;
            if (distance >= radius + shape.Radius)
                return;
            touching = true;
            // How far in the sender's surface reaches: to the receiver's core is all the way.
            depth = MathF.Max(depth, System.Math.Clamp(1f - MathF.Max(distance - shape.Radius, 0f) / MathF.Max(radius, 1e-6f), 0f, 1f));
            speed = MathF.Max(speed, (shape.Velocity - receiverVelocity).Length);
        }

        switch (mode)
        {
            case ContactReceiverMode.Depth:
                receiver.Raw = depth;
                break;
            case ContactReceiverMode.Enter:
                receiver.Raw = touching && !receiver.WasTouching && speed >= receiver.MinVelocity.Value ? 1f : 0f;
                break;
            default:
                receiver.Raw = touching ? 1f : 0f;
                break;
        }
        receiver.RawTouching = touching;
        receiver.WasTouching = touching;
        return pairs;
    }

    // ---- grid ----

    private void BuildGrid()
    {
        Array.Fill(_heads, -1);
        _entryCount = 0;
        _wide.Clear();
        if (_stamp.Length < _shapeCount)
            Array.Resize(ref _stamp, System.Math.Max(_shapeCount, _stamp.Length * 2));
        for (int s = 0; s < _shapeCount; s++)
        {
            ref var shape = ref _shapes[s];
            if (!CellSpan(shape.BoundsMin, shape.BoundsMax, out var lo, out var hi))
            {
                _wide.Add(s);
                continue;
            }
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                        Insert(Bucket(x, y, z), s);
        }
    }

    private void Insert(int bucket, int shape)
    {
        if (_entryCount >= _next.Length)
        {
            Array.Resize(ref _next, _next.Length * 2);
            Array.Resize(ref _entryShape, _entryShape.Length * 2);
        }
        _entryShape[_entryCount] = shape;
        _next[_entryCount] = _heads[bucket];
        _heads[bucket] = _entryCount;
        _entryCount++;
    }

    private void NextStamp()
    {
        if (++_stampValue == int.MaxValue)
        {
            Array.Clear(_stamp);
            _stampValue = 1;
        }
    }

    private static bool CellSpan(in float3 min, in float3 max, out (int x, int y, int z) lo, out (int x, int y, int z) hi)
    {
        lo = (Cell(min.x), Cell(min.y), Cell(min.z));
        hi = (Cell(max.x), Cell(max.y), Cell(max.z));
        long cells = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
        return cells > 0 && cells <= MaxCells;
    }

    private static int Cell(float value)
    {
        float cell = MathF.Floor(value / CellSize);
        return cell < -1e8f ? -100000000 : cell > 1e8f ? 100000000 : (int)cell;
    }

    private static int Bucket(int x, int y, int z)
    {
        unchecked
        {
            int hash = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            return hash & (BucketCount - 1);
        }
    }
}
