// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Scripting;
using static Lumora.Core.Scripting.ShaderGraphBuilder;

namespace Lumora.Core.Templates;

// The shader gallery's materials, every one of them a shader graph built node by node: what the gallery
// shows is what the node editor makes, and opening any orb's graph shows how. This half holds the sky and
// the worlds in it; the effects are in ShaderGalleryEffects. -xlinka
internal static partial class ShaderGalleryGraphs
{
    private const double Tau = System.Math.PI * 2.0;

    // How far each triplanar plane is pushed by the side of its axis it faces.
    private const double PlaneShift = 3.7;

    // ---- pieces the round worlds share ----

    // The point of a UV sphere a UV came from, by the sphere mesh's own formula. On a card's flat preview
    // the same nodes unroll the whole ball into a map, so every card shows the world it is part of.
    private static string SphereDirection(ShaderGraphBuilder g, string p)
    {
        g.Node(p + "UV", "UV");
        g.Node(p + "UVs", "SeparateXYZ", "float2", ("vector", p + "UV"));
        g.Mul(p + "Lon", p + "UVs.x", Tau);
        g.Mul(p + "Colat", p + "UVs.y", System.Math.PI);
        g.Unary(p + "CosLon", "Cosine", p + "Lon");
        g.Unary(p + "SinLon", "Sine", p + "Lon");
        g.Unary(p + "SinCo", "Sine", p + "Colat");
        g.Unary(p + "CosCo", "Cosine", p + "Colat");
        g.Mul(p + "X", p + "SinCo", p + "CosLon");
        g.Mul(p + "ZUp", p + "SinCo", p + "SinLon");
        g.Unary(p + "Z", "Negate", p + "ZUp");
        return g.Node(p, "CombineXYZ", ("x", p + "X"), ("y", p + "CosCo"), ("z", p + "Z"));
    }

    // How squarely a direction faces each axis, sharpened, the three summing to one.
    private static string Weights(ShaderGraphBuilder g, string p, string direction)
    {
        g.Node(p + "Abs", "Absolute", "float3", ("value", direction));
        g.Node(p + "Pow", "Power", "float3,float3", ("base", p + "Abs"), ("exponent", V3(4, 4, 4)));
        g.Node(p + "Sum", "DotProduct", ("a", p + "Pow"), ("b", V3(1, 1, 1)));
        g.Node(p + "Div", "Divide", "float3,float3", ("a", p + "Pow"), ("b", p + "Sum"));
        return g.Node(p, "SeparateXYZ", ("vector", p + "Div"));
    }

    private static void Planes(ShaderGraphBuilder g, string p, string parts)
    {
        Plane(g, p + "X", parts, "y", "z", "x");
        Plane(g, p + "Y", parts, "x", "z", "y");
        Plane(g, p + "Z", parts, "x", "y", "z");
    }

    // One plane's reading, pushed along by the side of the axis it faces, so the two sides of the ball read
    // different ground instead of mirror images. The side and not the distance: a distance stretches it.
    private static void Plane(ShaderGraphBuilder g, string id, string parts, string a, string b, string axis)
    {
        g.Unary(id + "Side", "Sign", $"{parts}.{axis}");
        g.Mul(id + "Shift", id + "Side", PlaneShift);
        g.Add(id + "U", $"{parts}.{a}", id + "Shift");
        g.Pair(id, id + "U", $"{parts}.{b}");
    }

