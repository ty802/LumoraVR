// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The kinds of quantity scripts can carry, each with its units. Factors are exact where a definition
// makes them so: an inch is 0.0254 m, a pound 0.45359237 kg.

public readonly struct DistanceKind : IScriptQuantityKind
{
    public static string Name => "distance";
    public static ScriptUnit[] Units { get; } =
    {
        new("m", 1d, "meter", "meters", "metre", "metres") { Name = "metre", Plural = "metres", Auto = true },
        new("km", 1000d, "kilometer", "kilometers") { Name = "kilometre", Plural = "kilometres", Auto = true },
        new("cm", 0.01d, "centimeter", "centimeters") { Name = "centimetre", Plural = "centimetres", Auto = true },
        new("mm", 0.001d, "millimeter", "millimeters") { Name = "millimetre", Plural = "millimetres", Auto = true },
        new("um", 1e-6, "µm", "micrometer", "micrometers") { Name = "micrometre", Plural = "micrometres", Auto = true },
        new("nm", 1e-9, "nanometer", "nanometers") { Name = "nanometre", Plural = "nanometres", Auto = true },
        new("in", 0.0254d, "inch", "inches", "\"") { Name = "inch", Plural = "inches" },
        new("ft", 0.3048d, "foot", "feet", "'") { Name = "foot", Plural = "feet" },
        new("yd", 0.9144d, "yard", "yards") { Name = "yard", Plural = "yards" },
        new("mi", 1609.344d, "mile", "miles") { Name = "mile", Plural = "miles" },
        new("nmi", 1852d, "nautical mile", "nautical miles") { Name = "nautical mile", Plural = "nautical miles" },
        new("au", 149597870700d, "astronomical unit") { Name = "astronomical unit", Plural = "astronomical units" },
        new("ly", 9460730472580800d, "light year", "light years") { Name = "light year", Plural = "light years" },
    };
}

public readonly struct AngleKind : IScriptQuantityKind
{
    public static string Name => "angle";
    public static ScriptUnit[] Units { get; } =
    {
        new("rad", 1d, "radian", "radians") { Name = "radian", Plural = "radians" },
        new("deg", Math.PI / 180d, "°", "degree", "degrees") { Name = "degree", Plural = "degrees", Auto = true },
        new("grad", Math.PI / 200d, "gon", "gradian", "gradians") { Name = "gradian", Plural = "gradians" },
        new("turn", Math.PI * 2d, "turns", "rev", "revolution", "revolutions") { Name = "turn", Plural = "turns" },
        new("arcmin", Math.PI / 10800d, "arcminute", "arcminutes") { Name = "arcminute", Plural = "arcminutes" },
        new("arcsec", Math.PI / 648000d, "arcsecond", "arcseconds") { Name = "arcsecond", Plural = "arcseconds" },
    };
}

public readonly struct MassKind : IScriptQuantityKind
{
    public static string Name => "mass";
    public static ScriptUnit[] Units { get; } =
    {
        new("kg", 1d, "kilogram", "kilograms") { Name = "kilogram", Plural = "kilograms", Auto = true },
        new("g", 0.001d, "gram", "grams") { Name = "gram", Plural = "grams", Auto = true },
        new("mg", 1e-6, "milligram", "milligrams") { Name = "milligram", Plural = "milligrams", Auto = true },
        new("t", 1000d, "tonne", "tonnes") { Name = "tonne", Plural = "tonnes", Auto = true },
        new("lb", 0.45359237d, "lbs", "pound", "pounds") { Name = "pound", Plural = "pounds" },
        new("oz", 0.028349523125d, "ounce", "ounces") { Name = "ounce", Plural = "ounces" },
        new("st", 6.35029318d, "stone") { Name = "stone", Plural = "stone" },
    };
}

public readonly struct SpeedKind : IScriptQuantityKind
{
    public static string Name => "speed";
    public static ScriptUnit[] Units { get; } =
    {
        new("m/s", 1d, "mps") { Name = "metre per second", Plural = "metres per second" },
        new("km/h", 1000d / 3600d, "kph", "kmh") { Name = "kilometre per hour", Plural = "kilometres per hour" },
        new("mph", 0.44704d) { Name = "mile per hour", Plural = "miles per hour" },
        new("kn", 1852d / 3600d, "knot", "knots") { Name = "knot", Plural = "knots" },
        new("ft/s", 0.3048d, "fps") { Name = "foot per second", Plural = "feet per second" },
    };
}

