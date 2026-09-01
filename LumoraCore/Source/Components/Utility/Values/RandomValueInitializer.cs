// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Per-type random draw for the initializer, built once and cast through object exactly once each, the
// same way ValueOps does it - a type test at the call site would box both bounds and the result on
// every roll. A type with no entry leaves the delegate null, and that null IS the answer the
// IsValidGenericType guard reports. -xlinka
internal static class RandomValue<T>
{
    public static readonly Func<SeededRandom, T, T, T>? Range;

    // What a fresh component offers as its upper bound, so the first roll after attaching is useful
    // instead of a number from the far end of the type.
    public static readonly T DefaultMax;

    public static bool CanRange => Range != null;

    static RandomValue()
    {
        DefaultMax = SyncCoder.GetDefault<T>();

        if (typeof(T) == typeof(float))
        {
            Range = Cast<float>((r, lo, hi) => r.Range(lo, hi));
            DefaultMax = Box(1f);
        }
        else if (typeof(T) == typeof(double))
        {
            Range = Cast<double>((r, lo, hi) => lo + (hi - lo) * r.Value);
            DefaultMax = Box(1d);
        }
        else if (typeof(T) == typeof(int))
        {
            Range = Cast<int>((r, lo, hi) => RangeInclusive(r, lo, hi));
            DefaultMax = Box(1);
        }
        else if (typeof(T) == typeof(long))
        {
            // Through double, so a range wider than 2^53 quantizes. A long-typed field with a range that
            // wide is a bit pattern, not a number somebody is picking between two bounds. -xlinka
            Range = Cast<long>((r, lo, hi) => lo + (long)System.Math.Round((hi - lo) * (double)r.Value));
            DefaultMax = Box(1L);
        }
        else if (typeof(T) == typeof(float2))
        {
            Range = Cast<float2>((r, lo, hi) => new float2(r.Range(lo.x, hi.x), r.Range(lo.y, hi.y)));
            DefaultMax = Box(float2.One);
        }
        else if (typeof(T) == typeof(float3))
        {
            Range = Cast<float3>((r, lo, hi) => r.Range(lo, hi));
            DefaultMax = Box(float3.One);
        }
        else if (typeof(T) == typeof(float4))
        {
            Range = Cast<float4>((r, lo, hi) => new float4(
                r.Range(lo.x, hi.x), r.Range(lo.y, hi.y), r.Range(lo.z, hi.z), r.Range(lo.w, hi.w)));
            DefaultMax = Box(new float4(1f, 1f, 1f, 1f));
        }
        else if (typeof(T) == typeof(floatQ))
        {
            // A rotation has no per-component range worth drawing in; the honest reading of "between
            // these two" is a point along the shortest arc between them.
            Range = Cast<floatQ>((r, lo, hi) => floatQ.Slerp(lo, hi, r.Value));
            DefaultMax = Box(floatQ.Identity);
        }
        else if (typeof(T) == typeof(color))
        {
            Range = Cast<color>((r, lo, hi) => new color(
                r.Range(lo.r, hi.r), r.Range(lo.g, hi.g), r.Range(lo.b, hi.b), r.Range(lo.a, hi.a)));
            DefaultMax = Box(color.White);
        }
        else if (typeof(T) == typeof(colorHDR))
        {
            Range = Cast<colorHDR>((r, lo, hi) => new colorHDR(
                r.Range(lo.r, hi.r), r.Range(lo.g, hi.g), r.Range(lo.b, hi.b), r.Range(lo.a, hi.a)));
            DefaultMax = Box(colorHDR.White);
        }
    }

    // Inclusive of both ends: a die goes up to six, not to five.
    private static int RangeInclusive(SeededRandom random, int low, int high)
    {
        if (high <= low)
            return low;
        long span = (long)high - low + 1L;
        return (int)(low + (long)(random.NextUInt() % (ulong)span));
    }

    private static Func<SeededRandom, T, T, T> Cast<TConcrete>(Func<SeededRandom, TConcrete, TConcrete, TConcrete> op)
        => (Func<SeededRandom, T, T, T>)(object)op;

    private static T Box<TConcrete>(TConcrete value) => (T)(object)value!;
}

// Rolls one random value into a field when the object comes to life.
//
// One generic instead of a component per type: the draw table above is what varies, and the wiring
// around it - when to roll, who rolls, what the seed means - is identical for every one of them.
//
// Who rolls matters. A field write replicates, so if every peer rolled on start, an object would land on
// as many different values as there are people in the session and the last write would win at random.
// The authority rolls on start; the peer that made a copy rolls for the copy, because it is the one
// holding the new slots before anybody else has seen them. -xlinka
[ComponentCategory("Utility/Values")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class RandomValueInitializer<T> : Component
{
    public readonly SyncRef<IField<T>> Target;

    public readonly Sync<T> Min;

    public readonly Sync<T> Max;

    // Zero draws from the machine, anything else draws the same value every time - the same seed in the
    // same component is the same number on every peer and after every load.
    public readonly Sync<int> Seed;

    public readonly Sync<bool> RandomizeOnStart;

    public readonly Sync<bool> RandomizeOnDuplicate;

    public static bool IsValidGenericType => RandomValue<T>.CanRange;

    public RandomValueInitializer()
    {
        Target = new SyncRef<IField<T>>(this);
        Min = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Max = new Sync<T>(this, RandomValue<T>.DefaultMax);
        Seed = new Sync<int>(this, 0);
        RandomizeOnStart = new Sync<bool>(this, true);
        RandomizeOnDuplicate = new Sync<bool>(this, true);
    }

    public override void OnStart()
    {
        base.OnStart();
        if (RandomizeOnStart.Value && World?.IsAuthority == true)
            Randomize();
    }

    public override void OnDuplicate()
    {
        base.OnDuplicate();
        if (RandomizeOnDuplicate.Value)
            Randomize();
    }

    [SyncMethod]
    public void Randomize()
    {
        var target = Target.Target;
        var draw = RandomValue<T>.Range;
        if (target == null || draw == null || !target.CanWrite)
            return;

        int seed = Seed.Value;
        var random = seed != 0 ? new SeededRandom(seed) : new SeededRandom();
        target.Value = draw(random, Min.Value, Max.Value);
    }
}
