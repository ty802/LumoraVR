// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The one palette the dashboard draws from. Every screen used to carry its own dozen color constants,
// which is how the dash ended up as five slightly different purples fighting each other. Screens pull
// from here and nowhere else: dark glassy ground, one accent for the primary action, status colors
// only where they mean something, mode tints only on chips and placeholder art.
//
// Sizes are canvas units on the 1180x720 dashboard canvas, not screen pixels. -xlinka
public static class DashTheme
{
    // Every color below is written in sRGB, the numbers you read off a design tool, and decoded to
    // linear ONCE here. The UI mesh path hands vertex colors to the shader untouched and the swapchain
    // applies the sRGB transfer on the way out, so a token pasted straight in shows up about two stops
    // brighter than written: this Backdrop went on screen as (77,75,93), the flat lavender everyone
    // hated. Consumers get linear values, which is also what luminance math wants. -xlinka
    private static color S(float r, float g, float b, float a = 1f) => new color(r, g, b, a).ToLinear();

    // Mesh materials take their tint as sRGB and linearise it themselves, so a theme colour (stored
    // linear) has to go back to sRGB first or it is linearised twice and comes out near black. -xlinka
    public static colorHDR MeshTint(in color linear)
        => new colorHDR(ToSrgb(linear.r), ToSrgb(linear.g), ToSrgb(linear.b), linear.a);

