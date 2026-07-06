// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

internal static class LayoutSizing
{
    // Per-child outer margin (x=left, y=bottom, z=right, w=top). Zero when no LayoutElement.
    public static float4 GetMargin(RectTransform rect)
    {
        var element = rect.Slot.GetComponent<LayoutElement>();
        return element != null ? element.Margin.Value : float4.Zero;
    }

    internal struct Element
    {
        public float Size;
        public float Offset;
        public float Min;
        public float Grow;
        public float Flexible;

        public Element(in LayoutMetrics metrics)
        {
            Size = 0f;
            Offset = 0f;
            Min = metrics.Min;
            Grow = Max(0f, metrics.Preferred - metrics.Min);
            Flexible = Max(0f, metrics.Flexible);
        }
    }

    public static bool IsIgnored(RectTransform rect)
    {
        return rect.Slot.GetComponent<IgnoreLayout>() != null;
    }

    // Prefer the bottom-up measured cache (populated by the canvas measure pass before arrange); fall
    // back to a live compute for any rect not yet measured (e.g. added mid-arrange) so we never read a
    // stale zero. This is what layouts read during arrange/aggregation. -xlinka
    public static LayoutMetrics Measured(RectTransform rect, LayoutDirection direction)
        => rect.MetricsValid ? rect.GetMeasuredMetrics(direction) : GetMetrics(rect, direction);

    public static LayoutMetrics GetMetrics(RectTransform rect, LayoutDirection direction)
    {
        var result = default(LayoutMetrics);
        bool hasAny = false;
        int priority = int.MinValue;

        foreach (var element in rect.Slot.GetComponentsImplementing<ILayoutElement>())
        {
            if (element is Component component && !component.Enabled.Value)
            {
                continue;
            }

            element.PrepareCompute();
            element.EnsureValidMetrics(direction);
            if (element.Priority < priority)
            {
                continue;
            }

            if (element.Priority > priority)
            {
                result = default;
                hasAny = false;
                priority = element.Priority;
            }

            if (direction == LayoutDirection.Horizontal)
            {
                ApplyMetric(ref result.Min, element.MinWidth, ref hasAny);
                ApplyMetric(ref result.Preferred, element.PreferredWidth, ref hasAny);
                ApplyMetric(ref result.Flexible, element.FlexibleWidth, ref hasAny);
            }
            else
            {
                ApplyMetric(ref result.Min, element.MinHeight, ref hasAny);
                ApplyMetric(ref result.Preferred, element.PreferredHeight, ref hasAny);
                ApplyMetric(ref result.Flexible, element.FlexibleHeight, ref hasAny);
            }
        }

        if (!hasAny)
        {
            result.Flexible = 0f;
        }

        if (result.Preferred < result.Min)
        {
            result.Preferred = result.Min;
        }

        return result;
    }

    public static void Distribute(float totalSize, float spacing, float offset, IReadOnlyList<LayoutMetrics> metrics,
        List<Element> elements, bool forceExpand)
    {
        elements.Clear();
        float remaining = totalSize - spacing * Max(0, metrics.Count - 1);

        for (int i = 0; i < metrics.Count; i++)
        {
            var element = new Element(metrics[i]);
            element.Size = element.Min;
            remaining -= element.Min;
            elements.Add(element);
        }

        if (remaining > 0f)
        {
            float totalGrow = 0f;
            for (int i = 0; i < elements.Count; i++)
            {
                totalGrow += elements[i].Grow;
            }

            if (totalGrow > 0f)
            {
                float factor = Min(1f, remaining / totalGrow);
                for (int i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];
                    element.Size += element.Grow * factor;
                    elements[i] = element;
                }
                remaining -= totalGrow * factor;
            }
        }

        if (remaining > 0f)
        {
            float totalFlexible = 0f;
            for (int i = 0; i < elements.Count; i++)
            {
                totalFlexible += elements[i].Flexible;
            }

            if (totalFlexible > 0f)
            {
                for (int i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];
                    element.Size += remaining * (element.Flexible / totalFlexible);
                    elements[i] = element;
                }
                remaining = 0f;
            }
        }

        if (remaining > 0f && forceExpand && elements.Count > 0)
        {
            float extra = remaining / elements.Count;
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                element.Size += extra;
                elements[i] = element;
            }
            remaining = 0f;
        }

        float cursor = offset;
        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            element.Offset = cursor;
            cursor += element.Size + spacing;
            elements[i] = element;
        }
    }

    // Re-distribute leftover main-axis space across already-sized elements per a
    // justify-content rule. Called after Distribute when the layout isn't
    // force-expanding the main axis. No-op for Start or when there's no leftover.
    internal static void AlignMainAxis(List<Element> elements, float innerSize, float padStart, float spacing, MainAxisAlignment align)
    {
        int n = elements.Count;
        if (n == 0 || align == MainAxisAlignment.Start) return;

        float content = 0f;
        for (int i = 0; i < n; i++)
            content += elements[i].Size;
        content += spacing * Max(0, n - 1);

        float leftover = innerSize - content;
        if (leftover <= 0.0001f) return; // overflow or exact fit - nothing to distribute

        float start = padStart;
        float gap = spacing;
        switch (align)
        {
            case MainAxisAlignment.Center:
                start = padStart + leftover * 0.5f;
                break;
            case MainAxisAlignment.End:
                start = padStart + leftover;
                break;
            case MainAxisAlignment.SpaceBetween:
                if (n > 1) gap = spacing + leftover / (n - 1);
                else start = padStart + leftover * 0.5f; // single child centers
                break;
            case MainAxisAlignment.SpaceAround:
            {
                float unit = leftover / n;
                start = padStart + unit * 0.5f;
                gap = spacing + unit;
                break;
            }
            case MainAxisAlignment.SpaceEvenly:
            {
                float unit = leftover / (n + 1);
                start = padStart + unit;
                gap = spacing + unit;
                break;
            }
        }

        float cursor = start;
        for (int i = 0; i < n; i++)
        {
            var element = elements[i];
            element.Offset = cursor;
            cursor += element.Size + gap;
            elements[i] = element;
        }
    }

    private static void ApplyMetric(ref float field, float? value, ref bool hasAny)
    {
        if (!value.HasValue)
        {
            return;
        }

        field = value.Value;
        hasAny = true;
    }

    private static float Max(float a, float b) => a > b ? a : b;
    private static int Max(int a, int b) => a > b ? a : b;
    private static float Min(float a, float b) => a < b ? a : b;
}
