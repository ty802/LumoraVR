// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// What Set Channel, Add To Channel and Multiply Channel share. Whatever part is edited lands in the range
// the type gives it: 0 to 1 on a color, and on an HDR color red, green, blue and brightness from 0 up with
// alpha 0 to 1. Hue wraps round and saturation is 0 to 1 on both. Parts not edited pass through as they
// are, except that hue, saturation and brightness rebuild the color from HSV. Raw lane arithmetic with no
// ranges is what Math/Add and Math/Multiply on colors are for. A NaN edit lands at 0, and a channel this
// does not know gives the color back as it is. -xlinka
internal static class ColorChannelEdit
{
    public enum Op : byte
    {
        Set,
        Add,
        Multiply
    }

    public static color Apply(color value, ColorChannel channel, Op op, float amount)
    {
        var edited = Apply(new colorHDR(value.r, value.g, value.b, value.a), channel, op, amount, hdr: false);
        return new color(edited.r, edited.g, edited.b, edited.a);
    }

    public static colorHDR Apply(colorHDR value, ColorChannel channel, Op op, float amount)
        => Apply(value, channel, op, amount, hdr: true);

    private static colorHDR Apply(colorHDR c, ColorChannel channel, Op op, float amount, bool hdr)
    {
        switch (channel)
        {
            case ColorChannel.Red:
                c.r = Light(Edit(op, c.r, amount), hdr);
                return c;
            case ColorChannel.Green:
                c.g = Light(Edit(op, c.g, amount), hdr);
                return c;
            case ColorChannel.Blue:
                c.b = Light(Edit(op, c.b, amount), hdr);
                return c;
            case ColorChannel.Alpha:
                c.a = ColorModels.Unit(Edit(op, c.a, amount));
                return c;
            case ColorChannel.RGB:
                return new colorHDR(Light(Edit(op, c.r, amount), hdr), Light(Edit(op, c.g, amount), hdr),
                    Light(Edit(op, c.b, amount), hdr), c.a);
            case ColorChannel.Hue or ColorChannel.Saturation or ColorChannel.Brightness:
                ColorModels.ToHsv(c.r, c.g, c.b, out float h, out float s, out float v);
                if (channel == ColorChannel.Hue)
                    h = Edit(op, h, amount);
                else if (channel == ColorChannel.Saturation)
                    s = Edit(op, s, amount);
                else
                    v = Edit(op, v, amount);
                if (hdr)
                    return ColorModels.FromHsvHdr(h, s, v, c.a);
                var ldr = ColorModels.FromHsv(h, s, v, c.a);
                return new colorHDR(ldr.r, ldr.g, ldr.b, ldr.a);
            default:
                return c;
        }
    }

    private static float Edit(Op op, float current, float amount) => op switch
    {
        Op.Set => amount,
        Op.Add => current + amount,
        _ => current * amount,
    };

    private static float Light(float value, bool hdr)
        => hdr ? (value > 0f ? MathF.Min(value, float.MaxValue) : 0f) : ColorModels.Unit(value);
}