    private static float ToSrgb(float c)
    {
        if (c <= 0f) return 0f;
        if (c >= 1f) return 1f;
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    // Ground. Backdrop is the dash body behind everything; Panel is a screen's own background;
    // Surface is a card or row sitting on a panel; Field is a sunken well (inputs, image slots).
    public static readonly color Backdrop = S(0.075f, 0.071f, 0.110f, 0.97f);
    public static readonly color Panel = S(0.110f, 0.104f, 0.157f, 1f);
    public static readonly color Surface = S(0.153f, 0.145f, 0.216f, 1f);
    // A canvas root over a glass bezel. Mostly panel colour: the blend is linear, so even a few percent of
    // a bright sky lifts the dark ground a long way, and at 0.42 a panel in daylight went grey and its
    // text washed out with it. What is left of the glass reads as a faint frost.
    public static readonly color GlassBackdrop = new color(Panel.r, Panel.g, Panel.b, 0.88f);
    public static readonly color SurfaceHover = S(0.196f, 0.186f, 0.275f, 1f);
    public static readonly color SurfacePressed = S(0.235f, 0.224f, 0.325f, 1f);
    public static readonly color Field = S(0.086f, 0.082f, 0.125f, 1f);

    // Glass. The dash is captured onto a clear background and laid over a blurred, darkened view of the
    // world, so whatever these leave uncovered is the world behind the dash. The veil is the dash body, thin
    // enough that the empty desk is plainly the room; windows carry the most colour because they carry the
    // most text; the chrome sits between; cards on the desk let a little more through than a window. The
    // composite darkens the room under each in step with how much it covers. -xlinka
    public static readonly color Veil = new color(Backdrop.r, Backdrop.g, Backdrop.b, 0.15f);
    public static readonly color GlassWindow = new color(Panel.r, Panel.g, Panel.b, 0.84f);
    public static readonly color GlassChrome = new color(Panel.r, Panel.g, Panel.b, 0.74f);
    public static readonly color GlassCard = new color(Surface.r, Surface.g, Surface.b, 0.66f);
    public static readonly color GlassCardHover = new color(SurfaceHover.r, SurfaceHover.g, SurfaceHover.b, 0.78f);
    public static readonly color GlassCardPressed = new color(SurfacePressed.r, SurfacePressed.g, SurfacePressed.b, 0.86f);
    // A modal only darkens the UI behind it; a coloured fill can brighten the darkest surfaces.
    public static readonly color ModalScrim = new color(0f, 0f, 0f, 0.62f);

    // Edges are hairlines of white, never a second colored border around a colored panel. The alphas
    // are lower than a design tool would show because S() only fixes the color channels: the blend
    // itself happens in linear, where 7% white over a near-black surface lands at sRGB 0.32, not the
    // 0.21 you would get on a normal compositor. 3% gets the line back to about that. -xlinka
    public static readonly color Outline = S(1f, 1f, 1f, 0.03f);
    public static readonly color OutlineStrong = S(1f, 1f, 1f, 0.06f);
    public static readonly color Divider = S(1f, 1f, 1f, 0.025f);

    public static readonly color Text = S(0.949f, 0.945f, 0.973f, 1f);
    public static readonly color TextDim = S(0.663f, 0.651f, 0.761f, 1f);
    public static readonly color TextMuted = S(0.451f, 0.439f, 0.561f, 1f);
    // Label color over a light fill (Positive, Warning, a bright thumbnail scrim).
    public static readonly color TextOnLight = S(0.075f, 0.071f, 0.110f, 1f);

    // Accent is for the primary action and the selected state. If everything is accent, nothing is.
    public static readonly color Accent = S(0.545f, 0.486f, 0.965f, 1f);
    public static readonly color AccentHover = S(0.616f, 0.565f, 0.980f, 1f);
    public static readonly color AccentPressed = S(0.470f, 0.416f, 0.900f, 1f);
    public static readonly color AccentSoft = S(0.545f, 0.486f, 0.965f, 0.18f);
    public static readonly color OnAccent = color.White;

    public static readonly color Positive = S(0.239f, 0.839f, 0.549f, 1f);
    public static readonly color PositiveHover = S(0.325f, 0.878f, 0.612f, 1f);
    public static readonly color Warning = S(0.961f, 0.710f, 0.290f, 1f);
    public static readonly color Negative = S(0.941f, 0.333f, 0.420f, 1f);

    // Level meters. The bed is Positive taken most of the way down to the ground so a bar at rest still
    // says which colour it moves in, and the peak marker is a step above the fill so it stays readable
    // sitting on top of it. -xlinka
    public static readonly color MeterTrack = S(0.055f, 0.176f, 0.125f, 1f);
    public static readonly color MeterPeak = S(0.596f, 0.961f, 0.749f, 1f);

    // Icon tiles on the home cards: one hue per kind of thing, so a column reads by colour before it
    // reads by word.
    public static readonly color IconBlue = S(0.290f, 0.520f, 0.985f, 1f);
    public static readonly color IconTeal = S(0.145f, 0.785f, 0.745f, 1f);
    public static readonly color IconPink = S(0.965f, 0.330f, 0.600f, 1f);
    public static readonly color NegativeHover = S(0.965f, 0.420f, 0.498f, 1f);

    // Platform identity, used where users are counted by the machine they are on. Blue/yellow/green read
    // apart at dot size and at 2 degrees of ring, which is all the donut ever gives them; anything the
    // client does not recognise falls back to TextMuted rather than borrowing a platform's color. -xlinka
    public static readonly color PlatformWindows = S(0.33f, 0.55f, 0.98f, 1f);
    public static readonly color PlatformLinux = S(0.96f, 0.78f, 0.22f, 1f);
    public static readonly color PlatformAndroid = S(0.30f, 0.84f, 0.52f, 1f);

    public static readonly color ModeBuilder = S(0.298f, 0.620f, 0.980f, 1f);
    public static readonly color ModeSocial = S(0.298f, 0.800f, 0.549f, 1f);
    public static readonly color ModeEvent = S(0.800f, 0.451f, 0.950f, 1f);

    public const float RadiusPanel = 16f;
    public const float RadiusCard = 12f;
    public const float RadiusControl = 8f;
    public const float RadiusChip = 6f;
    public const float OutlineWidth = 1f;

    public const float FontDisplay = 26f;
    public const float FontTitle = 20f;
    public const float FontHeading = 17f;
    public const float FontBody = 15f;
    public const float FontSmall = 13f;
    public const float FontLabel = 12f;

    public const float Gap = 8f;
    public const float GapLarge = 16f;
    public const float Inset = 20f;
    public const float ControlHeight = 34f;
    public const float ChipHeight = 22f;

    // Lato for everything people read, Fira Code only where fixed width earns its keep (debug, code,
    // numbers that need to line up). Both ship under the OFL in Assets/Fonts.
    public static readonly Uri FontRegular = new("res://Assets/Fonts/Lato/Lato-Regular.ttf");
    public static readonly Uri FontSemibold = new("res://Assets/Fonts/Lato/Lato-Semibold.ttf");
    public static readonly Uri FontBold = new("res://Assets/Fonts/Lato/Lato-Bold.ttf");
    public static readonly Uri FontMono = new("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

    public static color ModeTint(WorldMode mode) => mode switch
    {
        WorldMode.Social => ModeSocial,
        WorldMode.Event => ModeEvent,
        _ => ModeBuilder,
    };

    public static string ModeLabel(WorldMode mode) => mode switch
    {
        WorldMode.Social => "Social",
        WorldMode.Event => "Event",
        _ => "Builder",
    };
}
