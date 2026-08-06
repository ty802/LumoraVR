// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Networking.Sync;

public static class FieldValidators
{
    public static Func<T, IField<T>, T> Clamp<T>(T min, T max) where T : IComparable<T>
    {
        return (value, field) =>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        };
    }

    public static Func<float, IField<float>, float> ClampFloat(float min, float max)
    {
        return (value, field) => System.Math.Clamp(value, min, max);
    }

    public static Func<double, IField<double>, double> ClampDouble(double min, double max)
    {
        return (value, field) => System.Math.Clamp(value, min, max);
    }

    public static Func<int, IField<int>, int> ClampInt(int min, int max)
    {
        return (value, field) => System.Math.Clamp(value, min, max);
    }

    public static Func<float, IField<float>, float> SanitizeFloat(float fallback = 0f)
    {
        return (value, field) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    public static Func<double, IField<double>, double> SanitizeDouble(double fallback = 0.0)
    {
        return (value, field) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }

    public static Func<float3, IField<float3>, float3> SanitizeFloat3(float3? fallback = null)
    {
        var fb = fallback ?? float3.Zero;
        return (value, field) =>
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z))
            {
                return fb;
            }
            return value;
        };
    }

    public static Func<float4, IField<float4>, float4> SanitizeFloat4(float4? fallback = null)
    {
        var fb = fallback ?? float4.Zero;
        return (value, field) =>
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z) ||
                float.IsNaN(value.w) || float.IsInfinity(value.w))
            {
                return fb;
            }
            return value;
        };
    }

    public static Func<floatQ, IField<floatQ>, floatQ> SanitizeQuaternion()
    {
        return (value, field) =>
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z) ||
                float.IsNaN(value.w) || float.IsInfinity(value.w))
            {
                return floatQ.Identity;
            }
            var lengthSq = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            if (lengthSq < 0.0001f)
            {
                return floatQ.Identity;
            }
            return value;
        };
    }

    public static Func<string, IField<string>, string> MaxLength(int maxLength)
    {
        return (value, field) =>
        {
            if (value == null) return null!;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        };
    }

    public static Func<string, IField<string>, string> NullToEmpty()
    {
        return (value, field) => value ?? string.Empty;
    }

    public static Func<string, IField<string>, string> Trim()
    {
        return (value, field) => value?.Trim() ?? null!;
    }

    public static Func<float, IField<float>, float> NonNegative()
    {
        return (value, field) => value < 0 ? 0 : value;
    }

    public static Func<float, IField<float>, float> Positive(float minPositive = 0.0001f)
    {
        return (value, field) => value < minPositive ? minPositive : value;
    }

    public static Func<T, IField<T>, T> Combine<T>(params Func<T, IField<T>, T>[] filters)
    {
        return (value, field) =>
        {
            foreach (var filter in filters)
            {
                value = filter(value, field);
            }
            return value;
        };
    }
}

public static class FieldValidatorExtensions
{
    public static SyncField<float> WithClamp(this SyncField<float> field, float min, float max)
    {
        field.LocalFilter = FieldValidators.ClampFloat(min, max);
        return field;
    }

    public static SyncField<float> WithSanitize(this SyncField<float> field, float fallback = 0f)
    {
        field.LocalFilter = FieldValidators.SanitizeFloat(fallback);
        return field;
    }

    public static SyncField<float3> WithSanitize(this SyncField<float3> field, float3? fallback = null)
    {
        field.LocalFilter = FieldValidators.SanitizeFloat3(fallback);
        return field;
    }

    public static SyncField<floatQ> WithSanitize(this SyncField<floatQ> field)
    {
        field.LocalFilter = FieldValidators.SanitizeQuaternion();
        return field;
    }

    public static SyncField<string> WithMaxLength(this SyncField<string> field, int maxLength)
    {
        field.LocalFilter = FieldValidators.MaxLength(maxLength);
        return field;
    }

    public static SyncField<float> WithNonNegative(this SyncField<float> field)
    {
        field.LocalFilter = FieldValidators.NonNegative();
        return field;
    }
}