    // A 2D pattern read from the three planes and blended by the weights: no seam, no pinch at the poles.
    private static string Triplanar(ShaderGraphBuilder g, string p, string type, string planes, string weights, string output,
        params (string Port, In Value)[] tune)
    {
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            var inputs = new List<(string, In)> { ("uv", planes + axis) };
            inputs.AddRange(tune);
            g.Node(p + axis, type, inputs.ToArray());
            g.Mul(p + "W" + axis, $"{p}{axis}.{output}", $"{weights}.{axis.ToLowerInvariant()}");
        }
        g.Add(p + "XY", p + "WX", p + "WY");
        return g.Add(p + "Sum", p + "XY", p + "WZ");
    }

    private static In Direction(float3 v) => V3(v.x, v.y, v.z);

    // ---- the living planet ----

    // Warped continents, biomes by moisture and height, relief in the mountains, ice caps, clouds turning
    // faster than the ground, an atmosphere, and on the night side cities and aurora. The ground turns
    // under a sun that stays where the world's light is.
    public static void Planet(ShaderGraphBuilder g, float3 toSun)
    {
        SphereDirection(g, "dir");

        g.Node("time", "Time");
        g.Float("daySpeed", "Day Speed", 0.03, 0, 0.5);
        g.Mul("spin", "time", "daySpeed");
        g.Node("ground", "RotateAboutAxis", ("vector", "dir"), ("radians", "spin"));
        Weights(g, "w", "ground");
        g.Node("d", "SeparateXYZ", ("vector", "ground"));
        Planes(g, "plane", "d");

        // A slow noise pushes the point before the continents read it, so coasts wander.
        var warp = Triplanar(g, "warp", "GradientNoise", "plane", "w", "noise", ("scale", 2.4));
        g.Sub("warpC", warp, 0.5);
        g.Float("warpAmt", "Warp", 0.3, 0, 1);
        g.Mul("warpS", "warpC", "warpAmt");
        g.Node("warped", "Add", "float3,float3", ("a", "ground"), ("b", "warpS"));
        g.Node("wd", "SeparateXYZ", ("vector", "warped"));
        Planes(g, "wPlane", "wd");

        var height = Triplanar(g, "land", "NoiseTexture", "wPlane", "w", "noise", ("scale", 1.9), ("detail", 5.0), ("roughness", 0.5));
        g.Map("heightC", height, 0.3, 0.7, 0, 1);
        g.Unary("heightS", "Saturate", "heightC");

        // The sea: a soft coastline, depth below it, elevation above it.
        g.Float("seaLevel", "Sea Level", 0.55, 0, 1);
        g.Add("coastHi", "seaLevel", 0.012);
        g.Smooth("land", "heightS", "seaLevel", "coastHi");
        g.Sub("depth", "seaLevel", "heightS");
        g.Smooth("depthN", "depth", 0, 0.35);
        g.Map("elev", "heightS", "coastHi", 1, 0, 1);
        g.Unary("elevS", "Saturate", "elev");
        g.ColorProperty("shallow", "Shallow Water", 0.05, 0.42, 0.55);
        g.ColorProperty("deep", "Deep Water", 0.01, 0.045, 0.16);
        g.MixColors("ocean", "shallow", "deep", "depthN");

        // Moisture picks desert, grassland or forest; height picks rock and snow; sand at the coast.
        var moisture = Triplanar(g, "moist", "GradientNoise", "plane", "w", "noise", ("scale", 3.3));
        g.Color("desert", 0.74, 0.58, 0.33);
        g.Color("grass", 0.22, 0.45, 0.13);
        g.Color("forest", 0.05, 0.22, 0.07);
        g.Color("rock", 0.35, 0.30, 0.26);
        g.Color("snow", 0.96, 0.97, 1.0);
        g.Color("sand", 0.86, 0.79, 0.58);
        g.Smooth("wet", moisture, 0.38, 0.58);
        g.MixColors("lowland", "desert", "grass", "wet");
        g.Smooth("jungle", moisture, 0.55, 0.72);
        g.Smooth("upland", "elevS", 0.3, 0.55);
        g.Unary("lowOnly", "OneMinus", "upland");
        g.Mul("forestAmt", "jungle", "lowOnly");
        g.MixColors("forested", "lowland", "forest", "forestAmt");
        g.Smooth("rockAmt", "elevS", 0.45, 0.72);
        g.MixColors("rocky", "forested", "rock", "rockAmt");
        g.Smooth("snowAmt", "elevS", 0.8, 0.93);
        g.MixColors("snowy", "rocky", "snow", "snowAmt");
        g.Smooth("beachAmt", "elevS", 0.0, 0.05);
        g.MixColors("beachy", "sand", "snowy", "beachAmt");

        // Relief: a fine noise shading the land, strongest in the mountains.
        var ridges = Triplanar(g, "ridge", "NoiseTexture", "wPlane", "w", "noise", ("scale", 9.0), ("detail", 4.0), ("roughness", 0.55));
        g.Sub("ridgeC", ridges, 0.5);
        g.Mix("reliefAmt", 0.35, 1.2, "elevS");
        g.Mul("relief", "ridgeC", "reliefAmt");
        g.Add("shade", "relief", 1.0);
        g.Node("landLit", "Multiply", "color,color", ("a", "beachy"), ("b", "shade"));
        g.MixColors("terrain", "ocean", "landLit", "land");

        // Ice caps from latitude, their edge roughed up by the moisture noise.
        g.Unary("lat", "Absolute", "d.y");
        g.Sub("jitter", moisture, 0.5);
        g.Mul("jitterS", "jitter", 0.25);
        g.Add("latJ", "lat", "jitterS");
        g.Float("iceLine", "Ice Line", 0.9, 0, 1);
        g.Add("iceHi", "iceLine", 0.04);
        g.Smooth("ice", "latJ", "iceLine", "iceHi");
        g.Color("iceColor", 0.9, 0.95, 1.0);
        g.MixColors("iced", "terrain", "iceColor", "ice");

        // Clouds turn about the pole on their own clock, with weights of their own so the blend follows them.
        g.Float("cloudSpeed", "Cloud Speed", 0.05, 0, 0.5);
        g.Mul("cloudSpin", "time", "cloudSpeed");
        g.Node("cloudDir", "RotateAboutAxis", ("vector", "dir"), ("radians", "cloudSpin"));
        Weights(g, "cw", "cloudDir");
        g.Node("cd", "SeparateXYZ", ("vector", "cloudDir"));
        Planes(g, "cPlane", "cd");
        var cloudNoise = Triplanar(g, "cloud", "NoiseTexture", "cPlane", "cw", "noise", ("scale", 3.0), ("detail", 6.0), ("roughness", 0.62));
        g.Float("cover", "Cloud Cover", 0.5, 0, 1);
        g.Map("cloudLo", "cover", 0, 1, 0.66, 0.34);
        g.Add("cloudHi", "cloudLo", 0.12);
        g.Smooth("clouds", cloudNoise, "cloudLo", "cloudHi");
        g.Color("cloudColor", 0.97, 0.98, 1.0);
        g.Mul("cloudOpacity", "clouds", 0.92);
        g.MixColors("clouded", "iced", "cloudColor", "cloudOpacity");

        // Day and night from the sun's direction, the same one lighting the world.
        g.Node("sunDir", "VectorProperty", ("name", "Sun Direction"), ("value", Direction(toSun)));
        g.Node("ndotl", "DotProduct", ("a", "dir"), ("b", "sunDir"));
        g.Smooth("dusk", "ndotl", -0.2, 0.12);
        g.Unary("night", "OneMinus", "dusk");

        // Haze toward the rim, and at the very rim a glow that is sunlit, warm at the terminator.
        g.Node("fresnel", "Fresnel", ("power", 2.4));
        g.ColorProperty("atmoColor", "Atmosphere", 0.32, 0.6, 1.0);
        g.Mul("haze", "fresnel", 0.3);
        g.MixColors("hazed", "clouded", "atmoColor", "haze");
        g.Node("rimFresnel", "Fresnel", ("power", 6.0));
        g.Float("atmoGlow", "Atmosphere Glow", 2.2, 0, 4);
        g.Mul("terminator", "dusk", "night");
        g.Mul("twilightAmt", "terminator", 1.5);
        g.Color("twilightColor", 1.0, 0.62, 0.38);
        g.MixColors("skyTint", "atmoColor", "twilightColor", "twilightAmt");
        g.Mul("rim", "rimFresnel", "atmoGlow");
        g.Add("sunlit", "dusk", 0.1);
        g.Mul("rimLit", "rim", "sunlit");
        g.Node("rimGlow", "Multiply", "color,color", ("a", "skyTint"), ("b", "rimLit"));

        // The night side darker than the world's sky light leaves it, so what glows there reads.
        g.Mix("nightShade", 0.14, 1.0, "dusk");
        g.Node("shaded", "Multiply", "color,color", ("a", "hazed"), ("b", "nightShade"));

        // Cities: points of a fine cell pattern where it is lowland, wet enough to live, not iced over, not
        // under cloud, and night.
        var cityDistance = Triplanar(g, "city", "Voronoi", "plane", "w", "distance", ("cellDensity", 20.0), ("angleOffset", 3.0));
        g.Smooth("dotsRaw", cityDistance, 0.02, 0.18);
        g.Unary("dots", "OneMinus", "dotsRaw");
        g.Smooth("pop", moisture, 0.42, 0.62);
        g.Mul("popLow", "pop", "lowOnly");
        g.Mul("lit1", "dots", "popLow");
        g.Mul("lit2", "lit1", "land");
        g.Unary("noIce", "OneMinus", "ice");
        g.Mul("lit3", "lit2", "noIce");
        g.Unary("clear", "OneMinus", "clouds");
        g.Mul("lit4", "lit3", "clear");
        g.Mul("lit5", "lit4", "night");
        g.ColorProperty("cityColor", "City Lights", 1.0, 0.7, 0.3);
        g.Mul("cityBright", "lit5", 7.0);
        g.Node("cityGlow", "Multiply", "color,color", ("a", "cityColor"), ("b", "cityBright"));

        // Aurora: a ring round each ice cap on the night side, rippled by the drifting cloud noise.
        g.Smooth("auroraLo", "lat", 0.82, 0.87);
        g.Smooth("auroraHi", "lat", 0.91, 0.95);
        g.Unary("auroraIn", "OneMinus", "auroraHi");
        g.Mul("auroraRing", "auroraLo", "auroraIn");
        g.Smooth("auroraWave", cloudNoise, 0.5, 0.58);
        g.Mul("auroraShape", "auroraRing", "auroraWave");
        g.Mul("auroraNight", "auroraShape", "night");
        g.Mul("auroraBright", "auroraNight", 1.3);
        g.ColorProperty("auroraColor", "Aurora", 0.25, 1.0, 0.55);
        g.Node("auroraGlow", "Multiply", "color,color", ("a", "auroraColor"), ("b", "auroraBright"));

        g.Node("glow", "Add", "color,color", ("a", "rimGlow"), ("b", "cityGlow"));
        g.Node("glowAll", "Add", "color,color", ("a", "glow"), ("b", "auroraGlow"));

        // The sea glossy, land rough, ice between, cloud matte.
        g.Mix("roughTerrain", 0.22, 0.9, "land");
        g.Mix("roughIce", "roughTerrain", 0.4, "ice");
        g.Mix("roughCloud", "roughIce", 0.95, "clouds");

        g.Into("output", ("baseColor", "shaded"), ("emission", "glowAll"), ("roughness", "roughCloud"),
            ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // A shell a little bigger than the planet, glowing where it rises past the planet's edge: brightest
    // just outside the edge, nothing at the shell's own edge, a little over the disc. Blue by day, red
    // where the sun is setting, dark at night.
    public static void Atmosphere(ShaderGraphBuilder g, float3 toSun, double shellRatio)
    {
        // Facing (N.V) the shell has where the planet's edge shows through it.
        double limb = System.Math.Sqrt(1.0 - 1.0 / (shellRatio * shellRatio));
        g.Node("fresnel", "Fresnel", ("power", 1.0));
        g.Unary("facing", "OneMinus", "fresnel");
        g.Smooth("outer", "facing", 0.0, limb);
        g.Pow("outerSharp", "outer", 2.2);
        g.Smooth("inner", "facing", limb, limb + 0.3);
        g.Mix("innerFade", 1.0, 0.05, "inner");
        g.Mul("profile", "outerSharp", "innerFade");

        g.Node("normal", "Normal", ("space", "World"));
        g.Node("sunDir", "VectorProperty", ("name", "Sun Direction"), ("value", Direction(toSun)));
        g.Node("ndotl", "DotProduct", ("a", "normal"), ("b", "sunDir"));
        g.Smooth("lit", "ndotl", -0.3, 0.1);
        g.Smooth("day", "ndotl", -0.05, 0.35);
        g.ColorProperty("sky", "Sky", 0.35, 0.62, 1.0);
        g.ColorProperty("sunset", "Sunset", 1.0, 0.45, 0.2);
        g.MixColors("tint", "sunset", "sky", "day");
        g.Float("strength", "Glow", 2.0, 0, 8);
        g.Mul("amount", "profile", "lit");
        g.Mul("amountS", "amount", "strength");
        g.Node("glow", "Multiply", "color,color", ("a", "tint"), ("b", "amountS"));

        g.Into("output", ("baseColor", "glow"), ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Back"));
    }

    // A moon of dark seas and craters at two sizes: a bowl darkening each middle, a rim the light catches.
    public static void Moon(ShaderGraphBuilder g)
    {
        SphereDirection(g, "dir");
        Weights(g, "w", "dir");
        g.Node("d", "SeparateXYZ", ("vector", "dir"));
        Planes(g, "plane", "d");

        var seas = Triplanar(g, "sea", "GradientNoise", "plane", "w", "noise", ("scale", 1.3));
        g.Smooth("mare", seas, 0.52, 0.62);

        var big = Triplanar(g, "big", "Voronoi", "plane", "w", "distance", ("cellDensity", 3.5), ("angleOffset", 1.0));
        var small = Triplanar(g, "small", "Voronoi", "plane", "w", "distance", ("cellDensity", 11.0), ("angleOffset", 4.0));
        Crater(g, "bigC", big, 0.3);
        Crater(g, "smallC", small, 0.22);

        // Rims bright, bowls dark. Shading by tone rather than a bumped normal: a bump works from how the
        // height changes between pixels and at this size it comes out in blocks.
        g.Sub("bigH", "bigCRim", "bigCBowl");
        g.Sub("smallH", "smallCRim", "smallCBowl");

        g.Color("highland", 0.68, 0.68, 0.67);
        g.Color("lowland", 0.3, 0.3, 0.32);
        g.MixColors("ground", "highland", "lowland", "mare");
        g.Mul("toneBig", "bigH", 0.25);
        g.Mul("toneSmall", "smallH", 0.18);
        g.Add("toneSum", "toneBig", "toneSmall");
        g.Add("tone", "toneSum", 1.0);
        g.Node("albedo", "Multiply", "color,color", ("a", "ground"), ("b", "tone"));

        g.Into("output", ("baseColor", "albedo"), ("roughness", 0.95),
            ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // A crater from a cell distance: a bowl darkening its middle, a rim round its edge.
    private static void Crater(ShaderGraphBuilder g, string p, string distance, double radius)
    {
        g.Smooth(p + "In", distance, radius * 0.15, radius);
        g.Unary(p + "Bowl", "OneMinus", p + "In");
        g.Smooth(p + "RimUp", distance, radius * 0.75, radius);
        g.Smooth(p + "RimDown", distance, radius, radius * 1.4);
        g.Unary(p + "RimOut", "OneMinus", p + "RimDown");
        g.Mul(p + "Rim", p + "RimUp", p + "RimOut");
    }

    // ---- the gas giant ----

    // Banded clouds whose edges churn, neighbouring bands sliding against each other, and a great storm the
    // bands swirl round. The churn is read off the cosine and the sine of the longitude, both continuous all
    // the way round, so there is no seam where the sphere's UV closes.
    public static void GasGiant(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("uvs", "SeparateXYZ", "float2", ("vector", "uv"));
        g.Node("time", "Time");

        g.Mul("shearLat", "uvs.y", 23.0);
        g.Unary("shear", "Sine", "shearLat");
        g.Mul("swayT", "time", 0.07);
        g.Unary("sway", "Sine", "swayT");
        g.Float("wind", "Wind", 0.5, 0, 2);
        g.Mul("swayS", "sway", "wind");
        g.Mul("drift", "shear", "swayS");
        g.Mul("lonBase", "uvs.x", Tau);
        g.Add("lon", "lonBase", "drift");

        g.Unary("cosLon", "Cosine", "lon");
        g.Unary("sinLon", "Sine", "lon");
        g.Mul("latS", "uvs.y", 11.0);
        g.Mul("cosS", "cosLon", 2.2);
        g.Mul("sinS", "sinLon", 2.2);
        g.Pair("qa", "cosS", "latS");
        g.Pair("qb", "sinS", "latS");
        g.Node("na", "NoiseTexture", ("uv", "qa"), ("scale", 1.0), ("detail", 5.0), ("roughness", 0.6));
        g.Node("nb", "NoiseTexture", ("uv", "qb"), ("scale", 1.0), ("detail", 5.0), ("roughness", 0.6));
        g.Add("nab", "na", "nb");
        g.Sub("turb", "nab", 1.0);
        g.Float("turbAmt", "Turbulence", 0.045, 0, 0.2);
        g.Mul("turbS", "turb", "turbAmt");

        // The storm: inside it the bands are read from a twirled UV.
        g.Node("swirl", "Twirl", ("uv", "uv"), ("center", V2(0.32, 0.62)), ("strength", 40.0));
        g.Node("swirls", "SeparateXYZ", "float2", ("vector", "swirl"));
        g.Node("toSpot", "Subtract", "float2,float2", ("a", "uv"), ("b", V2(0.32, 0.62)));
        g.Node("spotDist", "Length", "float2", ("vector", "toSpot"));
        g.Smooth("spotOut", "spotDist", 0.03, 0.09);
        g.Unary("spotIn", "OneMinus", "spotOut");
        g.Mix("bandLatRaw", "uvs.y", "swirls.y", "spotIn");
        g.Add("bandLat", "bandLatRaw", "turbS");

        // Three families of bands over each other.
        g.Mul("b1Phase", "bandLat", 41.0);
        g.Unary("b1Sin", "Sine", "b1Phase");
        g.Map("b1", "b1Sin", -1, 1, 0, 1);
        g.Mul("b2Phase", "bandLat", 17.0);
        g.Add("b2Shift", "b2Phase", 1.7);
        g.Unary("b2Sin", "Sine", "b2Shift");
        g.Map("b2", "b2Sin", -1, 1, 0, 1);
        g.Mul("b3Phase", "bandLat", 6.5);
        g.Unary("b3Sin", "Sine", "b3Phase");
        g.Smooth("b3", "b3Sin", 0.4, 1.0);
        g.Color("cream", 0.97, 0.92, 0.8);
        g.Color("tan", 0.78, 0.55, 0.34);
        g.Color("rust", 0.58, 0.28, 0.15);
        g.Color("slate", 0.55, 0.62, 0.72);
        g.MixColors("c1", "cream", "tan", "b1");
        g.Mul("rustAmt", "b2", 0.8);
        g.MixColors("c2", "c1", "rust", "rustAmt");
        g.Mul("slateAmt", "b3", 0.45);
        g.MixColors("c3", "c2", "slate", "slateAmt");

        g.ColorProperty("storm", "Storm", 0.78, 0.3, 0.17);
        g.Smooth("stormOut", "spotDist", 0.015, 0.05);
        g.Unary("stormCore", "OneMinus", "stormOut");
        g.Mul("stormAmt", "stormCore", 0.85);
        g.MixColors("c4", "c3", "storm", "stormAmt");

        // Darker, bluer poles and a pale haze at the edge.
        g.Sub("fromEq", "uvs.y", 0.5);
        g.Unary("fromEqAbs", "Absolute", "fromEq");
        g.Smooth("polar", "fromEqAbs", 0.3, 0.5);
        g.Color("poleColor", 0.32, 0.36, 0.42);
        g.MixColors("c5", "c4", "poleColor", "polar");
        g.Node("fresnel", "Fresnel", ("power", 3.0));
        g.Mul("hazeAmt", "fresnel", 0.45);
        g.Color("haze", 0.85, 0.88, 0.95);
        g.MixColors("c6", "c5", "haze", "hazeAmt");

        g.Into("output", ("baseColor", "c6"), ("roughness", 0.85), ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // Rings on a flat disc: ringlets from noise along the radius only, a dark gap, soft edges; see-through
    // where they thin, and lit by the sun on whichever face it reaches.
    public static void Rings(ShaderGraphBuilder g, double inner)
    {
        g.Node("uv", "UV");
        g.Node("centred", "Subtract", "float2,float2", ("a", "uv"), ("b", V2(0.5, 0.5)));
        g.Node("len", "Length", "float2", ("vector", "centred"));
        g.Mul("r", "len", 2.0);

        g.Mul("rCoarse", "r", 34.0);
        g.Pair("qCoarse", "rCoarse", 0.5);
        g.Node("coarse", "GradientNoise", ("uv", "qCoarse"), ("scale", 1.0));
        g.Mul("rFine", "r", 260.0);
        g.Pair("qFine", "rFine", 0.5);
        g.Node("fine", "SimpleNoise", ("uv", "qFine"), ("scale", 1.0));
        g.Mul("fineS", "fine", 0.45);
        g.Add("densityRaw", "coarse", "fineS");
        g.Smooth("density", "densityRaw", 0.25, 1.15);

        g.Smooth("gapIn", "r", 0.735, 0.75);
        g.Smooth("gapOut", "r", 0.77, 0.785);
        g.Sub("gap", "gapIn", "gapOut");
        g.Unary("noGap", "OneMinus", "gap");
        g.Smooth("innerEdge", "r", inner, inner + 0.05);
        g.Smooth("outerEdge", "r", 0.95, 0.995);
        g.Unary("outerIn", "OneMinus", "outerEdge");
        g.Mul("a1", "density", "noGap");
        g.Mul("a2", "a1", "innerEdge");
        g.Mul("alpha", "a2", "outerIn");

        g.Color("dust", 0.76, 0.66, 0.5);
        g.Color("ice", 0.96, 0.92, 0.84);
        g.MixColors("ringColor", "dust", "ice", "coarse");
        g.Node("ringGlow", "Multiply", "color,color", ("a", "ringColor"), ("b", 0.3));

        g.Into("output", ("baseColor", "ringColor"), ("alpha", "alpha"), ("roughness", 1.0), ("emission", "ringGlow"),
            ("surface", "Transparent"), ("lighting", "Lit"), ("cull", "Both"));
    }

    // ---- the sky ----

    // The whole sky as one shader on the inside of a dome: night overhead fading to a blue horizon, the
    // sunset glow and the sun where the world's light comes from, two layers of twinkling stars, a milky
    // way with dark rifts and nebulae. The dome is turned so its UV poles lie on the horizon and its seam
    // under the ground; the milky way runs along its equator, over the top of the sky.
    public static void Sky(ShaderGraphBuilder g, float3 toSun)
    {
        g.Node("pos", "Position", ("space", "World"));
        g.Node("centre", "ObjectPosition");
        g.Node("offset", "Subtract", "float3,float3", ("a", "pos"), ("b", "centre"));
        g.Node("look", "Normalize", ("vector", "offset"));
        g.Node("lookParts", "SeparateXYZ", ("vector", "look"));
        g.Smooth("high", "lookParts.y", 0.0, 0.55);
        g.Unary("low", "OneMinus", "high");
        g.ColorProperty("zenith", "Zenith", 0.012, 0.016, 0.045);
        g.ColorProperty("horizon", "Horizon", 0.13, 0.15, 0.28);
        g.MixColors("sky", "horizon", "zenith", "high");

        g.Node("sunDir", "VectorProperty", ("name", "Sun Direction"), ("value", Direction(toSun)));
        g.Node("toward", "DotProduct", ("a", "look"), ("b", "sunDir"));
        g.Unary("towardS", "Saturate", "toward");
        g.Pow("sunWide", "towardS", 3.0);
        g.Mul("sunWideLow", "sunWide", "low");
        g.Pow("sunHalo", "towardS", 60.0);
        g.Mul("haloS", "sunHalo", 1.6);
        g.Step("sunDisc", 0.9993, "toward");
        g.Mul("discS", "sunDisc", 8.0);
        g.Add("sunLight1", "sunWideLow", "haloS");
        g.Add("sunLight", "sunLight1", "discS");
        g.ColorProperty("sunset", "Sunset", 1.0, 0.46, 0.2);
        g.Node("sunGlow", "Multiply", "color,color", ("a", "sunset"), ("b", "sunLight"));

        // Stars: a fine field and a few bright ones, each twinkling at its own pace.
        g.Node("uv", "UV");
        g.Node("starUV", "TilingAndOffset", ("uv", "uv"), ("tiling", V2(2.0, 1.0)));
        g.Node("stars", "Voronoi", ("uv", "starUV"), ("cellDensity", 150.0), ("angleOffset", 2.0));
        g.Smooth("starOut", "stars.distance", 0.0, 0.07);
        g.Unary("starDot", "OneMinus", "starOut");
        g.Step("starPick", 0.55, "stars.cells");
        g.Mul("starOn", "starDot", "starPick");
        g.Node("time", "Time");
        g.Mul("twPhase", "stars.cells", 50.0);
        g.Mul("twT", "time", 2.3);
        g.Add("twArg", "twPhase", "twT");
        g.Unary("twSin", "Sine", "twArg");
        g.Map("twinkle", "twSin", -1, 1, 0.55, 1.0);
        g.Mul("starLit", "starOn", "twinkle");
        g.Node("bright", "Voronoi", ("uv", "starUV"), ("cellDensity", 38.0), ("angleOffset", 5.0));
        g.Smooth("brightOut", "bright.distance", 0.0, 0.045);
        g.Unary("brightDot", "OneMinus", "brightOut");
        g.Step("brightPick", 0.82, "bright.cells");
        g.Mul("brightOn", "brightDot", "brightPick");
        g.Mul("brightS", "brightOn", 3.0);

        // The milky way: a wandering band along the dome's equator, clumped, split by dark rifts.
        g.Node("uvs", "SeparateXYZ", "float2", ("vector", "uv"));
        g.Node("mwNoise", "NoiseTexture", ("uv", "starUV"), ("scale", 3.0), ("detail", 6.0), ("roughness", 0.6));
        g.Sub("mwWarp", "mwNoise", 0.5);
        g.Mul("mwWarpS", "mwWarp", 0.08);
        g.Sub("mwOff", "uvs.y", 0.5);
        g.Add("mwOffW", "mwOff", "mwWarpS");
        g.Unary("mwAbs", "Absolute", "mwOffW");
        g.Smooth("mwEdge", "mwAbs", 0.02, 0.13);
        g.Unary("band", "OneMinus", "mwEdge");
        g.Node("mwDetail", "NoiseTexture", ("uv", "starUV"), ("scale", 9.0), ("detail", 5.0), ("roughness", 0.55));
        g.Mul("mwCloud", "band", "mwDetail");
        g.Smooth("rift", "mwDetail", 0.6, 0.74);
        g.Mul("riftS", "rift", 0.6);
        g.Unary("noRift", "OneMinus", "riftS");
        g.Mul("mwLit", "mwCloud", "noRift");
        g.Pow("mwSoft", "mwLit", 1.6);

        // Haze near the horizon swallows the faint things.
        g.Smooth("clearSky", "lookParts.y", 0.02, 0.3);
        g.Mul("mwSeen", "mwSoft", "clearSky");
        g.Mul("mwS", "mwSeen", 0.9);
        g.ColorProperty("mwColor", "Milky Way", 0.62, 0.58, 0.78);
        g.Node("milky", "Multiply", "color,color", ("a", "mwColor"), ("b", "mwS"));

        g.Mul("bandBoost", "band", 1.5);
        g.Add("boost", "bandBoost", 1.0);
        g.Mul("starsBoosted", "starLit", "boost");
        g.Add("starsAll", "starsBoosted", "brightS");
        g.Mul("starsSeen", "starsAll", "clearSky");

        g.Node("neb", "NoiseTexture", ("uv", "starUV"), ("scale", 1.6), ("detail", 5.0), ("roughness", 0.55));
        g.Smooth("nebMask", "neb", 0.55, 0.78);
        g.Node("nebHue", "GradientNoise", ("uv", "starUV"), ("scale", 2.5));
        g.Color("teal", 0.1, 0.55, 0.6);
        g.Color("magenta", 0.65, 0.15, 0.55);
        g.MixColors("nebColor", "teal", "magenta", "nebHue");
        g.Mul("nebAmt", "nebMask", 0.22);
        g.Mul("nebSeen", "nebAmt", "clearSky");
        g.Node("nebula", "Multiply", "color,color", ("a", "nebColor"), ("b", "nebSeen"));

        g.Node("s1", "Add", "color,color", ("a", "sky"), ("b", "sunGlow"));
        g.Node("s2", "Add", "color,color", ("a", "s1"), ("b", "milky"));
        g.Node("s3", "Add", "color,color", ("a", "s2"), ("b", "nebula"));
        g.Node("s4", "Add", "color,color", ("a", "s3"), ("b", "starsSeen"));

        g.Into("output", ("baseColor", "s4"), ("surface", "Opaque"), ("lighting", "Unlit"), ("cull", "Front"));
    }
}