public readonly struct AccelerationKind : IScriptQuantityKind
{
    public static string Name => "acceleration";
    public static ScriptUnit[] Units { get; } =
    {
        new("m/s2", 1d, "m/s²") { Name = "metre per second squared", Plural = "metres per second squared" },
        new("g0", 9.80665d, "gee") { Name = "gee", Plural = "gees" },
        new("ft/s2", 0.3048d, "ft/s²") { Name = "foot per second squared", Plural = "feet per second squared" },
    };
}

public readonly struct TemperatureKind : IScriptQuantityKind
{
    public static string Name => "temperature";
    public static ScriptUnit[] Units { get; } =
    {
        new("K", 1d, "kelvin") { Name = "kelvin", Plural = "kelvins" },
        new("C", 1d, 273.15d, "°C", "celsius") { Name = "degree Celsius", Plural = "degrees Celsius", Auto = true },
        new("F", 5d / 9d, 459.67d * 5d / 9d, "°F", "fahrenheit") { Name = "degree Fahrenheit", Plural = "degrees Fahrenheit" },
    };
}

public readonly struct AreaKind : IScriptQuantityKind
{
    public static string Name => "area";
    public static ScriptUnit[] Units { get; } =
    {
        new("m2", 1d, "m²") { Name = "square metre", Plural = "square metres", Auto = true },
        new("km2", 1e6, "km²") { Name = "square kilometre", Plural = "square kilometres", Auto = true },
        new("cm2", 1e-4, "cm²") { Name = "square centimetre", Plural = "square centimetres", Auto = true },
        new("mm2", 1e-6, "mm²") { Name = "square millimetre", Plural = "square millimetres", Auto = true },
        new("ha", 1e4, "hectare", "hectares") { Name = "hectare", Plural = "hectares" },
        new("acre", 4046.8564224d, "acres") { Name = "acre", Plural = "acres" },
        new("ft2", 0.09290304d, "ft²") { Name = "square foot", Plural = "square feet" },
        new("in2", 0.00064516d, "in²") { Name = "square inch", Plural = "square inches" },
    };
}

public readonly struct VolumeKind : IScriptQuantityKind
{
    public static string Name => "volume";
    public static ScriptUnit[] Units { get; } =
    {
        new("m3", 1d, "m³") { Name = "cubic metre", Plural = "cubic metres", Auto = true },
        new("l", 0.001d, "L", "liter", "liters", "litre", "litres") { Name = "litre", Plural = "litres", Auto = true },
        new("ml", 1e-6, "mL", "milliliter", "milliliters") { Name = "millilitre", Plural = "millilitres", Auto = true },
        new("cm3", 1e-6, "cm³", "cc") { Name = "cubic centimetre", Plural = "cubic centimetres" },
        new("gal", 0.003785411784d, "gallon", "gallons") { Name = "gallon", Plural = "gallons" },
        new("qt", 0.000946352946d, "quart", "quarts") { Name = "quart", Plural = "quarts" },
        new("pt", 0.000473176473d, "pint", "pints") { Name = "pint", Plural = "pints" },
        new("floz", 2.95735295625e-5, "fl oz") { Name = "fluid ounce", Plural = "fluid ounces" },
        new("ft3", 0.028316846592d, "ft³") { Name = "cubic foot", Plural = "cubic feet" },
    };
}

public readonly struct ForceKind : IScriptQuantityKind
{
    public static string Name => "force";
    public static ScriptUnit[] Units { get; } =
    {
        new("N", 1d, "newton", "newtons") { Name = "newton", Plural = "newtons", Auto = true },
        new("kN", 1000d, "kilonewton", "kilonewtons") { Name = "kilonewton", Plural = "kilonewtons", Auto = true },
        new("lbf", 4.4482216152605d, "pound force") { Name = "pound force", Plural = "pounds force" },
        new("dyn", 1e-5, "dyne", "dynes") { Name = "dyne", Plural = "dynes" },
    };
}

