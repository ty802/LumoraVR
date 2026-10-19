// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Scripting;
using static Lumora.Core.Scripting.ShaderGraphBuilder;

namespace Lumora.Core.Templates;

// The gallery's effects, each a shader graph like everything else in it. -xlinka
internal static partial class ShaderGalleryGraphs
{
    // Lines at every whole step of a coordinate, a width either side.
    private static string Lines(ShaderGraphBuilder g, string id, string coordinate, double count, double width)
    {
        g.Mul(id + "N", coordinate, count);
        g.Unary(id + "F", "Fraction", id + "N");
        g.Sub(id + "C", id + "F", 0.5);
        g.Unary(id + "A", "Absolute", id + "C");
        return g.Smooth(id, id + "A", 0.5 - width, 0.5);
    }

    // A thin ring at a radius.
    private static string Ring(ShaderGraphBuilder g, string id, string radius, double at, double width)
    {
        g.Sub(id + "Off", radius, at);
        g.Unary(id + "Abs", "Absolute", id + "Off");
        g.Smooth(id + "Out", id + "Abs", width * 0.5, width);
        return g.Unary(id, "OneMinus", id + "Out");
    }

    // ---- lava lake ----

    // Plates of cooling crust drifting on magma: cells of a slowly churning pattern, their edges frayed by
    // noise. The magma's heat flickers and pulses cell by cell, the plates' edges still glow dull red, and
    // the surface swells, the plates riding higher than what they float on.
    public static void Lava(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("time", "Time");
        g.Node("tiled", "TilingAndOffset", ("uv", "uv"), ("tiling", V2(2.4, 2.4)));
        g.Mul("driftX", "time", 0.025);
        g.Mul("driftY", "time", 0.017);
        g.Pair("drift", "driftX", "driftY");
        g.Node("flow", "Add", "float2,float2", ("a", "tiled"), ("b", "drift"));
        g.Node("warpN", "NoiseTexture", ("uv", "flow"), ("scale", 1.6), ("detail", 3.0), ("roughness", 0.5));
        g.Sub("warpC", "warpN", 0.5);
        g.Mul("warpS", "warpC", 0.45);
        g.Node("warped", "Add", "float2,float2", ("a", "flow"), ("b", "warpS"));
        g.Mul("churn", "time", 0.35);
        g.Node("plates", "Voronoi", ("uv", "warped"), ("angleOffset", "churn"), ("cellDensity", 3.2));
        g.Node("edgeN", "NoiseTexture", ("uv", "tiled"), ("scale", 9.0), ("detail", 4.0), ("roughness", 0.55));
        g.Sub("edgeC", "edgeN", 0.5);
        g.Mul("edgeS", "edgeC", 0.18);
        g.Add("edge", "plates.distance", "edgeS");
        g.Smooth("open", "edge", 0.5, 0.58);
        g.Unary("crust", "OneMinus", "open");

        g.Node("heatN", "NoiseTexture", ("uv", "flow"), ("scale", 5.0), ("detail", 4.0), ("roughness", 0.5));
        g.Mul("pulseCell", "plates.cells", 12.0);
        g.Mul("pulseT", "time", 1.7);
        g.Add("pulsePhase", "pulseT", "pulseCell");
        g.Unary("pulseSin", "Sine", "pulsePhase");
        g.Map("pulse", "pulseSin", -1, 1, 0.7, 1.0);
        g.Mul("heatRaw", "heatN", 0.9);
        g.Add("heatBase", "heatRaw", 0.55);
        g.Mul("heatOpen", "open", "heatBase");
        g.Mul("heat", "heatOpen", "pulse");
        g.Color("magmaDeep", 1.0, 0.25, 0.03);
        g.Color("magmaHot", 1.0, 0.82, 0.42);
        g.Smooth("hotAmt", "heat", 0.55, 1.15);
        g.MixColors("magma", "magmaDeep", "magmaHot", "hotAmt");
        g.Float("glowAmt", "Glow", 3.0, 0, 20);
        g.Mul("heatGlow", "heat", "glowAmt");
        g.Node("magmaGlow", "Multiply", "color,color", ("a", "magma"), ("b", "heatGlow"));

        g.Smooth("rimUp", "edge", 0.4, 0.51);
        g.Mul("rim", "rimUp", "crust");
        g.Mul("rimS", "rim", 1.4);
        g.Color("ember", 0.85, 0.12, 0.02);
        g.Node("rimGlow", "Multiply", "color,color", ("a", "ember"), ("b", "rimS"));
        g.Node("emission", "Add", "color,color", ("a", "magmaGlow"), ("b", "rimGlow"));

        g.Color("basalt", 0.075, 0.065, 0.06);
        g.Color("scorched", 0.22, 0.05, 0.02);
        g.MixColors("rock", "basalt", "scorched", "open");
        g.Mix("rough", 0.92, 0.35, "open");

        g.Node("swellN", "GradientNoise", ("uv", "flow"), ("scale", 2.5));
        g.Sub("swellC", "swellN", 0.5);
        g.Mul("swell", "swellC", 0.05);
        g.Mul("plateLift", "crust", 0.03);
        g.Add("height", "swell", "plateLift");
        g.Node("up", "Normal", ("space", "Object"));
        g.Node("lift", "Multiply", "float3,float3", ("a", "up"), ("b", "height"));

        g.Into("output", ("baseColor", "rock"), ("roughness", "rough"), ("emission", "emission"), ("vertexOffset", "lift"),
            ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Both"));
    }

    // ---- hologram ----

    // A wire globe of light: longitude and latitude lines off the sphere's UV, a bright edge, scanlines
    // and a sweep climbing through world height, a flicker, and now and then a slice of it jumping sideways.
    public static void Hologram(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("uvs", "SeparateXYZ", "float2", ("vector", "uv"));
        g.Node("time", "Time");
        Lines(g, "lon", "uvs.x", 24, 0.06);
        Lines(g, "lat", "uvs.y", 12, 0.06);
        g.Sub("fromEq", "uvs.y", 0.5);
        g.Unary("fromEqAbs", "Absolute", "fromEq");
        g.Smooth("nearPole", "fromEqAbs", 0.36, 0.47);
        g.Unary("awayFromPole", "OneMinus", "nearPole");
        g.Mul("lonFaded", "lon", "awayFromPole");
        g.Max("grid", "lonFaded", "lat");

        g.Node("pos", "Position", ("space", "World"));
        g.Node("posParts", "SeparateXYZ", ("vector", "pos"));
        g.Mul("scanN", "posParts.y", 22.0);
        g.Mul("scanT", "time", 0.9);
        g.Sub("scanArg", "scanN", "scanT");
        g.Unary("scanF", "Fraction", "scanArg");
        g.Pow("scan", "scanF", 8.0);
        g.Mul("sweepN", "posParts.y", 0.9);
        g.Mul("sweepT", "time", 0.35);
        g.Sub("sweepArg", "sweepN", "sweepT");
        g.Unary("sweepF", "Fraction", "sweepArg");
        g.Smooth("sweep", "sweepF", 0.88, 1.0);
        g.Node("fresnel", "Fresnel", ("power", 2.2));

        g.Mul("flickT", "time", 14.0);
        g.Pair("flickQ", "flickT", 0.5);
        g.Node("flickN", "SimpleNoise", ("uv", "flickQ"), ("scale", 1.0));
        g.Map("flicker", "flickN", 0, 1, 0.72, 1.08);

        g.Mul("gridS", "grid", 1.4);
        g.Mul("scanS", "scan", 0.35);
        g.Mul("sweepS", "sweep", 0.7);
        g.Add("e1", "gridS", "fresnel");
        g.Add("e2", "e1", "scanS");
        g.Add("e3", "e2", "sweepS");
        g.Add("e4", "e3", 0.05);
        g.Mul("e5", "e4", "flicker");
        g.Float("power", "Intensity", 1.0, 0, 4);
        g.Mul("e6", "e5", "power");
        g.ColorProperty("tint", "Tint", 0.3, 0.85, 1.0);
        g.Node("holo", "Multiply", "color,color", ("a", "tint"), ("b", "e6"));

        // The glitch moves vertices, so it reads the object's own space where they live.
        g.Node("objPos", "Position", ("space", "Object"));
        g.Node("objParts", "SeparateXYZ", ("vector", "objPos"));
        g.Mul("glitchY", "objParts.y", 7.0);
        g.Mul("glitchT", "time", 2.3);
        g.Add("glitchArg", "glitchY", "glitchT");
        g.Unary("glitchSin", "Sine", "glitchArg");
        g.Step("glitchBand", 0.93, "glitchSin");
        g.Mul("glitchNT", "time", 4.0);
        g.Pair("glitchQ", "glitchNT", 0.25);
        g.Node("glitchN", "SimpleNoise", ("uv", "glitchQ"), ("scale", 1.0));
        g.Step("glitchOn", 0.62, "glitchN");
        g.Mul("glitchAmt", "glitchBand", "glitchOn");
        g.Mul("glitchShift", "glitchAmt", 0.05);
        g.Node("glitch", "CombineXYZ", ("x", "glitchShift"), ("y", 0.0), ("z", 0.0));

        g.Into("output", ("baseColor", "holo"), ("vertexOffset", "glitch"),
            ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // The projector's beam on a cone: bright at the lens, gone by the globe, streaked, soft at its sides.
    public static void Beam(ShaderGraphBuilder g, double height)
    {
        g.Node("time", "Time");
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("parts", "SeparateXYZ", ("vector", "pos"));
        g.Map("along", "parts.y", -height * 0.5, height * 0.5, 0, 1);
        g.Unary("fromTop", "OneMinus", "along");
        // Clamped first: at the top edge float error leaves this a hair below zero, a power of that is NaN,
        // and the glow pass spreads a NaN into a bright ring.
        g.Unary("fromTopS", "Saturate", "fromTop");
        g.Pow("fade", "fromTopS", 1.6);

        g.Mul("streakX", "parts.x", 14.0);
        g.Mul("streakT", "time", 0.1);
        g.Add("streakXT", "streakX", "streakT");
        g.Mul("streakZ", "parts.z", 14.0);
        g.Pair("streakQ", "streakXT", "streakZ");
        g.Node("streakN", "GradientNoise", ("uv", "streakQ"), ("scale", 1.0));
        g.Map("streak", "streakN", 0, 1, 0.45, 1.15);

        g.Node("fresnel", "Fresnel", ("power", 1.5));
        g.Unary("core", "OneMinus", "fresnel");
        g.Mul("b1", "fade", "streak");
        g.Mul("b2", "b1", "core");
        g.Mul("b3", "b2", 0.55);
        g.ColorProperty("tint", "Tint", 0.3, 0.85, 1.0);
        g.Node("beam", "Multiply", "color,color", ("a", "tint"), ("b", "b3"));

        g.Into("output", ("baseColor", "beam"), ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // ---- energy shield ----

    // A dome of light: a lattice of slices along each axis, seamless all over; a shimmer climbing it; a
    // bright edge; a glowing foot where it meets the floor; and hits landing somewhere new each pulse,
    // a flash and a ripple spreading out from each.
    public static void Shield(ShaderGraphBuilder g, double radius)
    {
        g.Node("time", "Time");
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("cellsP", "Multiply", "float3,float3", ("a", "pos"), ("b", V3(4.2, 4.2, 4.2)));
        g.Node("cellsF", "Fraction", "float3", ("value", "cellsP"));
        g.Node("cellsC", "Subtract", "float3,float3", ("a", "cellsF"), ("b", V3(0.5, 0.5, 0.5)));
        g.Node("cellsA", "Absolute", "float3", ("value", "cellsC"));
        g.Node("cellsL", "Smoothstep", "float3,float,float", ("value", "cellsA"), ("edge0", 0.43), ("edge1", 0.5));
        g.Node("cellsParts", "SeparateXYZ", ("vector", "cellsL"));
        g.Max("latXY", "cellsParts.x", "cellsParts.y");
        g.Max("lattice", "latXY", "cellsParts.z");

        g.Node("p", "SeparateXYZ", ("vector", "pos"));
        g.Add("shU", "p.x", "p.z");
        g.Mul("shT", "time", 0.45);
        g.Sub("shV", "p.y", "shT");
        g.Pair("shQ", "shU", "shV");
        g.Node("shN", "GradientNoise", ("uv", "shQ"), ("scale", 3.0));
        g.Map("shimmer", "shN", 0, 1, 0.4, 1.3);

        // Each pulse a new spot on the dome: round by the golden angle, up and down by a sine.
        g.Mul("pulseT", "time", 0.45);
        g.Unary("pulseN", "Floor", "pulseT");
        g.Unary("phase", "Fraction", "pulseT");
        g.Mul("hitAz", "pulseN", 2.39996);
        g.Mul("hitElSeed", "pulseN", 1.7);
        g.Unary("hitElSin", "Sine", "hitElSeed");
        g.Map("hitEl", "hitElSin", -1, 1, 0.25, 1.1);
        g.Unary("cosEl", "Cosine", "hitEl");
        g.Unary("sinEl", "Sine", "hitEl");
        g.Unary("sinAz", "Sine", "hitAz");
        g.Unary("cosAz", "Cosine", "hitAz");
        g.Mul("hx", "sinAz", "cosEl");
        g.Mul("hz", "cosAz", "cosEl");
        g.Node("hitDir", "CombineXYZ", ("x", "hx"), ("y", "sinEl"), ("z", "hz"));
        g.Node("hit", "Multiply", "float3,float3", ("a", "hitDir"), ("b", V3(radius, radius, radius)));
        g.Node("hitDist", "Distance", ("a", "pos"), ("b", "hit"));
        g.Mul("waveR", "phase", radius * 1.6);
        g.Sub("waveOff", "hitDist", "waveR");
        g.Unary("waveAbs", "Absolute", "waveOff");
        g.Smooth("waveOut", "waveAbs", 0.0, 0.14);
        g.Unary("wave", "OneMinus", "waveOut");
        g.Unary("fadeIn", "OneMinus", "phase");
        g.Pow("fade", "fadeIn", 1.5);
        g.Mul("ripple", "wave", "fade");
        g.Smooth("flashOut", "hitDist", 0.0, 0.35);
        g.Unary("flashIn", "OneMinus", "flashOut");
        g.Pow("flashFade", "fadeIn", 4.0);
        g.Mul("flash", "flashIn", "flashFade");
        g.Add("impactRaw", "ripple", "flash");
        g.Mul("impact", "impactRaw", 1.6);

        g.Node("fresnel", "Fresnel", ("power", 2.5));
        g.Smooth("footOut", "p.y", 0.0, 0.12);
        g.Unary("foot", "OneMinus", "footOut");
        g.Mul("latticeS", "lattice", "shimmer");
        g.Mul("latticeD", "latticeS", 0.32);
        g.Add("i1", "latticeD", "fresnel");
        g.Add("i2", "i1", "impact");
        g.Mul("footS", "foot", 1.4);
        g.Add("i3", "i2", "footS");
        g.Add("i4", "i3", 0.04);
        g.Float("power", "Power", 1.2, 0, 4);
        g.Mul("i5", "i4", "power");
        g.ColorProperty("energy", "Energy", 0.25, 0.65, 1.0);
        g.Color("hot", 0.85, 0.95, 1.0);
        g.Unary("impactC", "Saturate", "impact");
        g.MixColors("tint", "energy", "hot", "impactC");
        g.Node("shield", "Multiply", "color,color", ("a", "tint"), ("b", "i5"));

        g.Into("output", ("baseColor", "shield"), ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // ---- dissolve ----

    // Gold burning away across the object and growing back: a sweep through its own space plus noise picks
    // what goes first, a white-hot band runs along the cut, and the metal chars just behind it.
    public static void Dissolve(ShaderGraphBuilder g)
    {
        g.Node("time", "Time");
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("p", "SeparateXYZ", ("vector", "pos"));
        g.Pair("qa", "p.x", "p.y");
        g.Pair("qb", "p.z", "p.y");
        g.Node("na", "NoiseTexture", ("uv", "qa"), ("scale", 4.0), ("detail", 4.0), ("roughness", 0.55));
        g.Node("nb", "NoiseTexture", ("uv", "qb"), ("scale", 4.0), ("detail", 4.0), ("roughness", 0.55));
        g.Add("nab", "na", "nb");
        g.Mul("noiseS", "nab", 0.25);
        g.Map("sweep", "p.x", -0.7, 0.7, 0, 1);
        g.Mul("sweepS", "sweep", 0.5);
        g.Add("mask", "sweepS", "noiseS");

        g.Float("speed", "Speed", 0.45, 0, 2);
        g.Mul("cycleT", "time", "speed");
        g.Unary("cycleSin", "Sine", "cycleT");
        g.Map("cut", "cycleSin", -1, 1, -0.1, 1.1);
        g.Float("width", "Edge Width", 0.06, 0.005, 0.3);
        g.Step("keep", "cut", "mask");
        g.Add("cutEdge", "cut", "width");
        g.Step("beyond", "cutEdge", "mask");
        g.Sub("band", "keep", "beyond");
        g.Sub("intoBand", "mask", "cut");
        g.Div("bandPos", "intoBand", "width");
        g.Unary("bandHot", "OneMinus", "bandPos");
        g.Unary("bandHotS", "Saturate", "bandHot");
        g.Color("flame", 1.0, 0.32, 0.04);
        g.Color("whiteHot", 1.0, 0.9, 0.62);
        g.MixColors("edgeColor", "flame", "whiteHot", "bandHotS");
        g.Float("edgeGlow", "Edge Glow", 9.0, 0, 30);
        g.Mul("bandGlow", "band", "edgeGlow");
        g.Node("emission", "Multiply", "color,color", ("a", "edgeColor"), ("b", "bandGlow"));

        g.Mul("charFar", "width", 5.0);
        g.Add("charEnd", "cut", "charFar");
        g.Smooth("charOut", "mask", "cutEdge", "charEnd");
        g.Unary("char", "OneMinus", "charOut");
        g.ColorProperty("metal", "Metal", 1.0, 0.77, 0.34);
        g.Color("soot", 0.04, 0.03, 0.03);
        g.MixColors("base", "metal", "soot", "char");
        g.Unary("metallic", "OneMinus", "char");
        g.Mix("rough", 0.22, 0.9, "char");

        g.Into("output", ("baseColor", "base"), ("metallic", "metallic"), ("roughness", "rough"), ("emission", "emission"),
            ("alpha", "keep"), ("alphaClip", 0.5), ("surface", "Cutout"), ("lighting", "Lit"), ("cull", "Both"));
    }

    // ---- galaxy ----

    // A spiral galaxy on a disc: two logarithmic arms from polar coordinates, gas swirled the way the arms
    // wind, dust lanes trailing them, a warm core, stars thickest in the arms and pink knots where stars
    // are being born.
    public static void Galaxy(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("polar", "PolarCoordinates", ("uv", "uv"), ("center", V2(0.5, 0.5)));
        g.Node("pp", "SeparateXYZ", "float2", ("vector", "polar"));
        g.Mul("theta", "pp.y", Tau);
        g.Add("rSafe", "pp.x", 0.02);
        g.Unary("logR", "Logarithm", "rSafe");
        g.Mul("armTheta", "theta", 2.0);
        g.Float("winding", "Winding", 4.5, 0, 10);
        g.Mul("logW", "logR", "winding");
        g.Sub("phase", "armTheta", "logW");
        g.Unary("phaseCos", "Cosine", "phase");
        g.Map("arm01", "phaseCos", -1, 1, 0, 1);
        g.Pow("arm", "arm01", 2.5);

        g.Node("swirl", "Twirl", ("uv", "uv"), ("center", V2(0.5, 0.5)), ("strength", -9.0));
        g.Node("gas", "NoiseTexture", ("uv", "swirl"), ("scale", 7.0), ("detail", 6.0), ("roughness", 0.6));

        g.Sub("dustPhase", "phase", 0.75);
        g.Unary("dustCos", "Cosine", "dustPhase");
        g.Map("dust01", "dustCos", -1, 1, 0, 1);
        g.Pow("dustLane", "dust01", 6.0);
        g.Mul("dust", "dustLane", "gas");
        g.Mul("dustS", "dust", 0.85);
        g.Unary("clear", "OneMinus", "dustS");

        g.Mul("armGas", "arm", "gas");
        g.Mul("armGasS", "armGas", 1.8);
        g.Add("armLight", "armGasS", 0.06);
        g.Mul("armClear", "armLight", "clear");
        g.Smooth("rimFade", "pp.x", 0.55, 1.0);
        g.Unary("disc", "OneMinus", "rimFade");
        g.Mul("armDisc", "armClear", "disc");
        g.Color("young", 0.55, 0.72, 1.0);
        g.Color("old", 1.0, 0.84, 0.62);
        g.Smooth("outerness", "pp.x", 0.05, 0.5);
        g.MixColors("armColor", "old", "young", "outerness");
        g.Node("armsLit", "Multiply", "color,color", ("a", "armColor"), ("b", "armDisc"));

        g.Mul("coreArg", "pp.x", -9.0);
        g.Unary("coreExp", "Exponential", "coreArg");
        g.Mul("coreS", "coreExp", 2.6);
        g.Color("coreColor", 1.0, 0.88, 0.7);
        g.Node("core", "Multiply", "color,color", ("a", "coreColor"), ("b", "coreS"));

        g.Node("starField", "Voronoi", ("uv", "uv"), ("cellDensity", 85.0), ("angleOffset", 1.0));
        g.Smooth("starOut", "starField.distance", 0.0, 0.11);
        g.Unary("starDot", "OneMinus", "starOut");
        g.Step("starPick", 0.62, "starField.cells");
        g.Mul("star1", "starDot", "starPick");
        g.Add("starArm", "arm", 0.25);
        g.Mul("star2", "star1", "starArm");
        g.Mul("star3", "star2", "disc");
        g.Mul("stars", "star3", 1.6);

        g.Node("knotField", "Voronoi", ("uv", "swirl"), ("cellDensity", 16.0), ("angleOffset", 3.0));
        g.Smooth("knotOut", "knotField.distance", 0.0, 0.16);
        g.Unary("knotDot", "OneMinus", "knotOut");
        g.Step("knotPick", 0.8, "knotField.cells");
        g.Mul("knot1", "knotDot", "knotPick");
        g.Mul("knot2", "knot1", "arm");
        g.Mul("knot3", "knot2", 2.2);
        g.Color("pink", 1.0, 0.45, 0.68);
        g.Node("knots", "Multiply", "color,color", ("a", "pink"), ("b", "knot3"));

        g.Node("sum1", "Add", "color,color", ("a", "armsLit"), ("b", "core"));
        g.Node("sum2", "Add", "color,color", ("a", "sum1"), ("b", "knots"));
        g.Node("sum3", "Add", "color,color", ("a", "sum2"), ("b", "stars"));
        g.Float("bright", "Brightness", 1.4, 0, 4);
        g.Node("galaxy", "Multiply", "color,color", ("a", "sum3"), ("b", "bright"));

        g.Into("output", ("baseColor", "galaxy"), ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // ---- ocean ----

    // A lagoon. Four travelling waves and a chop move the vertices; the same height, read again per pixel,
    // bends the normal so the sun glints off the crests. Clear water deepening in the troughs, crests lit
    // through, foam on the highest, the sky in it at grazing angles, and caustics: two cell patterns
    // drifting against each other, brightest far from their cells' middles, the light the waves focus.
    public static void Ocean(ShaderGraphBuilder g)
    {
        g.Node("time", "Time");
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("p", "SeparateXYZ", ("vector", "pos"));
        g.Pair("plane", "p.x", "p.y");
        Wave(g, "w1", 3.4, 1.0, 0.25, 1.5, 0.026);
        Wave(g, "w2", 4.9, -0.55, 1.0, 1.9, 0.017);
        Wave(g, "w3", 7.6, 0.2, -1.0, 2.7, 0.009);
        Wave(g, "w4", 11.5, -1.0, -0.45, 3.4, 0.005);
        g.Add("h12", "w1", "w2");
        g.Add("h34", "w3", "w4");
        g.Add("hWaves", "h12", "h34");
        g.Mul("chopT", "time", 0.12);
        g.Node("chopUV", "Add", "float2,float2", ("a", "plane"), ("b", "chopT"));
        g.Node("chopN", "GradientNoise", ("uv", "chopUV"), ("scale", 2.6));
        g.Sub("chopC", "chopN", 0.5);
        g.Mul("chop", "chopC", 0.016);
        g.Add("hRaw", "hWaves", "chop");
        g.Float("waveHeight", "Wave Height", 1.0, 0, 3);
        g.Mul("height", "hRaw", "waveHeight");
        g.Node("up", "Normal", ("space", "Object"));
        g.Node("lift", "Multiply", "float3,float3", ("a", "up"), ("b", "height"));
        g.Node("bump", "Bump", ("height", "height"), ("strength", 1.0));

        g.Smooth("crest", "height", -0.025, 0.045);
        g.ColorProperty("deep", "Deep", 0.03, 0.32, 0.46);
        g.ColorProperty("shallow", "Shallow", 0.2, 0.8, 0.8);
        g.MixColors("water", "deep", "shallow", "crest");

        g.Mul("foamT", "time", 0.05);
        g.Node("foamUV", "Add", "float2,float2", ("a", "plane"), ("b", "foamT"));
        g.Node("foamN", "SimpleNoise", ("uv", "foamUV"), ("scale", 34.0));
        g.Sub("foamC", "foamN", 0.5);
        g.Mul("foamJ", "foamC", 0.03);
        g.Add("foamH", "height", "foamJ");
        g.Smooth("foam", "foamH", 0.034, 0.056);
        g.Color("foamColor", 0.92, 0.97, 1.0);
        g.MixColors("watery", "water", "foamColor", "foam");

        g.Node("fresnel", "Fresnel", ("power", 5.0));
        g.Mul("skyAmt", "fresnel", 0.6);
        g.Color("skyColor", 0.45, 0.55, 0.85);
        g.MixColors("sea", "watery", "skyColor", "skyAmt");

        g.Mul("drift1", "time", 0.06);
        g.Node("causticUV1", "Add", "float2,float2", ("a", "plane"), ("b", "drift1"));
        g.Mul("spin1", "time", 0.7);
        g.Node("caustic1", "Voronoi", ("uv", "causticUV1"), ("angleOffset", "spin1"), ("cellDensity", 2.2));
        g.Mul("drift2", "time", -0.045);
        g.Node("causticUV2", "Add", "float2,float2", ("a", "plane"), ("b", "drift2"));
        g.Mul("spin2", "time", -0.5);
        g.Node("caustic2", "Voronoi", ("uv", "causticUV2"), ("angleOffset", "spin2"), ("cellDensity", 3.1));
        g.Pow("web1", "caustic1.distance", 6.0);
        g.Pow("web2", "caustic2.distance", 6.0);
        g.Add("web", "web1", "web2");
        g.Float("causticAmt", "Caustics", 7.0, 0, 20);
        g.Unary("noFoam", "OneMinus", "foam");
        g.Mul("webLit", "web", "causticAmt");
        g.Mul("webSeen", "webLit", "noFoam");
        g.Color("causticColor", 0.6, 1.0, 0.95);
        g.Node("caustics", "Multiply", "color,color", ("a", "causticColor"), ("b", "webSeen"));
        g.Mul("glowAmt", "crest", 0.2);
        g.Node("glowCrest", "Multiply", "color,color", ("a", "shallow"), ("b", "glowAmt"));
        g.Node("glow", "Add", "color,color", ("a", "glowCrest"), ("b", "caustics"));
        g.Mix("rough", 0.03, 0.55, "foam");

        g.Into("output", ("baseColor", "sea"), ("roughness", "rough"), ("normal", "bump"), ("emission", "glow"),
            ("vertexOffset", "lift"), ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Both"));
    }

    // One wave: a sine travelling across the plane, k waves a metre along its heading.
    private static string Wave(ShaderGraphBuilder g, string id, double k, double dx, double dy, double speed, double amplitude)
    {
        double length = System.Math.Sqrt(dx * dx + dy * dy);
        g.Node(id + "D", "DotProduct", "float2,float2", ("a", "plane"), ("b", V2(dx / length * k, dy / length * k)));
        g.Mul(id + "T", "time", speed);
        g.Add(id + "P", id + "D", id + "T");
        g.Unary(id + "S", "Sine", id + "P");
        return g.Mul(id, id + "S", amplitude);
    }

    // ---- rose window ----

    // Stained glass in a round window: twelve mirrored sectors make a kaleidoscope of one pane pattern, lead
    // runs wherever a small step lands in another pane, stone tracery rings and spokes, every pane its own
    // jewel colour from a narrow palette, and the light behind it.
    public static void RoseWindow(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("polar", "PolarCoordinates", ("uv", "uv"), ("center", V2(0.5, 0.5)));
        g.Node("pp", "SeparateXYZ", "float2", ("vector", "polar"));
        g.Mul("sectorN", "pp.y", 12.0);
        g.Unary("sectorF", "Fraction", "sectorN");
        g.Sub("sectorC", "sectorF", 0.5);
        g.Unary("sectorA", "Absolute", "sectorC");
        g.Mul("across", "sectorA", 2.0);
        g.Mul("acrossS", "across", 0.9);
        g.Mul("outS", "pp.x", 3.2);
        g.Pair("q", "acrossS", "outS");
        g.Node("panes", "Voronoi", ("uv", "q"), ("cellDensity", 2.4), ("angleOffset", 1.3));
        g.Node("qx", "Add", "float2,float2", ("a", "q"), ("b", V2(0.035, 0.0)));
        g.Node("qy", "Add", "float2,float2", ("a", "q"), ("b", V2(0.0, 0.035)));
        g.Node("panesX", "Voronoi", ("uv", "qx"), ("cellDensity", 2.4), ("angleOffset", 1.3));
        g.Node("panesY", "Voronoi", ("uv", "qy"), ("cellDensity", 2.4), ("angleOffset", 1.3));
        g.Sub("dX", "panes.cells", "panesX.cells");
        g.Sub("dY", "panes.cells", "panesY.cells");
        g.Unary("aX", "Absolute", "dX");
        g.Unary("aY", "Absolute", "dY");
        g.Max("dAny", "aX", "aY");
        g.Step("leadPanes", 0.0001, "dAny");

        g.Smooth("spokes", "across", 0.93, 0.97);
        g.Smooth("pastMedallion", "pp.x", 0.26, 0.28);
        g.Mul("spokesOut", "spokes", "pastMedallion");
        Ring(g, "ring1", "pp.x", 0.27, 0.012);
        Ring(g, "ring2", "pp.x", 0.62, 0.01);
        g.Smooth("frame", "pp.x", 0.94, 0.955);
        g.Max("l1", "leadPanes", "spokesOut");
        g.Max("l2", "l1", "ring1");
        g.Max("l3", "l2", "ring2");
        g.Max("lead", "l3", "frame");

        g.Mul("hueCell", "panes.cells", 0.55);
        g.Mul("hueRing", "pp.x", 0.25);
        g.Add("hue1", "hueCell", "hueRing");
        g.Add("hue2", "hue1", 0.52);
        g.Unary("hue", "Fraction", "hue2");
        g.Node("noise", "SimpleNoise", ("uv", "uv"), ("scale", 160.0));
        g.Map("grain", "noise", 0, 1, 0.7, 1.1);
        g.Mul("vignetteD", "panes.distance", 0.7);
        g.Unary("vignette", "OneMinus", "vignetteD");
        g.Mul("value", "grain", "vignette");
        g.Node("glass", "CombineHSV", ("hue", "hue"), ("saturation", 0.82), ("value", "value"));
        g.Float("light", "Backlight", 1.8, 0, 6);
        g.Node("lit", "Multiply", "color,color", ("a", "glass"), ("b", "light"));
        g.Color("leadColor", 0.05, 0.05, 0.06);
        g.Color("stone", 0.42, 0.4, 0.37);
        g.MixColors("leaded", "lit", "leadColor", "lead");
        g.MixColors("window", "leaded", "stone", "frame");

        g.Into("output", ("baseColor", "window"), ("surface", "Opaque"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // ---- marble ----

    // Polished white marble with grey veins and seams of gold. The turbulence is three noises off three
    // planes summed, a solid texture rather than a wrapped one, so the veins run on round any shape.
    public static void Marble(ShaderGraphBuilder g)
    {
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("p", "SeparateXYZ", ("vector", "pos"));
        g.Pair("qa", "p.x", "p.y");
        g.Pair("qb", "p.y", "p.z");
        g.Pair("qc", "p.z", "p.x");
        g.Node("ta", "NoiseTexture", ("uv", "qa"), ("scale", 1.7), ("detail", 6.0), ("roughness", 0.55));
        g.Node("tb", "NoiseTexture", ("uv", "qb"), ("scale", 1.7), ("detail", 6.0), ("roughness", 0.55));
        g.Node("tc", "NoiseTexture", ("uv", "qc"), ("scale", 1.7), ("detail", 6.0), ("roughness", 0.55));
        g.Add("tab", "ta", "tb");
        g.Add("turb", "tab", "tc");

        g.Node("veinAxis", "DotProduct", ("a", "pos"), ("b", V3(5.0, 3.0, 1.5)));
        g.Mul("veinTurb", "turb", 5.0);
        g.Add("veinArg", "veinAxis", "veinTurb");
        g.Unary("veinSin", "Sine", "veinArg");
        g.Unary("veinAbs", "Absolute", "veinSin");
        g.Smooth("veinOut", "veinAbs", 0.0, 0.14);
        g.Unary("vein", "OneMinus", "veinOut");
        g.Smooth("cloudOut", "veinAbs", 0.0, 0.5);
        g.Unary("cloud", "OneMinus", "cloudOut");

        g.Node("goldAxis", "DotProduct", ("a", "pos"), ("b", V3(-1.6, 4.0, 1.2)));
        g.Mul("goldTurb", "turb", 3.6);
        g.Add("goldArg", "goldAxis", "goldTurb");
        g.Add("goldShift", "goldArg", 1.3);
        g.Unary("goldSin", "Sine", "goldShift");
        g.Unary("goldAbs", "Absolute", "goldSin");
        g.Float("goldWidth", "Gold Width", 0.07, 0.005, 0.3);
        g.Smooth("goldOut", "goldAbs", 0.0, "goldWidth");
        g.Unary("gold", "OneMinus", "goldOut");

        g.Color("white", 0.94, 0.93, 0.9);
        g.Color("grey", 0.45, 0.46, 0.5);
        g.Color("dark", 0.17, 0.17, 0.2);
        g.ColorProperty("goldColor", "Gold", 1.0, 0.77, 0.34);
        g.Mul("cloudAmt", "cloud", 0.55);
        g.MixColors("m1", "white", "grey", "cloudAmt");
        g.Mul("veinAmt", "vein", 0.8);
        g.MixColors("m2", "m1", "dark", "veinAmt");
        g.MixColors("m3", "m2", "goldColor", "gold");
        g.Mix("rough", 0.12, 0.2, "gold");

        g.Into("output", ("baseColor", "m3"), ("metallic", "gold"), ("roughness", "rough"),
            ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // ---- soap bubble ----

    // A thin film: its thickness drifts in slow noise and grows toward the edge, and the thickness picks the
    // colour, the way interference does. Nearly clear face on, more there at the edges.
    public static void Bubble(ShaderGraphBuilder g)
    {
        g.Node("time", "Time");
        g.Node("pos", "Position", ("space", "Object"));
        g.Node("p", "SeparateXYZ", ("vector", "pos"));
        g.Mul("riseA", "time", 0.08);
        g.Add("ya", "p.y", "riseA");
        g.Pair("qa", "p.x", "ya");
        g.Mul("riseB", "time", -0.05);
        g.Add("yb", "p.y", "riseB");
        g.Pair("qb", "p.z", "yb");
        g.Node("na", "NoiseTexture", ("uv", "qa"), ("scale", 5.0), ("detail", 3.0), ("roughness", 0.5));
        g.Node("nb", "NoiseTexture", ("uv", "qb"), ("scale", 4.0), ("detail", 3.0), ("roughness", 0.5));
        g.Add("film", "na", "nb");
        g.Node("fresnel", "Fresnel", ("power", 1.4));
        g.Mul("filmS", "film", 1.3);
        g.Mul("edgeS", "fresnel", 0.8);
        g.Add("thick", "filmS", "edgeS");
        g.Mul("shiftT", "time", 0.03);
        g.Add("hueRaw", "thick", "shiftT");
        g.Unary("hue", "Fraction", "hueRaw");
        g.Node("sheen", "CombineHSV", ("hue", "hue"), ("saturation", 0.62), ("value", 1.0));
        g.Mul("alphaEdge", "fresnel", 0.55);
        g.Add("alpha", "alphaEdge", 0.05);
        g.Mul("glowAmt", "fresnel", 0.35);
        g.Add("glowBase", "glowAmt", 0.06);
        g.Node("glow", "Multiply", "color,color", ("a", "sheen"), ("b", "glowBase"));

        g.Into("output", ("baseColor", "sheen"), ("alpha", "alpha"), ("roughness", 0.04), ("emission", "glow"),
            ("surface", "Transparent"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // ---- portal ----

    // A tunnel through the disc: depth from one over the radius, walls of noise read round a circle so
    // they close without a seam, twisting and flowing inward; violet to blue, a white-hot far end and a
    // bright lip at the ring.
    public static void Portal(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("time", "Time");
        g.Node("centred", "Subtract", "float2,float2", ("a", "uv"), ("b", V2(0.5, 0.5)));
        g.Node("c", "SeparateXYZ", "float2", ("vector", "centred"));
        g.Node("len", "Length", "float2", ("vector", "centred"));
        g.Mul("r", "len", 2.0);
        g.Node("angle", "Arctangent2", ("y", "c.y"), ("x", "c.x"));

        g.Add("rSafe", "r", 0.07);
        g.Unary("depth", "Reciprocal", "rSafe");
        g.Float("speed", "Speed", 1.0, 0, 4);
        g.Mul("flowT", "time", "speed");
        g.Mul("twist", "depth", 0.35);
        g.Mul("spinT", "flowT", 0.5);
        g.Add("a1", "angle", "twist");
        g.Add("turn", "a1", "spinT");

        g.Unary("cosA", "Cosine", "turn");
        g.Unary("sinA", "Sine", "turn");
        g.Mul("along1", "depth", 0.5);
        g.Mul("flowS", "flowT", 1.2);
        g.Add("along", "along1", "flowS");
        g.Mul("cx", "cosA", 1.6);
        g.Mul("sy", "sinA", 1.6);
        g.Add("wallX", "cx", "along");
        g.Pair("wall", "wallX", "sy");
        g.Node("wallN", "NoiseTexture", ("uv", "wall"), ("scale", 1.0), ("detail", 5.0), ("roughness", 0.6));
        g.Mul("cx4", "cosA", 4.5);
        g.Mul("sy4", "sinA", 4.5);
        g.Mul("along2", "along", 2.0);
        g.Add("fineX", "cx4", "along2");
        g.Pair("fineQ", "fineX", "sy4");
        g.Node("fineN", "NoiseTexture", ("uv", "fineQ"), ("scale", 1.0), ("detail", 3.0), ("roughness", 0.5));
        g.Pow("energyP", "wallN", 2.2);
        g.Mul("energyA", "energyP", 2.4);
        g.Mul("energyB", "fineN", 0.5);
        g.Add("energy", "energyA", "energyB");

        g.Mul("hueN", "wallN", 0.22);
        g.Mul("hueR", "r", -0.08);
        g.Add("hue1", "hueN", "hueR");
        g.Add("hue2", "hue1", 0.68);
        g.Unary("hue", "Fraction", "hue2");
        g.Node("swirlColor", "CombineHSV", ("hue", "hue"), ("saturation", 0.85), ("value", 1.0));
        g.Mul("rLight", "r", 0.9);
        g.Add("bright1", "rLight", 0.35);
        g.Mul("bright", "energy", "bright1");
        g.Node("swirl", "Multiply", "color,color", ("a", "swirlColor"), ("b", "bright"));

        g.Mul("coreArg", "r", -7.0);
        g.Unary("coreExp", "Exponential", "coreArg");
        g.Mul("coreS", "coreExp", 3.0);
        g.Color("coreColor", 1.0, 0.85, 1.0);
        g.Node("core", "Multiply", "color,color", ("a", "coreColor"), ("b", "coreS"));
        g.Smooth("rim", "r", 0.86, 0.99);
        g.Mul("rimS", "rim", 2.2);
        g.ColorProperty("rimColor", "Rim", 0.45, 0.85, 1.0);
        g.Node("lip", "Multiply", "color,color", ("a", "rimColor"), ("b", "rimS"));
        g.Node("s1", "Add", "color,color", ("a", "swirl"), ("b", "core"));
        g.Node("s2", "Add", "color,color", ("a", "s1"), ("b", "lip"));

        g.Into("output", ("baseColor", "s2"), ("surface", "Opaque"), ("lighting", "Unlit"), ("cull", "Both"));
    }

    // The portal's ring: dark iron carved with runes, about half its cells carved, each with its own glyph,
    // on both flat faces, lit by a pulse running round.
    public static void RuneRing(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("uvs", "SeparateXYZ", "float2", ("vector", "uv"));
        g.Node("time", "Time");
        g.Mul("cellsN", "uvs.x", 32.0);
        g.Unary("cell", "Floor", "cellsN");
        g.Unary("inCell", "Fraction", "cellsN");
        g.Pair("cellQ", "cell", 0.5);
        g.Node("pick", "SimpleNoise", ("uv", "cellQ"), ("scale", 1.0));
        g.Step("carved", 0.45, "pick");

        g.Mul("glyphV", "uvs.y", 4.0);
        g.Pair("glyphQ", "cellsN", "glyphV");
        g.Node("glyphField", "Voronoi", ("uv", "glyphQ"), ("cellDensity", 2.2), ("angleOffset", 2.0));
        g.Smooth("strokeIn", "glyphField.distance", 0.16, 0.2);
        g.Smooth("strokeOut", "glyphField.distance", 0.24, 0.28);
        g.Sub("stroke", "strokeIn", "strokeOut");

        g.Mul("faceArg", "uvs.y", Tau);
        g.Unary("faceSin", "Sine", "faceArg");
        g.Unary("faceAbs", "Absolute", "faceSin");
        g.Smooth("face", "faceAbs", 0.55, 0.8);
        g.Sub("inCellC", "inCell", 0.5);
        g.Unary("inCellA", "Absolute", "inCellC");
        g.Smooth("cellEdge", "inCellA", 0.38, 0.45);
        g.Unary("cellInside", "OneMinus", "cellEdge");
        g.Mul("r1", "stroke", "carved");
        g.Mul("r2", "r1", "face");
        g.Mul("rune", "r2", "cellInside");

        g.Mul("pulseCell", "cell", 0.6);
        g.Mul("pulseT", "time", 2.0);
        g.Sub("pulseArg", "pulseT", "pulseCell");
        g.Unary("pulseSin", "Sine", "pulseArg");
        g.Map("pulse", "pulseSin", -1, 1, 0.35, 1.0);
        g.Mul("runeLit", "rune", "pulse");
        g.Float("runeGlow", "Rune Glow", 5.0, 0, 20);
        g.Mul("runeS", "runeLit", "runeGlow");
        g.ColorProperty("runeColor", "Runes", 0.55, 0.45, 1.0);
        g.Node("glow", "Multiply", "color,color", ("a", "runeColor"), ("b", "runeS"));
        g.Color("iron", 0.16, 0.16, 0.19);

        g.Into("output", ("baseColor", "iron"), ("metallic", 0.85), ("roughness", 0.38), ("emission", "glow"),
            ("surface", "Opaque"), ("lighting", "Lit"), ("cull", "Back"));
    }

    // ---- fire ----

    // Flames on crossed cards: two noises rising at different speeds, cut to a tongue wide at the bottom
    // and licking to a point, thinned where the noise is low, coloured red to orange to white-yellow by heat.
    public static void Flame(ShaderGraphBuilder g)
    {
        g.Node("uv", "UV");
        g.Node("uvs", "SeparateXYZ", "float2", ("vector", "uv"));
        g.Node("time", "Time");
        g.Unary("up", "OneMinus", "uvs.y");
        g.Sub("sideC", "uvs.x", 0.5);
        g.Unary("side", "Absolute", "sideC");

        g.Mul("riseA", "time", 1.6);
        g.Mul("upA", "up", 2.2);
        g.Sub("ya", "upA", "riseA");
        g.Mul("xa", "uvs.x", 3.0);
        g.Pair("qa", "xa", "ya");
        g.Node("na", "NoiseTexture", ("uv", "qa"), ("scale", 1.0), ("detail", 4.0), ("roughness", 0.55));
        g.Mul("riseB", "time", 2.7);
        g.Mul("upB", "up", 4.5);
        g.Sub("yb", "upB", "riseB");
        g.Mul("xb", "uvs.x", 6.5);
        g.Add("xbs", "xb", 3.3);
        g.Pair("qb", "xbs", "yb");
        g.Node("nb", "NoiseTexture", ("uv", "qb"), ("scale", 1.0), ("detail", 3.0), ("roughness", 0.5));
        g.Mul("naS", "na", 0.65);
        g.Mul("nbS", "nb", 0.35);
        g.Add("n", "naS", "nbS");

        g.Sub("narrowing", 1.08, "up");
        g.Mul("halfWidth", "narrowing", 0.42);
        g.Sub("nC", "n", 0.5);
        g.Mul("wobble", "nC", 0.3);
        g.Add("sideW", "side", "wobble");
        g.Div("across", "sideW", "halfWidth");
        g.Smooth("acrossOut", "across", 0.25, 1.0);
        g.Unary("tongueX", "OneMinus", "acrossOut");
        g.Smooth("topOut", "up", 0.25, 0.95);
        g.Unary("tongueY", "OneMinus", "topOut");
        g.Smooth("bottomIn", "up", 0.0, 0.06);
        g.Mul("shape1", "tongueX", "tongueY");
        g.Mul("shape", "shape1", "bottomIn");
        g.Mul("shapeS", "shape", 1.7);
        g.Unary("thin", "OneMinus", "n");
        g.Mul("thinS", "thin", 0.85);
        g.Sub("fireRaw", "shapeS", "thinS");
        g.Unary("fire", "Saturate", "fireRaw");

        g.Color("red", 0.85, 0.1, 0.02);
        g.Color("orange", 1.0, 0.45, 0.06);
        g.Color("yellow", 1.0, 0.88, 0.55);
        g.Smooth("toOrange", "fire", 0.08, 0.45);
        g.MixColors("c1", "red", "orange", "toOrange");
        g.Smooth("toYellow", "fire", 0.5, 0.9);
        g.MixColors("c2", "c1", "yellow", "toYellow");
        g.Float("heat", "Heat", 2.4, 0, 8);
        g.Mul("fireS", "fire", "heat");
        g.Node("flame", "Multiply", "color,color", ("a", "c2"), ("b", "fireS"));

        g.Into("output", ("baseColor", "flame"), ("surface", "Additive"), ("lighting", "Unlit"), ("cull", "Both"));
    }
}