public readonly struct EnergyKind : IScriptQuantityKind
{
    public static string Name => "energy";
    public static ScriptUnit[] Units { get; } =
    {
        new("J", 1d, "joule", "joules") { Name = "joule", Plural = "joules", Auto = true },
        new("kJ", 1000d, "kilojoule", "kilojoules") { Name = "kilojoule", Plural = "kilojoules", Auto = true },
        new("MJ", 1e6, "megajoule", "megajoules") { Name = "megajoule", Plural = "megajoules", Auto = true },
        new("cal", 4.184d, "calorie", "calories") { Name = "calorie", Plural = "calories" },
        new("kcal", 4184d, "kilocalorie", "kilocalories") { Name = "kilocalorie", Plural = "kilocalories" },
        new("Wh", 3600d, "watt hour", "watt hours") { Name = "watt hour", Plural = "watt hours" },
        new("kWh", 3.6e6, "kilowatt hour", "kilowatt hours") { Name = "kilowatt hour", Plural = "kilowatt hours" },
        new("eV", 1.602176634e-19, "electronvolt", "electronvolts") { Name = "electronvolt", Plural = "electronvolts" },
    };
}

public readonly struct PowerKind : IScriptQuantityKind
{
    public static string Name => "power";
    public static ScriptUnit[] Units { get; } =
    {
        new("W", 1d, "watt", "watts") { Name = "watt", Plural = "watts", Auto = true },
        new("kW", 1000d, "kilowatt", "kilowatts") { Name = "kilowatt", Plural = "kilowatts", Auto = true },
        new("MW", 1e6, "megawatt", "megawatts") { Name = "megawatt", Plural = "megawatts", Auto = true },
        new("hp", 745.69987158227022d, "horsepower") { Name = "horsepower", Plural = "horsepower" },
    };
}

public readonly struct FrequencyKind : IScriptQuantityKind
{
    public static string Name => "frequency";
    public static ScriptUnit[] Units { get; } =
    {
        new("Hz", 1d, "hertz") { Name = "hertz", Plural = "hertz", Auto = true },
        new("kHz", 1e3, "kilohertz") { Name = "kilohertz", Plural = "kilohertz", Auto = true },
        new("MHz", 1e6, "megahertz") { Name = "megahertz", Plural = "megahertz", Auto = true },
        new("GHz", 1e9, "gigahertz") { Name = "gigahertz", Plural = "gigahertz", Auto = true },
        new("rpm", 1d / 60d) { Name = "revolution per minute", Plural = "revolutions per minute" },
    };
}

public readonly struct PressureKind : IScriptQuantityKind
{
    public static string Name => "pressure";
    public static ScriptUnit[] Units { get; } =
    {
        new("Pa", 1d, "pascal", "pascals") { Name = "pascal", Plural = "pascals", Auto = true },
        new("kPa", 1000d, "kilopascal", "kilopascals") { Name = "kilopascal", Plural = "kilopascals", Auto = true },
        new("MPa", 1e6, "megapascal", "megapascals") { Name = "megapascal", Plural = "megapascals", Auto = true },
        new("bar", 1e5) { Name = "bar", Plural = "bars" },
        new("atm", 101325d, "atmosphere", "atmospheres") { Name = "atmosphere", Plural = "atmospheres" },
        new("psi", 6894.757293168d) { Name = "pound per square inch", Plural = "pounds per square inch" },
        new("mmHg", 133.322387415d) { Name = "millimetre of mercury", Plural = "millimetres of mercury" },
    };
}

public readonly struct DataSizeKind : IScriptQuantityKind
{
    public static string Name => "datasize";
    public static ScriptUnit[] Units { get; } =
    {
        new("B", 1d, "byte", "bytes") { Name = "byte", Plural = "bytes", Auto = true },
        new("kB", 1e3, "KB", "kilobyte", "kilobytes") { Name = "kilobyte", Plural = "kilobytes", Auto = true },
        new("MB", 1e6, "megabyte", "megabytes") { Name = "megabyte", Plural = "megabytes", Auto = true },
        new("GB", 1e9, "gigabyte", "gigabytes") { Name = "gigabyte", Plural = "gigabytes", Auto = true },
        new("TB", 1e12, "terabyte", "terabytes") { Name = "terabyte", Plural = "terabytes", Auto = true },
        new("KiB", 1024d, "kibibyte", "kibibytes") { Name = "kibibyte", Plural = "kibibytes" },
        new("MiB", 1048576d, "mebibyte", "mebibytes") { Name = "mebibyte", Plural = "mebibytes" },
        new("GiB", 1073741824d, "gibibyte", "gibibytes") { Name = "gibibyte", Plural = "gibibytes" },
        new("TiB", 1099511627776d, "tebibyte", "tebibytes") { Name = "tebibyte", Plural = "tebibytes" },
        new("bit", 0.125d, "bits") { Name = "bit", Plural = "bits" },
    };
}
